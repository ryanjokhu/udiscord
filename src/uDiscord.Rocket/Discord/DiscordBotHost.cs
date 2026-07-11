using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UDiscord.Core.Models;
using UDiscord.Core.Security;
using UDiscord.Rocket.Configuration;
using UDiscord.Rocket.Game;
using UDiscord.Rocket.Infrastructure;
using UDiscord.Rocket.Persistence;

namespace UDiscord.Rocket.Discord
{
    public sealed class DiscordBotHost : IDisposable
    {
        private sealed class OutboundMessage
        {
            public ulong ChannelId { get; set; }
            public string Content { get; set; }
        }

        private readonly Func<UDiscordConfiguration> _configuration;
        private readonly MainThreadDispatcher _dispatcher;
        private readonly PlayerResolver _players;
        private readonly ModerationService _moderation;
        private readonly CaseStore _cases;
        private readonly SlidingWindowRateLimiter _messageRateLimiter;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly object _startSync = new object();

        private DiscordRestClient _rest;
        private DiscordGatewayClient _gateway;
        private DiscordInteractionHandler _interactions;
        private BoundedWorker<OutboundMessage> _outbound;
        private GameBridge _gameBridge;
        private bool _started;
        private int _presenceUpdatePending;
        private DateTime _lastQueueWarningUtc = DateTime.MinValue;
        private ulong _registeredApplicationId;
        private bool _serverOnlineNoticeSent;

        public bool SuppressOfflineNotice { get; set; }
        public bool SuppressOnlineNotice { get; set; }

        public DiscordBotHost(
            Func<UDiscordConfiguration> configuration,
            MainThreadDispatcher dispatcher,
            PlayerResolver players,
            ModerationService moderation,
            CaseStore cases)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
            _cases = cases ?? throw new ArgumentNullException(nameof(cases));

            UDiscordConfiguration config = _configuration();
            _messageRateLimiter = new SlidingWindowRateLimiter(
                Math.Max(1, config.RateLimits.DiscordMessagesPerWindow),
                TimeSpan.FromSeconds(Math.Max(1, config.RateLimits.DiscordMessageWindowSeconds)));
        }

        public BotConnectionState State => _gateway?.State ?? BotConnectionState.Disabled;
        public bool IsOnline => _gateway?.IsReady == true;
        public int OutboundQueueCount => _outbound?.Count ?? 0;
        public ulong ApplicationId => _gateway?.ApplicationId ?? 0;

        public void AttachGameBridge(GameBridge gameBridge)
        {
            _gameBridge = gameBridge ?? throw new ArgumentNullException(nameof(gameBridge));
        }

        public void Start()
        {
            lock (_startSync)
            {
                if (_started) return;
                _started = true;

                UDiscordConfiguration config = _configuration();
                if (config == null || !config.Enabled)
                {
                    PluginLog.Info("Discord integration is disabled by configuration.");
                    return;
                }

                string token = config.ResolveBotToken();
                _rest = new DiscordRestClient(token, TimeSpan.FromSeconds(config.Discord.RestRequestTimeoutSeconds));
                _gateway = new DiscordGatewayClient(
                    _rest,
                    token,
                    config.Discord.GatewayIntents,
                    config.Discord.MaximumGatewayPayloadBytes,
                    config.Discord.ReconnectMinimumSeconds,
                    config.Discord.ReconnectMaximumSeconds);
                _gateway.DispatchReceived += OnDispatchAsync;
                _gateway.StateChanged += OnStateChanged;

                _outbound = new BoundedWorker<OutboundMessage>(
                    "Discord outbound worker",
                    config.RateLimits.OutboundQueueCapacity,
                    SendOutboundAsync);
                _outbound.Start(_lifetime.Token);

                _interactions = new DiscordInteractionHandler(
                    _configuration,
                    this,
                    _dispatcher,
                    _players,
                    _moderation,
                    _cases);

                _gateway.Start(_lifetime.Token);
            }
        }

        public bool TryQueueChatMessage(string content)
        {
            UDiscordConfiguration config = _configuration();
            if (!_started || config?.ChatRelay?.GameToDiscordEnabled != true || config.Discord.ChatChannelId == 0 || _outbound == null) return false;
            string safe = MessageSanitizer.FromGameToDiscord(content, config.ChatRelay.MaximumDiscordMessageLength);
            if (safe.Length == 0) return false;

            bool accepted = _outbound.TryEnqueue(new OutboundMessage { ChannelId = config.Discord.ChatChannelId, Content = safe });
            if (!accepted && DateTime.UtcNow - _lastQueueWarningUtc > TimeSpan.FromSeconds(10))
            {
                _lastQueueWarningUtc = DateTime.UtcNow;
                PluginLog.Warn("Discord outbound queue is full; chat relay messages are being dropped.");
            }
            return accepted;
        }

        public void RequestPresenceUpdate()
        {
            if (_gateway?.IsReady != true || _gameBridge == null) return;
            if (Interlocked.Exchange(ref _presenceUpdatePending, 1) != 0) return;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, _lifetime.Token).ConfigureAwait(false);
                    string activity = await _dispatcher.RunAsync(() => _gameBridge.BuildPresenceActivity(), _lifetime.Token).ConfigureAwait(false);
                    await _gateway.UpdatePresenceAsync(activity, _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    PluginLog.Debug("Presence update failed: " + exception.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _presenceUpdatePending, 0);
                }
            });
        }

        public Task EditInteractionResponseAsync(DiscordInteraction interaction, string content, CancellationToken cancellationToken)
        {
            ulong applicationId = ApplicationId;
            if (applicationId == 0) throw new InvalidOperationException("Discord application ID is not available yet.");
            return _rest.EditOriginalInteractionResponseAsync(applicationId, interaction.Token, content, cancellationToken);
        }

        public Task RespondInteractionAsync(DiscordInteraction interaction, string content, bool ephemeral, CancellationToken cancellationToken)
        {
            return _rest.RespondInteractionAsync(interaction, content, ephemeral, cancellationToken);
        }

        public Task DeferInteractionAsync(DiscordInteraction interaction, bool ephemeral, CancellationToken cancellationToken)
        {
            return _rest.DeferInteractionAsync(interaction, ephemeral, cancellationToken);
        }

        public Task RespondAutocompleteAsync(DiscordInteraction interaction, IEnumerable<DiscordAutocompleteChoice> choices, CancellationToken cancellationToken)
        {
            return _rest.RespondAutocompleteAsync(interaction, choices, cancellationToken);
        }

        public Task SendModerationLogAsync(ModerationCase item, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            if (config?.Discord?.ModerationLogChannelId == 0 || _rest == null || item == null) return Task.CompletedTask;
            int color = item.Succeeded ? 0x00D1FF : 0xB90E31;
            List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Action", item.Action.ToString()),
                new KeyValuePair<string, string>("Target", string.IsNullOrWhiteSpace(item.TargetSteamId) ? item.TargetDisplayName : item.TargetDisplayName + " (" + item.TargetSteamId + ")"),
                new KeyValuePair<string, string>("Moderator", item.ActorDisplayName + " (" + item.ActorDiscordId + ")"),
                new KeyValuePair<string, string>("Reason", item.Reason),
                new KeyValuePair<string, string>("Result", item.Result),
                new KeyValuePair<string, string>("Operation", item.OperationId)
            };
            if (item.ExpiresUtc.HasValue) fields.Add(new KeyValuePair<string, string>("Expires", item.ExpiresUtc.Value.ToString("u") + " UTC"));
            return _rest.SendModerationEmbedAsync(
                config.Discord.ModerationLogChannelId,
                "uDiscord case #" + item.CaseId,
                item.Succeeded ? "Moderation action completed." : "Moderation action failed or was denied.",
                color,
                fields,
                cancellationToken);
        }

        public void RequestReconnect()
        {
            _gateway?.RequestReconnect();
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            if (!_started) return;
            UDiscordConfiguration config = _configuration();
            if (!SuppressOfflineNotice && config?.ChatRelay?.RelayServerOfflineMessage == true && _rest != null && config.Discord.ChatChannelId != 0)
            {
                try
                {
                    using (CancellationTokenSource noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await _rest.SendChannelMessageAsync(config.Discord.ChatChannelId, (config.ServerDisplayName ?? "Unturned Server") + " is shutting down.", noticeTimeout.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    PluginLog.Debug("Unable to send shutdown notice: " + exception.Message);
                }
            }

            if (_gateway != null)
            {
                try { await _gateway.StopAsync(timeout).ConfigureAwait(false); }
                catch (Exception exception) { PluginLog.Exception(exception, "Discord gateway failed to stop cleanly."); }
            }
            if (_outbound != null)
            {
                try { await _outbound.StopAsync(timeout, true).ConfigureAwait(false); }
                catch (Exception exception) { PluginLog.Exception(exception, "Discord outbound worker failed to stop cleanly."); }
            }
            _lifetime.Cancel();
            _started = false;
        }

        private async Task OnDispatchAsync(string eventName, JObject data)
        {
            if (string.Equals(eventName, "READY", StringComparison.Ordinal))
            {
                await OnReadyAsync().ConfigureAwait(false);
                return;
            }

            if (string.Equals(eventName, "MESSAGE_CREATE", StringComparison.Ordinal))
            {
                await HandleMessageCreateAsync(data).ConfigureAwait(false);
                return;
            }

            if (string.Equals(eventName, "INTERACTION_CREATE", StringComparison.Ordinal))
            {
                DiscordInteraction interaction = DiscordInteractionParser.Parse(data);
                if (interaction != null && _interactions != null)
                {
                    _ = Task.Run(() => _interactions.HandleAsync(interaction, _lifetime.Token), _lifetime.Token);
                }
            }
        }

        private async Task OnReadyAsync()
        {
            UDiscordConfiguration config = _configuration();
            if (config.Discord.RegisterGuildCommandsOnStartup && ApplicationId != 0 && _registeredApplicationId != ApplicationId)
            {
                try
                {
                    await _rest.RegisterGuildCommandsAsync(ApplicationId, config.Discord.GuildId, DiscordCommandSchema.Build(config.Moderation.RequireReasons, config.Moderation.RequirePermanentBanConfirmation), _lifetime.Token).ConfigureAwait(false);
                    _registeredApplicationId = ApplicationId;
                    PluginLog.Info("Registered guild-scoped /udiscord commands.");
                }
                catch (Exception exception)
                {
                    PluginLog.Exception(exception, "Discord command registration failed. Chat relay can remain online, but slash commands are degraded.");
                }
            }

            if (!_serverOnlineNoticeSent && !SuppressOnlineNotice && config.ChatRelay.RelayServerOnlineMessage && config.Discord.ChatChannelId != 0)
            {
                try
                {
                    await _rest.SendChannelMessageAsync(config.Discord.ChatChannelId, (config.ServerDisplayName ?? "Unturned Server") + " is online.", _lifetime.Token).ConfigureAwait(false);
                    _serverOnlineNoticeSent = true;
                }
                catch (Exception exception)
                {
                    PluginLog.Debug("Unable to send server-online notice: " + exception.Message);
                }
            }
            RequestPresenceUpdate();
        }

        private async Task HandleMessageCreateAsync(JObject data)
        {
            UDiscordConfiguration config = _configuration();
            ulong guildId = ParseSnowflake(data["guild_id"]);
            ulong channelId = ParseSnowflake(data["channel_id"]);
            if (guildId != config.Discord.GuildId || channelId != config.Discord.ChatChannelId || !config.ChatRelay.DiscordToGameEnabled) return;

            JObject author = data["author"] as JObject;
            ulong authorId = ParseSnowflake(author?["id"]);
            bool bot = (bool?)author?["bot"] == true;
            bool webhook = data["webhook_id"] != null;
            if (config.Discord.IgnoreBotMessages && bot) return;
            if (config.Discord.IgnoreWebhookMessages && webhook) return;
            if (authorId == 0) return;

            TimeSpan retry;
            if (!_messageRateLimiter.TryAcquire(authorId.ToString(), DateTime.UtcNow, out retry)) return;

            JObject member = data["member"] as JObject;
            string authorName = (string)member?["nick"] ?? (string)author?["global_name"] ?? (string)author?["username"] ?? "Discord User";
            string content = (string)data["content"] ?? string.Empty;
            List<DiscordAttachment> attachments = new List<DiscordAttachment>();
            JArray array = data["attachments"] as JArray;
            if (array != null)
            {
                foreach (JObject item in array.OfType<JObject>())
                {
                    attachments.Add(new DiscordAttachment
                    {
                        FileName = (string)item["filename"] ?? "attachment",
                        Url = (string)item["url"] ?? string.Empty,
                        Size = (long?)item["size"] ?? 0
                    });
                }
            }

            if (string.IsNullOrWhiteSpace(content) && attachments.Count == 0) return;
            await _dispatcher.RunAsync(() => _gameBridge?.BroadcastDiscordMessage(authorName, content, attachments), _lifetime.Token).ConfigureAwait(false);
        }

        private Task SendOutboundAsync(OutboundMessage message, CancellationToken cancellationToken)
        {
            return _rest.SendChannelMessageAsync(message.ChannelId, message.Content, cancellationToken);
        }

        private void OnStateChanged(BotConnectionState state, string reason)
        {
            switch (state)
            {
                case BotConnectionState.Online:
                    PluginLog.Info(reason);
                    break;
                case BotConnectionState.Degraded:
                    PluginLog.Error(reason);
                    break;
                case BotConnectionState.Reconnecting:
                    PluginLog.Warn(reason);
                    break;
                default:
                    PluginLog.Debug(state + ": " + reason);
                    break;
            }
        }

        private static ulong ParseSnowflake(JToken token)
        {
            ulong value;
            return ulong.TryParse((string)token, out value) ? value : 0;
        }

        public void Dispose()
        {
            if (_gateway != null)
            {
                _gateway.DispatchReceived -= OnDispatchAsync;
                _gateway.StateChanged -= OnStateChanged;
                _gateway.Dispose();
            }
            _rest?.Dispose();
            _outbound?.Dispose();
            _lifetime.Dispose();
        }
    }
}

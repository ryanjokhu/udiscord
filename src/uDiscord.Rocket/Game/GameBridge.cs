using System;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Core;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using UDiscord.Core.Models;
using UDiscord.Core.Security;
using UDiscord.Core.Utility;
using UDiscord.Rocket.Configuration;
using UDiscord.Rocket.Discord;
using UDiscord.Rocket.Infrastructure;
using UDiscord.Rocket.Persistence;

namespace UDiscord.Rocket.Game
{
    public sealed class GameBridge
    {
        private sealed class PendingCommand
        {
            public string Text { get; set; }
            public DateTime CapturedUtc { get; set; }
        }

        private readonly Func<UDiscordConfiguration> _configuration;
        private readonly MuteStore _mutes;
        private readonly DiscordBotHost _discord;
        private readonly Dictionary<string, DateTime> _muteReminderTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingCommand> _pendingCommands = new Dictionary<string, PendingCommand>(StringComparer.Ordinal);
        private bool _subscribed;
        private bool _internalMuteHookSubscribed;
        private bool _rocketChatHookSubscribed;
        private bool _rocketCommandHookSubscribed;
        private bool _configurationFailureLogged;
        private bool _muteHookFailureLogged;
        private bool _chatHookFailureLogged;
        private bool _commandHookFailureLogged;
        private DateTime _lastRelayQueueWarningUtc = DateTime.MinValue;

        public GameBridge(Func<UDiscordConfiguration> configuration, MuteStore mutes, DiscordBotHost discord)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _mutes = mutes ?? throw new ArgumentNullException(nameof(mutes));
            _discord = discord ?? throw new ArgumentNullException(nameof(discord));
        }

        public void Subscribe()
        {
            if (_subscribed) return;

            UDiscordConfiguration config;
            if (TryGetConfiguration(out config))
            {
                ConfigurationValidator.Normalize(config);

                ModerationSettings moderation = config.Moderation;
                MuteBackendSettings muteBackend = moderation == null ? null : moderation.MuteBackend;
                if (moderation != null && moderation.Enabled && muteBackend != null && muteBackend.UsesInternalStore)
                {
                    ChatManager.onChatted += OnInternalMuteChatted;
                    _internalMuteHookSubscribed = true;
                }

                DiscordOutputRoutingSettings outputs = config.Outputs ?? DiscordOutputRoutingSettings.CreateDefault();
                bool needsRocketChat = outputs.HasAnyPlayerChatOutputEnabled() ||
                                       (outputs.CommandLogs != null && outputs.CommandLogs.Enabled);
                if (needsRocketChat)
                {
                    UnturnedPlayerEvents.OnPlayerChatted += OnRocketPlayerChatted;
                    _rocketChatHookSubscribed = true;
                }

                if (outputs.CommandLogs != null && outputs.CommandLogs.Enabled)
                {
                    if (R.Commands != null)
                    {
                        R.Commands.OnExecuteCommand += OnRocketCommandExecuting;
                        _rocketCommandHookSubscribed = true;
                    }
                    else
                    {
                        PluginLog.Warn("Command logging is enabled, but Rocket's command manager is unavailable. Player commands will not be logged.");
                    }
                }
            }

            U.Events.OnPlayerConnected += OnPlayerConnected;
            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;

            if (_internalMuteHookSubscribed)
            {
                ChatManager.onChatted -= OnInternalMuteChatted;
                _internalMuteHookSubscribed = false;
            }

            if (_rocketChatHookSubscribed)
            {
                UnturnedPlayerEvents.OnPlayerChatted -= OnRocketPlayerChatted;
                _rocketChatHookSubscribed = false;
            }

            if (_rocketCommandHookSubscribed && R.Commands != null)
            {
                R.Commands.OnExecuteCommand -= OnRocketCommandExecuting;
                _rocketCommandHookSubscribed = false;
            }

            U.Events.OnPlayerConnected -= OnPlayerConnected;
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            _subscribed = false;
            _muteReminderTimes.Clear();
            _pendingCommands.Clear();
        }

        public void BroadcastDiscordMessage(string author, string message, IReadOnlyList<DiscordAttachment> attachments)
        {
            UDiscordConfiguration config = _configuration();
            if (config == null || config.ChatRelay == null || !config.ChatRelay.DiscordToGameEnabled) return;

            string safeAuthor = MessageSanitizer.SafeDisplayName(author, 64);
            string safeMessage = MessageSanitizer.FromDiscordToGame(message, config.ChatRelay.MaximumGameMessageLength);
            if (config.ChatRelay.RelayAttachments && attachments != null)
            {
                foreach (DiscordAttachment attachment in attachments)
                {
                    if (attachment == null) continue;
                    string part = "[attachment: " + MessageSanitizer.SafeDisplayName(attachment.FileName, 80) + "]";
                    if (config.ChatRelay.RelayAttachmentUrls && !string.IsNullOrWhiteSpace(attachment.Url)) part += " " + attachment.Url;
                    safeMessage = string.IsNullOrWhiteSpace(safeMessage) ? part : safeMessage + " " + part;
                }
                safeMessage = MessageSanitizer.Truncate(safeMessage, config.ChatRelay.MaximumGameMessageLength);
            }

            if (string.IsNullOrWhiteSpace(safeMessage)) return;
            string rendered = (config.ChatRelay.DiscordToGameFormat ?? "[Discord] {author}: {message}")
                .Replace("{author}", safeAuthor)
                .Replace("{message}", safeMessage);
            Color color = ParseColor(config.ChatRelay.GameChatColorHex, Color.white);

            foreach (SteamPlayer client in Provider.clients)
            {
                if (client == null || client.player == null) continue;
                UnturnedPlayer target = UnturnedPlayer.FromSteamPlayer(client);
                UnturnedChat.Say(target, rendered, color, config.ChatRelay.UseRichTextInGame);
            }
        }

        public string BuildPresenceActivity()
        {
            UDiscordConfiguration config = _configuration();
            string template = config != null && config.Discord != null && !string.IsNullOrWhiteSpace(config.Discord.ActivityTemplate)
                ? config.Discord.ActivityTemplate
                : "{players}/{maxplayers} players";
            return template
                .Replace("{players}", Provider.clients.Count.ToString())
                .Replace("{maxplayers}", Provider.maxPlayers.ToString())
                .Replace("{server}", config == null || string.IsNullOrWhiteSpace(config.ServerDisplayName) ? "Unturned Server" : config.ServerDisplayName);
        }

        private void OnRocketPlayerChatted(UnturnedPlayer player, ref Color color, string message, EChatMode chatMode, ref bool cancel)
        {
            try
            {
                if (player == null || string.IsNullOrWhiteSpace(message)) return;

                UDiscordConfiguration config;
                if (!TryGetConfiguration(out config)) return;
                ConfigurationValidator.Normalize(config);

                bool isCommand = IsCommandText(message);
                DiscordFormattedOutputSettings commandOutput = config.Outputs == null ? null : config.Outputs.CommandLogs;
                if (isCommand)
                {
                    if (commandOutput != null && commandOutput.Enabled)
                    {
                        _pendingCommands[player.Id] = new PendingCommand
                        {
                            Text = message.Trim(),
                            CapturedUtc = DateTime.UtcNow
                        };
                    }
                    return;
                }

                if (cancel || config.Outputs == null) return;
                DiscordFormattedOutputSettings output = config.Outputs.GetChatOutput(chatMode);
                if (output == null || !output.Enabled || output.ChannelId == 0) return;

                ChatRelaySettings relay = config.ChatRelay;
                if (relay == null) return;
                string safeMessage = MessageSanitizer.FromGameToDiscord(message, relay.MaximumDiscordMessageLength);
                if (safeMessage.Length == 0) return;

                string steamId = player.CSteamID.m_SteamID.ToString();
                string playerName = MessageSanitizer.SafeDisplayName(
                    string.IsNullOrWhiteSpace(player.CharacterName) ? player.DisplayName : player.CharacterName,
                    64);
                string modeName = GetModeName(chatMode);
                string rendered = (output.Format ?? "[" + modeName + "] {player}: {message}")
                    .Replace("{player}", playerName)
                    .Replace("{steamid}", steamId)
                    .Replace("{message}", safeMessage)
                    .Replace("{mode}", modeName);

                if (!_discord.TryQueueOutput(output, rendered, relay.MaximumDiscordMessageLength, modeName.ToLowerInvariant() + " chat"))
                {
                    WarnRelayQueueFailure(config, output.ChannelId, modeName.ToLowerInvariant() + " chat");
                }
                else
                {
                    PluginLog.Debug("Relayed " + modeName.ToLowerInvariant() + " chat from " + playerName + " (" + steamId + ") to Discord channel " + output.ChannelId + ".");
                }
            }
            catch (Exception exception)
            {
                if (!_chatHookFailureLogged)
                {
                    _chatHookFailureLogged = true;
                    PluginLog.Exception(exception, "Rocket player-chat relay hook failed. In-game chat was not modified and this error will only be logged once for this runtime.");
                }
            }
        }

        private void OnRocketCommandExecuting(IRocketPlayer caller, IRocketCommand command, ref bool cancel)
        {
            try
            {
                if (cancel || caller == null || command == null) return;

                UDiscordConfiguration config;
                if (!TryGetConfiguration(out config)) return;
                ConfigurationValidator.Normalize(config);

                DiscordFormattedOutputSettings output = config.Outputs == null ? null : config.Outputs.CommandLogs;
                CommandLoggingSettings settings = config.CommandLogging ?? CommandLoggingSettings.CreateDefault();
                if (output == null || !output.Enabled || output.ChannelId == 0) return;

                UnturnedPlayer player = caller as UnturnedPlayer;
                if (player == null)
                {
                    if (!settings.LogConsoleCommands) return;
                    string consoleCommand = "/" + NormalizeCommandName(command.Name);
                    string consoleRendered = (output.Format ?? "[Command] {player} ({steamid}) dispatched: {command}")
                        .Replace("{player}", "Console")
                        .Replace("{steamid}", "console")
                        .Replace("{command}", consoleCommand);
                    _discord.TryQueueOutput(output, consoleRendered, 1800, "command log");
                    return;
                }

                string steamId = player.CSteamID.m_SteamID.ToString();
                PendingCommand pending;
                string raw = null;
                if (_pendingCommands.TryGetValue(player.Id, out pending))
                {
                    _pendingCommands.Remove(player.Id);
                    if (DateTime.UtcNow - pending.CapturedUtc <= TimeSpan.FromSeconds(3)) raw = pending.Text;
                }

                string commandName = NormalizeCommandName(command.Name);
                if (string.IsNullOrWhiteSpace(commandName)) commandName = ExtractCommandName(raw);
                if (IsListed(settings.IgnoredCommands, commandName)) return;

                string displayCommand;
                if (!settings.IncludeArguments)
                {
                    displayCommand = "/" + commandName;
                }
                else if (IsListed(settings.RedactedCommands, commandName))
                {
                    displayCommand = "/" + commandName + " [arguments redacted]";
                }
                else
                {
                    displayCommand = string.IsNullOrWhiteSpace(raw) ? "/" + commandName : NormalizeRawCommand(raw);
                }

                string playerName = MessageSanitizer.SafeDisplayName(
                    string.IsNullOrWhiteSpace(player.CharacterName) ? player.DisplayName : player.CharacterName,
                    64);
                string rendered = (output.Format ?? "[Command] {player} ({steamid}) dispatched: {command}")
                    .Replace("{player}", playerName)
                    .Replace("{steamid}", steamId)
                    .Replace("{command}", displayCommand);
                _discord.TryQueueOutput(output, rendered, 1800, "command log");
            }
            catch (Exception exception)
            {
                if (!_commandHookFailureLogged)
                {
                    _commandHookFailureLogged = true;
                    PluginLog.Exception(exception, "Rocket command logging hook failed. Command execution was not interrupted and this error will only be logged once for this runtime.");
                }
            }
        }

        private void OnInternalMuteChatted(SteamPlayer steamPlayer, EChatMode mode, ref Color color, ref bool isRich, string text, ref bool isVisible)
        {
            try
            {
                if (steamPlayer == null || steamPlayer.playerID == null || string.IsNullOrWhiteSpace(text)) return;

                UDiscordConfiguration config;
                if (!TryGetConfiguration(out config)) return;

                ModerationSettings moderation = config.Moderation;
                if (moderation == null || !moderation.Enabled) return;

                MuteBackendSettings backend = moderation.MuteBackend;
                if (backend == null || !backend.UsesInternalStore) return;

                string steamId = steamPlayer.playerID.steamID.m_SteamID.ToString();
                MuteRecord mute;
                if (_mutes.TryGetActive(steamId, DateTime.UtcNow, out mute))
                {
                    isVisible = false;
                    SendMuteReminder(steamPlayer, mute, config);
                }
            }
            catch (Exception exception)
            {
                if (!_muteHookFailureLogged)
                {
                    _muteHookFailureLogged = true;
                    PluginLog.Exception(exception, "Internal mute chat hook failed. The message was allowed and this error will only be logged once for this runtime.");
                }
            }
        }

        private void OnPlayerConnected(UnturnedPlayer player)
        {
            if (player == null) return;
            UDiscordConfiguration config = _configuration();
            ConfigurationValidator.Normalize(config);
            DiscordFormattedOutputSettings output = config == null || config.Outputs == null ? null : config.Outputs.PlayerJoin;
            if (output != null && output.Enabled && output.ChannelId != 0)
            {
                string message = (output.Format ?? "{player} joined the server.")
                    .Replace("{player}", MessageSanitizer.SafeDisplayName(player.CharacterName, 64))
                    .Replace("{steamid}", player.CSteamID.m_SteamID.ToString())
                    .Replace("{server}", config.ServerDisplayName ?? "Unturned Server");
                _discord.TryQueueOutput(output, message, config.ChatRelay.MaximumDiscordMessageLength, "player join");
            }
            _discord.RequestPresenceUpdate();
        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            if (player == null) return;
            UDiscordConfiguration config = _configuration();
            ConfigurationValidator.Normalize(config);
            DiscordFormattedOutputSettings output = config == null || config.Outputs == null ? null : config.Outputs.PlayerLeave;
            if (output != null && output.Enabled && output.ChannelId != 0)
            {
                string message = (output.Format ?? "{player} left the server.")
                    .Replace("{player}", MessageSanitizer.SafeDisplayName(player.CharacterName, 64))
                    .Replace("{steamid}", player.CSteamID.m_SteamID.ToString())
                    .Replace("{server}", config.ServerDisplayName ?? "Unturned Server");
                _discord.TryQueueOutput(output, message, config.ChatRelay.MaximumDiscordMessageLength, "player leave");
            }
            _discord.RequestPresenceUpdate();
        }

        private void WarnRelayQueueFailure(UDiscordConfiguration config, ulong channelId, string category)
        {
            if (DateTime.UtcNow - _lastRelayQueueWarningUtc <= TimeSpan.FromSeconds(10)) return;
            _lastRelayQueueWarningUtc = DateTime.UtcNow;
            PluginLog.Warn("Unable to queue an Unturned " + category + " message for Discord. Bot state=" + _discord.State + ", channel=" + channelId + ".");
        }

        private void SendMuteReminder(SteamPlayer player, MuteRecord mute, UDiscordConfiguration config)
        {
            string steamId = mute.SteamId;
            DateTime now = DateTime.UtcNow;
            DateTime last;
            int cooldown = Math.Max(1, config != null && config.Moderation != null ? config.Moderation.MuteReminderCooldownSeconds : 5);
            if (_muteReminderTimes.TryGetValue(steamId, out last) && now - last < TimeSpan.FromSeconds(cooldown)) return;
            _muteReminderTimes[steamId] = now;

            string message;
            if (mute.IsPermanent)
            {
                message = config != null && config.Messages != null ? config.Messages.MutedReminderPermanent : "You are permanently muted. Reason: {reason}";
            }
            else
            {
                TimeSpan remaining = mute.ExpiresUtc.Value - now;
                message = config != null && config.Messages != null ? config.Messages.MutedReminderTemporary : "You are muted for {remaining}. Reason: {reason}";
                message = message.Replace("{remaining}", DurationParser.Format(remaining));
            }
            message = message.Replace("{reason}", MessageSanitizer.FromGameToDiscord(mute.Reason, 200));
            UnturnedPlayer target = UnturnedPlayer.FromSteamPlayer(player);
            UnturnedChat.Say(target, message, Color.red);
        }

        private bool TryGetConfiguration(out UDiscordConfiguration config)
        {
            config = null;
            try
            {
                config = _configuration();
                return config != null;
            }
            catch (Exception exception)
            {
                if (!_configurationFailureLogged)
                {
                    _configurationFailureLogged = true;
                    PluginLog.Exception(exception, "Unable to read uDiscord configuration from a game hook. The hook action was skipped.");
                }
                return false;
            }
        }

        private static bool IsCommandText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.TrimStart();
            return value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("@", StringComparison.Ordinal);
        }

        private static string GetModeName(EChatMode mode)
        {
            switch (mode)
            {
                case EChatMode.GLOBAL:
                    return "Global";
                case EChatMode.LOCAL:
                    return "Local";
                case EChatMode.GROUP:
                    return "Group";
                default:
                    return mode.ToString();
            }
        }

        private static string NormalizeCommandName(string value)
        {
            return (value ?? string.Empty).Trim().TrimStart('/', '@').ToLowerInvariant();
        }

        private static string ExtractCommandName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string normalized = raw.Trim().TrimStart('/', '@');
            int separator = normalized.IndexOf(' ');
            if (separator >= 0) normalized = normalized.Substring(0, separator);
            return NormalizeCommandName(normalized);
        }

        private static string NormalizeRawCommand(string raw)
        {
            string value = (raw ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (!value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith("@", StringComparison.Ordinal)) value = "/" + value;
            return MessageSanitizer.FromGameToDiscord(value, 1800);
        }

        private static bool IsListed(IEnumerable<string> entries, string commandName)
        {
            if (entries == null || string.IsNullOrWhiteSpace(commandName)) return false;
            foreach (string entry in entries)
            {
                if (string.Equals(NormalizeCommandName(entry), commandName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static Color ParseColor(string value, Color fallback)
        {
            Color parsed;
            return !string.IsNullOrWhiteSpace(value) && ColorUtility.TryParseHtmlString(value, out parsed) ? parsed : fallback;
        }
    }
}

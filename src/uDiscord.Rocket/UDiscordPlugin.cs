using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Rocket.API.Collections;
using Rocket.Core.Plugins;
using Rocket.Core.Utils;
using UDiscord.Core.Models;
using UDiscord.Core.Utility;
using UDiscord.Rocket.Configuration;
using UDiscord.Rocket.Discord;
using UDiscord.Rocket.Game;
using UDiscord.Rocket.Infrastructure;
using UDiscord.Rocket.Persistence;

namespace UDiscord.Rocket
{
    public sealed class UDiscordPlugin : RocketPlugin<UDiscordConfiguration>
    {
        private readonly object _runtimeSync = new object();

        private CancellationTokenSource _runtimeCancellation;
        private MainThreadDispatcher _dispatcher;
        private PersistenceWorker _persistenceWorker;
        private MuteStore _mutes;
        private CaseStore _cases;
        private PlayerResolver _players;
        private ModerationService _moderation;
        private DiscordBotHost _discord;
        private GameBridge _gameBridge;
        private Timer _muteExpiryTimer;
        private ValidationResult _lastValidation = new ValidationResult();
        private bool _runtimeReady;
        private bool _unloading;
        private int _reloadInProgress;

        public static UDiscordPlugin Instance { get; private set; }
        public DateTime LoadedUtc { get; private set; }
        public bool RuntimeReady => _runtimeReady;
        public BotConnectionState DiscordState => _discord == null ? BotConnectionState.Disabled : _discord.State;
        public string Version => Assembly.GetExecutingAssembly().GetName().Version == null
            ? "1.0.0"
            : Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public override TranslationList DefaultTranslations => new TranslationList
        {
            { "Status", "uDiscord {0}: runtime={1}, discord={2}, players={3}/{4}, active_mutes={5}, next_case={6}" },
            { "ReloadStarted", "uDiscord configuration is valid. Reconnecting the embedded Discord bot now." },
            { "ReloadBusy", "uDiscord is already reloading." },
            { "ReloadInvalid", "uDiscord configuration was not applied: {0}" },
            { "ReconnectRequested", "uDiscord reconnect requested." },
            { "TestQueued", "uDiscord test message queued for Discord." },
            { "TestFailed", "uDiscord could not queue the test message. Check that chat relay is configured and the bot is running." },
            { "NotReady", "uDiscord runtime is not ready. Check the server console for configuration errors." },
            { "Usage", "Usage: /udiscord <status|reload|reconnect|test>" }
        };

        protected override void Load()
        {
            Instance = this;
            LoadedUtc = DateTime.UtcNow;
            _unloading = false;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception exception)
            {
                PluginLog.Debug("Unable to explicitly enable TLS 1.2: " + exception.Message);
            }

            StartRuntime();
        }

        protected override void Unload()
        {
            _unloading = true;
            Interlocked.Exchange(ref _reloadInProgress, 1);

            RuntimeSnapshot snapshot = DetachRuntimeOnMainThread();
            StopRuntimeBlocking(snapshot, true);

            PluginLog.SensitiveToken = string.Empty;
            PluginLog.DebugEnabled = false;
            Instance = null;
        }

        public string Text(string key, params object[] arguments)
        {
            return Translate(key, arguments);
        }

        public string BuildStatusText()
        {
            int players = 0;
            int maxPlayers = 0;
            try
            {
                players = SDG.Unturned.Provider.clients.Count;
                maxPlayers = SDG.Unturned.Provider.maxPlayers;
            }
            catch
            {
            }

            int activeMutes = _moderation == null ? 0 : _moderation.ActiveMuteCount;
            long nextCase = _cases == null ? 0 : _cases.PeekNextCaseId();
            return Translate(
                "Status",
                Version,
                _runtimeReady ? "ready" : "degraded",
                DiscordState,
                players,
                maxPlayers,
                activeMutes,
                nextCase);
        }

        public bool RequestReconnect()
        {
            DiscordBotHost host = _discord;
            if (!_runtimeReady || host == null) return false;
            host.RequestReconnect();
            return true;
        }

        public bool QueueTestMessage()
        {
            DiscordBotHost host = _discord;
            if (!_runtimeReady || host == null) return false;
            return host.TryQueueChatMessage("uDiscord test: the embedded bot can send messages from the Unturned server.");
        }

        public bool BeginReload(out string response)
        {
            if (_unloading)
            {
                response = Translate("NotReady");
                return false;
            }

            if (Interlocked.CompareExchange(ref _reloadInProgress, 1, 0) != 0)
            {
                response = Translate("ReloadBusy");
                return false;
            }

            try
            {
                Configuration.Load();
                ValidationResult validation = ConfigurationValidator.Validate(Configuration.Instance);
                _lastValidation = validation;
                if (!validation.IsValid)
                {
                    Interlocked.Exchange(ref _reloadInProgress, 0);
                    response = Translate("ReloadInvalid", string.Join("; ", validation.Errors));
                    LogValidation(validation);
                    return false;
                }

                PluginLog.DebugEnabled = Configuration.Instance.Debug;
                PluginLog.SensitiveToken = Configuration.Instance.ResolveBotToken();

                GameBridge oldBridge;
                DiscordBotHost oldHost;
                lock (_runtimeSync)
                {
                    oldBridge = _gameBridge;
                    oldHost = _discord;
                    _gameBridge = null;
                    _discord = null;
                    _runtimeReady = false;
                }
                oldBridge?.Unsubscribe();

                response = Translate("ReloadStarted");
                Task.Run(async () =>
                {
                    try
                    {
                        if (oldHost != null)
                        {
                            oldHost.SuppressOfflineNotice = true;
                            await oldHost.StopAsync(GetShutdownTimeout()).ConfigureAwait(false);
                            oldHost.Dispose();
                        }
                    }
                    catch (Exception exception)
                    {
                        PluginLog.Exception(exception, "Unable to stop the previous Discord bot during reload.");
                    }

                    TaskDispatcher.QueueOnMainThread(() => CompleteReloadOnMainThread());
                });
                return true;
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _reloadInProgress, 0);
                PluginLog.Exception(exception, "uDiscord configuration reload failed.");
                response = Translate("ReloadInvalid", exception.Message);
                return false;
            }
        }

        private void CompleteReloadOnMainThread()
        {
            try
            {
                if (_unloading) return;
                if (_dispatcher == null || _players == null || _moderation == null || _cases == null)
                {
                    PluginLog.Error("Cannot complete Discord reload because the core runtime is unavailable. Reload the plugin through Rocket.");
                    return;
                }

                DiscordBotHost host = CreateDiscordHost();
                host.SuppressOnlineNotice = true;
                GameBridge bridge = new GameBridge(() => Configuration.Instance, _mutes, host);
                host.AttachGameBridge(bridge);
                bridge.Subscribe();
                host.Start();

                lock (_runtimeSync)
                {
                    _discord = host;
                    _gameBridge = bridge;
                    _runtimeReady = true;
                }
                PluginLog.Info("Configuration reloaded and embedded Discord bot restarted.");
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Unable to restart the embedded Discord bot after reload.");
            }
            finally
            {
                Interlocked.Exchange(ref _reloadInProgress, 0);
            }
        }

        private void StartRuntime()
        {
            UDiscordConfiguration config = Configuration.Instance;
            PluginLog.DebugEnabled = config != null && config.Debug;
            PluginLog.SensitiveToken = config == null ? string.Empty : config.ResolveBotToken();

            ValidationResult validation = ConfigurationValidator.Validate(config);
            _lastValidation = validation;
            LogValidation(validation);
            if (!validation.IsValid)
            {
                _runtimeReady = false;
                PluginLog.Error("uDiscord loaded in degraded mode because configuration validation failed. The Unturned server will continue running.");
                return;
            }

            if (!config.Enabled)
            {
                _runtimeReady = false;
                PluginLog.Info("uDiscord is disabled by configuration.");
                return;
            }

            try
            {
                _runtimeCancellation = new CancellationTokenSource();
                _dispatcher = new MainThreadDispatcher(TimeSpan.FromSeconds(config.RateLimits.MainThreadActionTimeoutSeconds));
                _persistenceWorker = new PersistenceWorker(config.RateLimits.PersistenceQueueCapacity, _runtimeCancellation.Token);
                _persistenceWorker.Start();

                string dataDirectory = ResolveDataDirectory(config.Persistence.DataDirectoryName);
                DirectoryInfo directory = System.IO.Directory.CreateDirectory(dataDirectory);
                string mutesPath = Path.Combine(directory.FullName, config.Persistence.MutesFileName);
                string casesPath = Path.Combine(directory.FullName, config.Persistence.CasesFileName);
                string statePath = Path.Combine(directory.FullName, config.Persistence.StateFileName);

                _mutes = new MuteStore(mutesPath, _persistenceWorker, config.Persistence.WriteIndentedJson);
                _mutes.Load();
                _cases = new CaseStore(casesPath, statePath, config.Persistence.MaximumCasesLoadedIntoMemory, _persistenceWorker);
                _cases.Load();
                _players = new PlayerResolver();
                _moderation = new ModerationService(_players, _mutes);

                _discord = CreateDiscordHost();
                _gameBridge = new GameBridge(() => Configuration.Instance, _mutes, _discord);
                _discord.AttachGameBridge(_gameBridge);
                _gameBridge.Subscribe();
                _discord.Start();

                _muteExpiryTimer = new Timer(PurgeExpiredMutes, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                _runtimeReady = true;
                PluginLog.Info(
                    "Loaded v" + Version + ". Chat relay=" + (config.ChatRelay.GameToDiscordEnabled || config.ChatRelay.DiscordToGameEnabled) +
                    ", moderation=" + config.Moderation.Enabled +
                    ", active mutes=" + _mutes.Count +
                    ", next case=#" + _cases.PeekNextCaseId() + ".");
            }
            catch (Exception exception)
            {
                _runtimeReady = false;
                PluginLog.Exception(exception, "uDiscord runtime startup failed. Cleaning up partial initialization.");
                RuntimeSnapshot snapshot = DetachRuntimeOnMainThread();
                StopRuntimeBlocking(snapshot, false);
            }
        }

        private DiscordBotHost CreateDiscordHost()
        {
            return new DiscordBotHost(
                () => Configuration.Instance,
                _dispatcher,
                _players,
                _moderation,
                _cases);
        }

        private RuntimeSnapshot DetachRuntimeOnMainThread()
        {
            RuntimeSnapshot snapshot;
            lock (_runtimeSync)
            {
                _runtimeReady = false;
                _muteExpiryTimer?.Dispose();
                _muteExpiryTimer = null;
                _gameBridge?.Unsubscribe();
                _dispatcher?.StopAccepting();

                snapshot = new RuntimeSnapshot
                {
                    Cancellation = _runtimeCancellation,
                    Dispatcher = _dispatcher,
                    PersistenceWorker = _persistenceWorker,
                    Mutes = _mutes,
                    Cases = _cases,
                    Discord = _discord,
                    GameBridge = _gameBridge
                };

                _runtimeCancellation = null;
                _dispatcher = null;
                _persistenceWorker = null;
                _mutes = null;
                _cases = null;
                _players = null;
                _moderation = null;
                _discord = null;
                _gameBridge = null;
            }
            return snapshot;
        }

        private void StopRuntimeBlocking(RuntimeSnapshot snapshot, bool sendOfflineNotice)
        {
            if (snapshot == null) return;
            TimeSpan timeout = GetShutdownTimeout();
            try
            {
                if (snapshot.Discord != null)
                {
                    if (!sendOfflineNotice) snapshot.Discord.SuppressOfflineNotice = true;
                    snapshot.Discord.StopAsync(timeout).GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Embedded Discord bot did not stop cleanly.");
            }

            try
            {
                snapshot.Mutes?.SaveNow();
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Final mute persistence write failed during shutdown.");
            }

            try
            {
                snapshot.PersistenceWorker?.StopAsync(timeout).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Persistence worker did not stop cleanly.");
            }

            snapshot.Cancellation?.Cancel();
            snapshot.Discord?.Dispose();
            snapshot.PersistenceWorker?.Dispose();
            snapshot.Dispatcher?.Dispose();
            snapshot.Cancellation?.Dispose();
            PluginLog.Info("Unloaded cleanly: hooks removed, workers stopped, and critical mute state flushed.");
        }

        private void PurgeExpiredMutes(object state)
        {
            try
            {
                int removed = _mutes == null ? 0 : _mutes.PurgeExpired(DateTime.UtcNow);
                if (removed > 0) PluginLog.Debug("Purged " + removed + " expired mute record(s).");
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Expired mute cleanup failed.");
            }
        }

        private string ResolveDataDirectory(string configuredName)
        {
            string root = Path.GetFullPath(Directory);
            string combined = Path.GetFullPath(Path.Combine(root, configuredName));
            string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Persistence.DataDirectoryName must stay inside the uDiscord plugin directory.");
            return combined;
        }

        private TimeSpan GetShutdownTimeout()
        {
            int seconds = Configuration.Instance?.Persistence?.ShutdownFlushTimeoutSeconds ?? 5;
            return TimeSpan.FromSeconds(Math.Max(1, Math.Min(30, seconds)));
        }

        private static void LogValidation(ValidationResult validation)
        {
            if (validation == null) return;
            foreach (string warning in validation.Warnings) PluginLog.Warn("Configuration: " + warning);
            foreach (string error in validation.Errors) PluginLog.Error("Configuration: " + error);
        }

        private sealed class RuntimeSnapshot
        {
            public CancellationTokenSource Cancellation { get; set; }
            public MainThreadDispatcher Dispatcher { get; set; }
            public PersistenceWorker PersistenceWorker { get; set; }
            public MuteStore Mutes { get; set; }
            public CaseStore Cases { get; set; }
            public DiscordBotHost Discord { get; set; }
            public GameBridge GameBridge { get; set; }
        }
    }
}

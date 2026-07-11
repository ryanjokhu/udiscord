using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Rocket.API;

namespace UDiscord.Rocket.Configuration
{
    public sealed class UDiscordConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled { get; set; }
        public string ServerDisplayName { get; set; }
        public bool Debug { get; set; }
        public DiscordSettings Discord { get; set; }
        public ChatRelaySettings ChatRelay { get; set; }
        public ModerationSettings Moderation { get; set; }
        public PermissionSettings Permissions { get; set; }
        public RateLimitSettings RateLimits { get; set; }
        public PersistenceSettings Persistence { get; set; }
        public MessageSettings Messages { get; set; }

        public void LoadDefaults()
        {
            Enabled = true;
            ServerDisplayName = "Unturned Server";
            Debug = false;
            Discord = DiscordSettings.CreateDefault();
            ChatRelay = ChatRelaySettings.CreateDefault();
            Moderation = ModerationSettings.CreateDefault();
            Permissions = PermissionSettings.CreateDefault();
            RateLimits = RateLimitSettings.CreateDefault();
            Persistence = PersistenceSettings.CreateDefault();
            Messages = MessageSettings.CreateDefault();
        }

        public string ResolveBotToken()
        {
            if (Discord == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(Discord.BotTokenEnvironmentVariable))
            {
                string value = Environment.GetEnvironmentVariable(Discord.BotTokenEnvironmentVariable.Trim());
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return (Discord.BotToken ?? string.Empty).Trim();
        }
    }

    public sealed class DiscordSettings
    {
        public string BotToken { get; set; }
        public string BotTokenEnvironmentVariable { get; set; }
        public ulong GuildId { get; set; }
        public ulong ChatChannelId { get; set; }
        public ulong ModerationLogChannelId { get; set; }
        public bool RegisterGuildCommandsOnStartup { get; set; }
        public bool IgnoreBotMessages { get; set; }
        public bool IgnoreWebhookMessages { get; set; }
        public int GatewayIntents { get; set; }
        public int MaximumGatewayPayloadBytes { get; set; }
        public int RestRequestTimeoutSeconds { get; set; }
        public int ReconnectMinimumSeconds { get; set; }
        public int ReconnectMaximumSeconds { get; set; }
        public string ActivityTemplate { get; set; }

        [XmlArrayItem("ChannelId")]
        public List<ulong> CommandChannelIds { get; set; }

        public static DiscordSettings CreateDefault()
        {
            return new DiscordSettings
            {
                BotToken = string.Empty,
                BotTokenEnvironmentVariable = "UDISCORD_BOT_TOKEN",
                GuildId = 0,
                ChatChannelId = 0,
                ModerationLogChannelId = 0,
                RegisterGuildCommandsOnStartup = true,
                IgnoreBotMessages = true,
                IgnoreWebhookMessages = true,
                GatewayIntents = 33281,
                MaximumGatewayPayloadBytes = 1048576,
                RestRequestTimeoutSeconds = 15,
                ReconnectMinimumSeconds = 2,
                ReconnectMaximumSeconds = 60,
                ActivityTemplate = "{players}/{maxplayers} players",
                CommandChannelIds = new List<ulong>()
            };
        }
    }

    public sealed class ChatRelaySettings
    {
        public bool GameToDiscordEnabled { get; set; }
        public bool DiscordToGameEnabled { get; set; }
        public bool RelayGlobalChat { get; set; }
        public bool RelayJoinMessages { get; set; }
        public bool RelayLeaveMessages { get; set; }
        public bool RelayServerOnlineMessage { get; set; }
        public bool RelayServerOfflineMessage { get; set; }
        public bool RelayAttachments { get; set; }
        public bool RelayAttachmentUrls { get; set; }
        public int MaximumGameMessageLength { get; set; }
        public int MaximumDiscordMessageLength { get; set; }
        public string DiscordToGameFormat { get; set; }
        public string GameToDiscordFormat { get; set; }
        public string JoinFormat { get; set; }
        public string LeaveFormat { get; set; }
        public string GameChatColorHex { get; set; }
        public bool UseRichTextInGame { get; set; }

        public static ChatRelaySettings CreateDefault()
        {
            return new ChatRelaySettings
            {
                GameToDiscordEnabled = true,
                DiscordToGameEnabled = true,
                RelayGlobalChat = true,
                RelayJoinMessages = true,
                RelayLeaveMessages = true,
                RelayServerOnlineMessage = true,
                RelayServerOfflineMessage = true,
                RelayAttachments = true,
                RelayAttachmentUrls = false,
                MaximumGameMessageLength = 240,
                MaximumDiscordMessageLength = 1800,
                DiscordToGameFormat = "<color=#5865F2>[Discord]</color> <color=#D3D3D3>{author}: {message}</color>",
                GameToDiscordFormat = "[Global] {player}: {message}",
                JoinFormat = "{player} joined the server.",
                LeaveFormat = "{player} left the server.",
                GameChatColorHex = "#D3D3D3",
                UseRichTextInGame = true
            };
        }
    }

    public sealed class ModerationSettings
    {
        public bool Enabled { get; set; }
        public bool RequireReasons { get; set; }
        public int MinimumReasonLength { get; set; }
        public int MaximumReasonLength { get; set; }
        public int MaximumTemporaryBanDays { get; set; }
        public int MaximumTemporaryMuteDays { get; set; }
        public bool RequirePermanentBanConfirmation { get; set; }
        public bool AllowOfflineBansBySteamId { get; set; }
        public bool AllowDiscordAdministratorBypass { get; set; }
        public bool LogDeniedActions { get; set; }
        public int MuteReminderCooldownSeconds { get; set; }
        public MuteBackendSettings MuteBackend { get; set; }

        public static ModerationSettings CreateDefault()
        {
            return new ModerationSettings
            {
                Enabled = true,
                RequireReasons = true,
                MinimumReasonLength = 3,
                MaximumReasonLength = 300,
                MaximumTemporaryBanDays = 365,
                MaximumTemporaryMuteDays = 365,
                RequirePermanentBanConfirmation = true,
                AllowOfflineBansBySteamId = true,
                AllowDiscordAdministratorBypass = false,
                LogDeniedActions = true,
                MuteReminderCooldownSeconds = 5,
                MuteBackend = MuteBackendSettings.CreateDefault()
            };
        }
    }

    public sealed class MuteBackendSettings
    {
        public string Mode { get; set; }
        public string MuteCommand { get; set; }
        public string TemporaryMuteCommand { get; set; }
        public string UnmuteCommand { get; set; }
        public bool AllowOfflineTargets { get; set; }

        public bool UsesInternalStore => string.Equals(Mode, "Internal", StringComparison.OrdinalIgnoreCase);
        public bool UsesCommands => string.Equals(Mode, "Command", StringComparison.OrdinalIgnoreCase);

        public static MuteBackendSettings CreateDefault()
        {
            return new MuteBackendSettings
            {
                Mode = "Command",
                MuteCommand = "mute {steamid} {reason}",
                TemporaryMuteCommand = "tempmute {steamid} {duration} {reason}",
                UnmuteCommand = "unmute {steamid}",
                AllowOfflineTargets = true
            };
        }
    }

    public sealed class PermissionSettings
    {
        [XmlArrayItem("RoleId")]
        public List<ulong> ViewerRoleIds { get; set; }

        [XmlArrayItem("RoleId")]
        public List<ulong> ModeratorRoleIds { get; set; }

        [XmlArrayItem("RoleId")]
        public List<ulong> AdministratorRoleIds { get; set; }

        public static PermissionSettings CreateDefault()
        {
            return new PermissionSettings
            {
                ViewerRoleIds = new List<ulong>(),
                ModeratorRoleIds = new List<ulong>(),
                AdministratorRoleIds = new List<ulong>()
            };
        }
    }

    public sealed class RateLimitSettings
    {
        public int DiscordMessagesPerWindow { get; set; }
        public int DiscordMessageWindowSeconds { get; set; }
        public int ModerationActionsPerWindow { get; set; }
        public int ModerationActionWindowSeconds { get; set; }
        public int OutboundQueueCapacity { get; set; }
        public int PersistenceQueueCapacity { get; set; }
        public int MainThreadActionTimeoutSeconds { get; set; }

        public static RateLimitSettings CreateDefault()
        {
            return new RateLimitSettings
            {
                DiscordMessagesPerWindow = 5,
                DiscordMessageWindowSeconds = 10,
                ModerationActionsPerWindow = 10,
                ModerationActionWindowSeconds = 60,
                OutboundQueueCapacity = 500,
                PersistenceQueueCapacity = 256,
                MainThreadActionTimeoutSeconds = 5
            };
        }
    }

    public sealed class PersistenceSettings
    {
        public string DataDirectoryName { get; set; }
        public string MutesFileName { get; set; }
        public string CasesFileName { get; set; }
        public string StateFileName { get; set; }
        public int MaximumCasesLoadedIntoMemory { get; set; }
        public int ShutdownFlushTimeoutSeconds { get; set; }
        public bool WriteIndentedJson { get; set; }

        public static PersistenceSettings CreateDefault()
        {
            return new PersistenceSettings
            {
                DataDirectoryName = "Data",
                MutesFileName = "mutes.json",
                CasesFileName = "cases.ndjson",
                StateFileName = "state.json",
                MaximumCasesLoadedIntoMemory = 10000,
                ShutdownFlushTimeoutSeconds = 5,
                WriteIndentedJson = true
            };
        }
    }

    public sealed class MessageSettings
    {
        public string NoPermission { get; set; }
        public string RateLimited { get; set; }
        public string PlayerNotFound { get; set; }
        public string PlayerAmbiguous { get; set; }
        public string MutedReminderPermanent { get; set; }
        public string MutedReminderTemporary { get; set; }
        public string BotOffline { get; set; }

        public static MessageSettings CreateDefault()
        {
            return new MessageSettings
            {
                NoPermission = "You do not have permission to use this command.",
                RateLimited = "You are doing that too quickly. Try again in {seconds}s.",
                PlayerNotFound = "No player matched that target. Use a Steam64 ID for offline players.",
                PlayerAmbiguous = "That name matched multiple players. Select a Steam64 ID from autocomplete.",
                MutedReminderPermanent = "You are permanently muted. Reason: {reason}",
                MutedReminderTemporary = "You are muted for {remaining}. Reason: {reason}",
                BotOffline = "Discord is not currently connected."
            };
        }
    }
}

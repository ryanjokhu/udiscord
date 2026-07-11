using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UDiscord.Core.Utility;

namespace UDiscord.Rocket.Configuration
{
    public static class ConfigurationValidator
    {
        public static ValidationResult Validate(UDiscordConfiguration configuration)
        {
            ValidationResult result = new ValidationResult();
            if (configuration == null)
            {
                result.AddError("Configuration is missing.");
                return result;
            }

            Normalize(configuration);

            if (configuration.Discord == null) result.AddError("Discord section is missing.");
            if (configuration.ChatRelay == null) result.AddError("ChatRelay section is missing.");
            if (configuration.Outputs == null) result.AddError("Outputs section is missing.");
            if (configuration.CommandLogging == null) configuration.CommandLogging = CommandLoggingSettings.CreateDefault();
            if (configuration.Moderation == null) result.AddError("Moderation section is missing.");
            if (configuration.Permissions == null) result.AddError("Permissions section is missing.");
            if (configuration.RateLimits == null) result.AddError("RateLimits section is missing.");
            if (configuration.Persistence == null) result.AddError("Persistence section is missing.");
            if (configuration.Messages == null) result.AddError("Messages section is missing.");
            if (!result.IsValid) return result;

            string token = configuration.ResolveBotToken();
            if (configuration.Enabled && string.IsNullOrWhiteSpace(token))
                result.AddError("Bot token is missing. Set Discord.BotToken or the configured environment variable.");
            if (configuration.Discord.GuildId == 0) result.AddError("Discord.GuildId must be configured.");
            if (configuration.ChatRelay.DiscordToGameEnabled && configuration.Discord.DiscordToGameChannelId == 0)
                result.AddError("Discord.DiscordToGameChannelId must be configured when Discord-to-game chat is enabled.");

            ValidateOutputs(configuration.Outputs, result);
            ValidateCommandLogging(configuration.CommandLogging, result);

            if (configuration.Moderation.Enabled && !HasAnyRole(configuration.Permissions))
                result.AddError("Moderation is enabled but no viewer, moderator, or administrator Discord role IDs are configured.");
            if (configuration.Moderation.Enabled && (configuration.Outputs.ModerationLogs == null || !configuration.Outputs.ModerationLogs.Enabled))
                result.AddWarning("Moderation is enabled while Outputs.ModerationLogs is disabled; cases will be persisted locally but not posted to Discord.");

            ValidateRoleLists(configuration.Permissions, result);

            if (configuration.Discord.MaximumGatewayPayloadBytes < 65536 || configuration.Discord.MaximumGatewayPayloadBytes > 8388608)
                result.AddError("Discord.MaximumGatewayPayloadBytes must be between 65536 and 8388608.");
            if (configuration.Discord.RestRequestTimeoutSeconds < 3 || configuration.Discord.RestRequestTimeoutSeconds > 120)
                result.AddError("Discord.RestRequestTimeoutSeconds must be between 3 and 120.");
            if (configuration.Discord.ReconnectMinimumSeconds < 1 || configuration.Discord.ReconnectMaximumSeconds < configuration.Discord.ReconnectMinimumSeconds)
                result.AddError("Discord reconnect bounds are invalid.");

            const int GuildsIntent = 1;
            const int GuildMessagesIntent = 512;
            const int MessageContentIntent = 32768;
            if ((configuration.Discord.GatewayIntents & GuildsIntent) == 0)
                result.AddError("Discord.GatewayIntents must include the Guilds intent (1).");
            if (configuration.ChatRelay.DiscordToGameEnabled && (configuration.Discord.GatewayIntents & GuildMessagesIntent) == 0)
                result.AddError("Discord-to-game chat requires the Guild Messages intent (512).");
            if (configuration.ChatRelay.DiscordToGameEnabled && (configuration.Discord.GatewayIntents & MessageContentIntent) == 0)
                result.AddError("Discord-to-game chat requires the Message Content intent (32768).");

            if (configuration.Discord.CommandChannelIds != null)
            {
                if (configuration.Discord.CommandChannelIds.Any(id => id == 0))
                    result.AddError("Discord.CommandChannelIds cannot contain zero.");
                foreach (ulong duplicate in configuration.Discord.CommandChannelIds.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
                    result.AddWarning("Discord command channel ID " + duplicate + " appears more than once.");
            }

            if (configuration.ChatRelay.MaximumGameMessageLength < 32 || configuration.ChatRelay.MaximumGameMessageLength > 512)
                result.AddError("ChatRelay.MaximumGameMessageLength must be between 32 and 512.");
            if (configuration.ChatRelay.MaximumDiscordMessageLength < 64 || configuration.ChatRelay.MaximumDiscordMessageLength > 2000)
                result.AddError("ChatRelay.MaximumDiscordMessageLength must be between 64 and 2000.");

            if (configuration.Moderation.MinimumReasonLength < 0 || configuration.Moderation.MaximumReasonLength < configuration.Moderation.MinimumReasonLength)
                result.AddError("Moderation reason length bounds are invalid.");
            if (configuration.Moderation.MaximumTemporaryBanDays < 1 || configuration.Moderation.MaximumTemporaryBanDays > 3650)
                result.AddError("Moderation.MaximumTemporaryBanDays must be between 1 and 3650.");
            if (configuration.Moderation.MaximumTemporaryMuteDays < 1 || configuration.Moderation.MaximumTemporaryMuteDays > 3650)
                result.AddError("Moderation.MaximumTemporaryMuteDays must be between 1 and 3650.");
            ValidateMuteBackend(configuration.Moderation.MuteBackend, result);

            if (configuration.RateLimits.DiscordMessagesPerWindow < 1 || configuration.RateLimits.DiscordMessageWindowSeconds < 1)
                result.AddError("Discord message rate limit must be positive.");
            if (configuration.RateLimits.ModerationActionsPerWindow < 1 || configuration.RateLimits.ModerationActionWindowSeconds < 1)
                result.AddError("Moderation action rate limit must be positive.");
            if (configuration.RateLimits.OutboundQueueCapacity < 10 || configuration.RateLimits.OutboundQueueCapacity > 10000)
                result.AddError("RateLimits.OutboundQueueCapacity must be between 10 and 10000.");
            if (configuration.RateLimits.PersistenceQueueCapacity < 10 || configuration.RateLimits.PersistenceQueueCapacity > 10000)
                result.AddError("RateLimits.PersistenceQueueCapacity must be between 10 and 10000.");
            if (configuration.RateLimits.MainThreadActionTimeoutSeconds < 1 || configuration.RateLimits.MainThreadActionTimeoutSeconds > 30)
                result.AddError("RateLimits.MainThreadActionTimeoutSeconds must be between 1 and 30.");

            if (string.IsNullOrWhiteSpace(configuration.Persistence.DataDirectoryName)) result.AddError("Persistence.DataDirectoryName is required.");
            else if (Path.IsPathRooted(configuration.Persistence.DataDirectoryName) || configuration.Persistence.DataDirectoryName.IndexOf("..", StringComparison.Ordinal) >= 0)
                result.AddError("Persistence.DataDirectoryName must be a relative directory inside the plugin folder.");
            ValidateFileName(configuration.Persistence.MutesFileName, "Persistence.MutesFileName", result);
            ValidateFileName(configuration.Persistence.CasesFileName, "Persistence.CasesFileName", result);
            ValidateFileName(configuration.Persistence.StateFileName, "Persistence.StateFileName", result);
            if (configuration.Persistence.MaximumCasesLoadedIntoMemory < 100 || configuration.Persistence.MaximumCasesLoadedIntoMemory > 100000)
                result.AddError("Persistence.MaximumCasesLoadedIntoMemory must be between 100 and 100000.");
            if (configuration.Persistence.ShutdownFlushTimeoutSeconds < 1 || configuration.Persistence.ShutdownFlushTimeoutSeconds > 30)
                result.AddError("Persistence.ShutdownFlushTimeoutSeconds must be between 1 and 30.");

            return result;
        }

        public static void Normalize(UDiscordConfiguration configuration)
        {
            if (configuration == null) return;
            if (configuration.Discord == null) configuration.Discord = DiscordSettings.CreateDefault();
            if (configuration.ChatRelay == null) configuration.ChatRelay = ChatRelaySettings.CreateDefault();
            if (configuration.CommandLogging == null) configuration.CommandLogging = CommandLoggingSettings.CreateDefault();

            if (configuration.Discord.DiscordToGameChannelId == 0 && configuration.Discord.ChatChannelId != 0)
                configuration.Discord.DiscordToGameChannelId = configuration.Discord.ChatChannelId;

            if (configuration.Outputs == null)
                configuration.Outputs = CreateOutputsFromLegacy(configuration);
            else
                FillMissingOutputs(configuration.Outputs);
        }

        private static DiscordOutputRoutingSettings CreateOutputsFromLegacy(UDiscordConfiguration configuration)
        {
            DiscordOutputRoutingSettings outputs = DiscordOutputRoutingSettings.CreateDefault();
            ulong legacyChatChannel = configuration.Discord == null ? 0UL : configuration.Discord.ChatChannelId;
            ChatRelaySettings relay = configuration.ChatRelay ?? ChatRelaySettings.CreateDefault();
            CommandLoggingSettings commandLogging = configuration.CommandLogging ?? CommandLoggingSettings.CreateDefault();

            outputs.GlobalChat.Enabled = relay.GameToDiscordEnabled && relay.RelayGlobalChat;
            outputs.GlobalChat.ChannelId = legacyChatChannel;
            outputs.GlobalChat.Format = string.IsNullOrWhiteSpace(relay.GameToDiscordFormat) ? outputs.GlobalChat.Format : relay.GameToDiscordFormat;

            // Local and group chat were not relayed by v1.x. Keep them off during migration to avoid leaking private chat.
            outputs.LocalChat.Enabled = false;
            outputs.LocalChat.ChannelId = 0;
            outputs.GroupChat.Enabled = false;
            outputs.GroupChat.ChannelId = 0;

            outputs.PlayerJoin.Enabled = relay.RelayJoinMessages;
            outputs.PlayerJoin.ChannelId = legacyChatChannel;
            outputs.PlayerJoin.Format = string.IsNullOrWhiteSpace(relay.JoinFormat) ? outputs.PlayerJoin.Format : relay.JoinFormat;

            outputs.PlayerLeave.Enabled = relay.RelayLeaveMessages;
            outputs.PlayerLeave.ChannelId = legacyChatChannel;
            outputs.PlayerLeave.Format = string.IsNullOrWhiteSpace(relay.LeaveFormat) ? outputs.PlayerLeave.Format : relay.LeaveFormat;

            outputs.ServerOnline.Enabled = relay.RelayServerOnlineMessage;
            outputs.ServerOnline.ChannelId = legacyChatChannel;
            outputs.ServerOffline.Enabled = relay.RelayServerOfflineMessage;
            outputs.ServerOffline.ChannelId = legacyChatChannel;
            outputs.TestMessages.Enabled = legacyChatChannel != 0;
            outputs.TestMessages.ChannelId = legacyChatChannel;

            outputs.CommandLogs.Enabled = commandLogging.Enabled;
            outputs.CommandLogs.ChannelId = commandLogging.ChannelId;

            ulong moderationChannel = configuration.Discord == null ? 0UL : configuration.Discord.ModerationLogChannelId;
            outputs.ModerationLogs.Enabled = moderationChannel != 0;
            outputs.ModerationLogs.ChannelId = moderationChannel;
            return outputs;
        }

        private static void FillMissingOutputs(DiscordOutputRoutingSettings outputs)
        {
            DiscordOutputRoutingSettings defaults = DiscordOutputRoutingSettings.CreateDefault();
            if (outputs.GlobalChat == null) outputs.GlobalChat = defaults.GlobalChat;
            if (outputs.LocalChat == null) outputs.LocalChat = defaults.LocalChat;
            if (outputs.GroupChat == null) outputs.GroupChat = defaults.GroupChat;
            if (outputs.PlayerJoin == null) outputs.PlayerJoin = defaults.PlayerJoin;
            if (outputs.PlayerLeave == null) outputs.PlayerLeave = defaults.PlayerLeave;
            if (outputs.ServerOnline == null) outputs.ServerOnline = defaults.ServerOnline;
            if (outputs.ServerOffline == null) outputs.ServerOffline = defaults.ServerOffline;
            if (outputs.TestMessages == null) outputs.TestMessages = defaults.TestMessages;
            if (outputs.CommandLogs == null) outputs.CommandLogs = defaults.CommandLogs;
            if (outputs.ModerationLogs == null) outputs.ModerationLogs = defaults.ModerationLogs;
        }

        private static void ValidateOutputs(DiscordOutputRoutingSettings outputs, ValidationResult result)
        {
            if (outputs == null) return;
            ValidateFormattedOutput(outputs.GlobalChat, "Outputs.GlobalChat", result);
            ValidateFormattedOutput(outputs.LocalChat, "Outputs.LocalChat", result);
            ValidateFormattedOutput(outputs.GroupChat, "Outputs.GroupChat", result);
            ValidateFormattedOutput(outputs.PlayerJoin, "Outputs.PlayerJoin", result);
            ValidateFormattedOutput(outputs.PlayerLeave, "Outputs.PlayerLeave", result);
            ValidateFormattedOutput(outputs.ServerOnline, "Outputs.ServerOnline", result);
            ValidateFormattedOutput(outputs.ServerOffline, "Outputs.ServerOffline", result);
            ValidateFormattedOutput(outputs.TestMessages, "Outputs.TestMessages", result);
            ValidateFormattedOutput(outputs.CommandLogs, "Outputs.CommandLogs", result);
            ValidateChannelOutput(outputs.ModerationLogs, "Outputs.ModerationLogs", result);
        }

        private static void ValidateFormattedOutput(DiscordFormattedOutputSettings output, string field, ValidationResult result)
        {
            ValidateChannelOutput(output, field, result);
            if (output != null && output.Enabled && string.IsNullOrWhiteSpace(output.Format))
                result.AddError(field + ".Format is required when the output is enabled.");
        }

        private static void ValidateChannelOutput(DiscordChannelOutputSettings output, string field, ValidationResult result)
        {
            if (output == null)
            {
                result.AddError(field + " section is missing.");
                return;
            }
            if (output.Enabled && output.ChannelId == 0)
                result.AddError(field + ".ChannelId must be configured when the output is enabled.");
        }

        private static void ValidateCommandLogging(CommandLoggingSettings settings, ValidationResult result)
        {
            if (settings == null) return;
            ValidateCommandNameList(settings.IgnoredCommands, "CommandLogging.IgnoredCommands", result);
            ValidateCommandNameList(settings.RedactedCommands, "CommandLogging.RedactedCommands", result);
        }

        private static void ValidateCommandNameList(IEnumerable<string> entries, string field, ValidationResult result)
        {
            if (entries == null) return;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in entries)
            {
                string value = (entry ?? string.Empty).Trim().TrimStart('/', '@');
                if (value.Length == 0)
                {
                    result.AddError(field + " cannot contain an empty command name.");
                    continue;
                }
                if (value.IndexOf(' ') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                    result.AddError(field + " entries must contain only the command name without arguments.");
                if (!seen.Add(value)) result.AddWarning(field + " contains duplicate command '" + value + "'.");
            }
        }

        private static void ValidateMuteBackend(MuteBackendSettings settings, ValidationResult result)
        {
            if (settings == null)
            {
                result.AddError("Moderation.MuteBackend section is missing.");
                return;
            }

            if (!settings.UsesCommands && !settings.UsesInternalStore)
            {
                result.AddError("Moderation.MuteBackend.Mode must be either Command or Internal.");
                return;
            }

            if (!settings.UsesCommands) return;
            ValidateCommandTemplate(settings.MuteCommand, "Moderation.MuteBackend.MuteCommand", false, result);
            ValidateCommandTemplate(settings.TemporaryMuteCommand, "Moderation.MuteBackend.TemporaryMuteCommand", true, result);
            ValidateCommandTemplate(settings.UnmuteCommand, "Moderation.MuteBackend.UnmuteCommand", false, result);
        }

        private static void ValidateCommandTemplate(string template, string field, bool requiresDuration, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                result.AddError(field + " is required when command mute mode is enabled.");
                return;
            }

            if (template.IndexOf('\r') >= 0 || template.IndexOf('\n') >= 0)
                result.AddError(field + " cannot contain line breaks.");
            if (!template.Contains("{steamid}") && !template.Contains("{player}"))
                result.AddError(field + " must contain {steamid} or {player}.");
            if (requiresDuration && !template.Contains("{duration}"))
                result.AddError(field + " must contain {duration}.");
        }

        private static void ValidateFileName(string value, string field, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError(field + " is required.");
                return;
            }

            if (Path.IsPathRooted(value) || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) || value.IndexOf("..", StringComparison.Ordinal) >= 0)
                result.AddError(field + " must be a simple file name without directories.");
        }

        private static bool HasAnyRole(PermissionSettings permissions)
        {
            return permissions != null &&
                   ((permissions.ViewerRoleIds != null && permissions.ViewerRoleIds.Count > 0) ||
                    (permissions.ModeratorRoleIds != null && permissions.ModeratorRoleIds.Count > 0) ||
                    (permissions.AdministratorRoleIds != null && permissions.AdministratorRoleIds.Count > 0));
        }

        private static void ValidateRoleLists(PermissionSettings permissions, ValidationResult result)
        {
            if (permissions == null) return;
            List<ulong> all = new List<ulong>();
            AddRoles(permissions.ViewerRoleIds, "ViewerRoleIds", all, result);
            AddRoles(permissions.ModeratorRoleIds, "ModeratorRoleIds", all, result);
            AddRoles(permissions.AdministratorRoleIds, "AdministratorRoleIds", all, result);

            IEnumerable<ulong> duplicates = all.GroupBy(x => x).Where(group => group.Count() > 1).Select(group => group.Key);
            foreach (ulong roleId in duplicates)
                result.AddWarning("Discord role ID " + roleId + " appears in more than one permission tier; the highest tier wins.");
        }

        private static void AddRoles(IEnumerable<ulong> roles, string name, ICollection<ulong> all, ValidationResult result)
        {
            if (roles == null) return;
            foreach (ulong role in roles)
            {
                if (role == 0) result.AddError("Permissions." + name + " contains an invalid zero role ID.");
                else all.Add(role);
            }
        }
    }
}

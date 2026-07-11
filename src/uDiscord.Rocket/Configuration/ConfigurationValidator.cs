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

            if (configuration.Discord == null) result.AddError("Discord section is missing.");
            if (configuration.ChatRelay == null) result.AddError("ChatRelay section is missing.");
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
            if ((configuration.ChatRelay.GameToDiscordEnabled || configuration.ChatRelay.DiscordToGameEnabled) && configuration.Discord.ChatChannelId == 0)
                result.AddError("Discord.ChatChannelId must be configured when chat relay is enabled.");
            if (configuration.Moderation.Enabled && configuration.Discord.ModerationLogChannelId == 0)
                result.AddWarning("Discord.ModerationLogChannelId is zero; moderation actions will be persisted but not posted to a Discord log channel.");

            if (configuration.Moderation.Enabled && !HasAnyRole(configuration.Permissions))
                result.AddError("Moderation is enabled but no viewer, moderator, or administrator Discord role IDs are configured.");

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
            {
                result.AddWarning("Discord role ID " + roleId + " appears in more than one permission tier; the highest tier wins.");
            }
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

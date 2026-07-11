using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UDiscord.Core.Models;
using UDiscord.Core.Security;
using UDiscord.Core.Utility;
using UDiscord.Rocket.Configuration;
using UDiscord.Rocket.Game;
using UDiscord.Rocket.Infrastructure;
using UDiscord.Rocket.Persistence;

namespace UDiscord.Rocket.Discord
{
    public sealed class DiscordInteractionHandler
    {
        private readonly Func<UDiscordConfiguration> _configuration;
        private readonly DiscordBotHost _host;
        private readonly MainThreadDispatcher _dispatcher;
        private readonly PlayerResolver _players;
        private readonly ModerationService _moderation;
        private readonly CaseStore _cases;
        private readonly DiscordPermissionService _permissions;
        private readonly SlidingWindowRateLimiter _moderationRateLimiter;

        public DiscordInteractionHandler(
            Func<UDiscordConfiguration> configuration,
            DiscordBotHost host,
            MainThreadDispatcher dispatcher,
            PlayerResolver players,
            ModerationService moderation,
            CaseStore cases)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
            _cases = cases ?? throw new ArgumentNullException(nameof(cases));

            UDiscordConfiguration config = _configuration();
            _permissions = new DiscordPermissionService(config.Permissions, config.Moderation.AllowDiscordAdministratorBypass);
            _moderationRateLimiter = new SlidingWindowRateLimiter(
                Math.Max(1, config.RateLimits.ModerationActionsPerWindow),
                TimeSpan.FromSeconds(Math.Max(1, config.RateLimits.ModerationActionWindowSeconds)));
        }

        public async Task HandleAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            if (interaction == null) return;
            UDiscordConfiguration config = _configuration();
            if (interaction.GuildId != config.Discord.GuildId)
            {
                await SafeRespondAsync(interaction, "This uDiscord instance is not configured for that Discord server.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (config.Discord.CommandChannelIds != null && config.Discord.CommandChannelIds.Count > 0 && !config.Discord.CommandChannelIds.Contains(interaction.ChannelId))
            {
                await RecordDeniedAsync(interaction, "Command used outside an allowed channel.", cancellationToken).ConfigureAwait(false);
                await SafeRespondAsync(interaction, "uDiscord commands are not enabled in this channel.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(interaction.CommandName, "udiscord", StringComparison.OrdinalIgnoreCase)) return;
            if (interaction.Type == 4)
            {
                await HandleAutocompleteAsync(interaction, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (interaction.Type != 2) return;
            string command = (interaction.SubcommandName ?? string.Empty).ToLowerInvariant();
            PermissionTier required = RequiredTier(command);
            if (!_permissions.IsAllowed(interaction, required))
            {
                await RecordDeniedAsync(interaction, "Missing required " + required + " permission tier for /udiscord " + command + ".", cancellationToken).ConfigureAwait(false);
                await SafeRespondAsync(interaction, config.Messages.NoPermission, cancellationToken).ConfigureAwait(false);
                return;
            }

            bool mutating = IsMutating(command);
            if (mutating)
            {
                if (!config.Moderation.Enabled && !string.Equals(command, "say", StringComparison.Ordinal))
                {
                    await SafeRespondAsync(interaction, "Discord moderation is disabled in the uDiscord configuration.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                TimeSpan retry;
                if (!_moderationRateLimiter.TryAcquire(interaction.UserId.ToString(), DateTime.UtcNow, out retry))
                {
                    string message = (config.Messages.RateLimited ?? "You are doing that too quickly. Try again in {seconds}s.")
                        .Replace("{seconds}", Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString());
                    await RecordDeniedAsync(interaction, "Moderation rate limit exceeded.", cancellationToken).ConfigureAwait(false);
                    await SafeRespondAsync(interaction, message, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            try
            {
                await _host.DeferInteractionAsync(interaction, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Unable to defer Discord interaction /udiscord " + command + ".");
                return;
            }

            string response;
            try
            {
                response = await ExecuteAsync(command, interaction, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Unhandled /udiscord " + command + " failure.");
                response = "uDiscord could not complete the command because an internal error occurred. Check the server console with operation context.";
            }

            try
            {
                await _host.EditInteractionResponseAsync(interaction, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                PluginLog.Exception(exception, "Unable to edit Discord interaction response.");
            }
        }

        private async Task<string> ExecuteAsync(string command, DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            switch (command)
            {
                case "status": return await StatusAsync(cancellationToken).ConfigureAwait(false);
                case "players": return await PlayersAsync(cancellationToken).ConfigureAwait(false);
                case "help": return Help();
                case "say": return await SayAsync(interaction, cancellationToken).ConfigureAwait(false);
                case "kick": return await KickAsync(interaction, cancellationToken).ConfigureAwait(false);
                case "ban": return await BanAsync(interaction, null, cancellationToken).ConfigureAwait(false);
                case "tempban": return await TemporaryBanAsync(interaction, cancellationToken).ConfigureAwait(false);
                case "unban": return await UnbanAsync(interaction, cancellationToken).ConfigureAwait(false);
                case "mute": return await MuteAsync(interaction, null, cancellationToken).ConfigureAwait(false);
                case "tempmute": return await TemporaryMuteAsync(interaction, cancellationToken).ConfigureAwait(false);
                case "unmute": return await UnmuteAsync(interaction, cancellationToken).ConfigureAwait(false);
                case "case": return Case(interaction);
                case "history": return History(interaction);
                default: return "Unknown uDiscord subcommand. Run `/udiscord help`.";
            }
        }

        private async Task<string> StatusAsync(CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            string gameStatus = await _dispatcher.RunAsync(() =>
                "Players: " + SDG.Unturned.Provider.clients.Count + "/" + SDG.Unturned.Provider.maxPlayers,
                cancellationToken).ConfigureAwait(false);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("**uDiscord 1.0.0**");
            builder.AppendLine("Connection: " + _host.State);
            builder.AppendLine("Server: " + MessageSanitizer.SafeDisplayName(config.ServerDisplayName, 100));
            builder.AppendLine(gameStatus);
            builder.AppendLine("Active mutes: " + _moderation.ActiveMuteCount);
            builder.AppendLine("Outbound queue: " + _host.OutboundQueueCount + "/" + config.RateLimits.OutboundQueueCapacity);
            builder.Append("Next case: #").Append(_cases.PeekNextCaseId());
            return builder.ToString();
        }

        private async Task<string> PlayersAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<OnlinePlayerInfo> players = await _dispatcher.RunAsync(() => _players.GetOnlinePlayers(), cancellationToken).ConfigureAwait(false);
            if (players.Count == 0) return "No players are currently connected.";

            StringBuilder builder = new StringBuilder("**Online players (" + players.Count + ")**\n");
            foreach (OnlinePlayerInfo player in players)
            {
                string line = "• " + player.DisplayName + " — `" + player.SteamId + "`\n";
                if (builder.Length + line.Length > 1950)
                {
                    builder.Append("…list truncated");
                    break;
                }
                builder.Append(line);
            }
            return builder.ToString().TrimEnd();
        }

        private static string Help()
        {
            return "**uDiscord commands**\n" +
                   "Viewer: `status`, `players`, `case`, `history`, `help`\n" +
                   "Moderator: `kick`, `mute`, `tempmute`, `unmute`\n" +
                   "Administrator: `ban`, `tempban`, `unban`, `say`\n\n" +
                   "Use online-player autocomplete whenever possible. Offline bans, mutes, unbans, and unmute actions require a Steam64 ID. Durations support `s`, `m`, `h`, `d`, and `w`.";
        }

        private async Task<string> SayAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            string raw = DiscordInteractionParser.GetString(interaction, "message");
            string message = MessageSanitizer.FromDiscordToGame(raw, config.ChatRelay.MaximumGameMessageLength);
            if (message.Length == 0) return "Announcement text is empty after sanitization.";
            string rendered = "<color=#FABD0F>[Discord Staff]</color> <color=#D3D3D3>" + message + "</color>";
            Color color;
            if (!ColorUtility.TryParseHtmlString(config.ChatRelay.GameChatColorHex, out color)) color = Color.white;
            string operationId = OperationIds.New("say");
            ModerationExecution execution = await _dispatcher.RunAsync(() => _moderation.Announce(rendered, color, true), cancellationToken).ConfigureAwait(false);
            return await RecordExecutionAsync(interaction, execution, message, operationId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> KickAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            string reason;
            string error;
            if (!TryGetReason(interaction, out reason, out error)) return error;
            string target = DiscordInteractionParser.GetString(interaction, "target");
            string operationId = OperationIds.New("kick");
            ModerationExecution execution = await _dispatcher.RunAsync(() => _moderation.Kick(target, reason), cancellationToken).ConfigureAwait(false);
            return await RecordExecutionAsync(interaction, execution, reason, operationId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> BanAsync(DiscordInteraction interaction, TimeSpan? duration, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            if (!duration.HasValue && config.Moderation.RequirePermanentBanConfirmation && !DiscordInteractionParser.GetBoolean(interaction, "confirm", false))
                return "Permanent ban cancelled because `confirm` was not set to true.";

            string reason;
            string error;
            if (!TryGetReason(interaction, out reason, out error)) return error;
            string target = DiscordInteractionParser.GetString(interaction, "target");
            string operationId = OperationIds.New(duration.HasValue ? "tempban" : "ban");
            ModerationExecution execution = await _dispatcher.RunAsync(() => _moderation.Ban(target, reason, duration, config.Moderation.AllowOfflineBansBySteamId), cancellationToken).ConfigureAwait(false);
            return await RecordExecutionAsync(interaction, execution, reason, operationId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> TemporaryBanAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            DurationParseResult parsed = DurationParser.Parse(
                DiscordInteractionParser.GetString(interaction, "duration"),
                TimeSpan.FromDays(config.Moderation.MaximumTemporaryBanDays),
                false);
            if (!parsed.Success) return parsed.Error;
            return await BanAsync(interaction, parsed.Duration, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> UnbanAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            string reason;
            string error;
            if (!TryGetReason(interaction, out reason, out error)) return error;
            string steamId = DiscordInteractionParser.GetString(interaction, "steamid");
            string operationId = OperationIds.New("unban");
            ModerationExecution execution = await _dispatcher.RunAsync(() => _moderation.Unban(steamId), cancellationToken).ConfigureAwait(false);
            return await RecordExecutionAsync(interaction, execution, reason, operationId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> MuteAsync(DiscordInteraction interaction, TimeSpan? duration, CancellationToken cancellationToken)
        {
            string reason;
            string error;
            if (!TryGetReason(interaction, out reason, out error)) return error;
            string target = DiscordInteractionParser.GetString(interaction, "target");
            string operationId = OperationIds.New(duration.HasValue ? "tempmute" : "mute");
            ModerationExecution execution = await _dispatcher.RunAsync(() =>
                _moderation.Mute(target, reason, duration, interaction.UserId.ToString(), interaction.UserDisplayName, operationId),
                cancellationToken).ConfigureAwait(false);
            return await RecordExecutionAsync(interaction, execution, reason, operationId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> TemporaryMuteAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            DurationParseResult parsed = DurationParser.Parse(
                DiscordInteractionParser.GetString(interaction, "duration"),
                TimeSpan.FromDays(config.Moderation.MaximumTemporaryMuteDays),
                false);
            if (!parsed.Success) return parsed.Error;
            return await MuteAsync(interaction, parsed.Duration, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> UnmuteAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            string reason;
            string error;
            if (!TryGetReason(interaction, out reason, out error)) return error;
            string target = DiscordInteractionParser.GetString(interaction, "target");
            string operationId = OperationIds.New("unmute");
            ModerationExecution execution = await _dispatcher.RunAsync(() => _moderation.Unmute(target), cancellationToken).ConfigureAwait(false);
            return await RecordExecutionAsync(interaction, execution, reason, operationId, cancellationToken).ConfigureAwait(false);
        }

        private string Case(DiscordInteraction interaction)
        {
            long caseId = DiscordInteractionParser.GetInteger(interaction, "case_id", 0);
            ModerationCase item = _cases.Get(caseId);
            return item == null ? "Case #" + caseId + " is not loaded or does not exist." : FormatCase(item);
        }

        private string History(DiscordInteraction interaction)
        {
            string target = DiscordInteractionParser.GetString(interaction, "target").Trim();
            ulong steamId;
            if (!ulong.TryParse(target, out steamId))
                return "History lookup requires a Steam64 ID. Select a player from autocomplete.";
            IReadOnlyList<ModerationCase> history = _cases.GetHistory(steamId.ToString(), 10);
            if (history.Count == 0) return "No loaded moderation history was found for " + steamId + ".";

            StringBuilder builder = new StringBuilder("**Recent history for " + steamId + "**\n");
            foreach (ModerationCase item in history)
            {
                string line = "• #" + item.CaseId + " " + item.Action + " — " + (item.Succeeded ? "completed" : "failed") + " — " + item.CreatedUtc.ToString("u") + "\n";
                if (builder.Length + line.Length > 1950) break;
                builder.Append(line);
            }
            return builder.ToString().TrimEnd();
        }

        private async Task HandleAutocompleteAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
        {
            if (!_permissions.IsAllowed(interaction, PermissionTier.Viewer))
            {
                await _host.RespondAutocompleteAsync(interaction, new List<DiscordAutocompleteChoice>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            string focused = DiscordInteractionParser.GetFocusedOptionName(interaction);
            if (!string.Equals(focused, "target", StringComparison.Ordinal))
            {
                await _host.RespondAutocompleteAsync(interaction, new List<DiscordAutocompleteChoice>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            string query = DiscordInteractionParser.GetString(interaction, focused);
            IReadOnlyList<OnlinePlayerInfo> players = await _dispatcher.RunAsync(() => _players.Autocomplete(query, 25), cancellationToken).ConfigureAwait(false);
            List<DiscordAutocompleteChoice> choices = players.Select(player => new DiscordAutocompleteChoice
            {
                Name = MessageSanitizer.Truncate(player.DisplayName + " | " + player.SteamId, 100),
                Value = player.SteamId
            }).ToList();
            await _host.RespondAutocompleteAsync(interaction, choices, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> RecordExecutionAsync(DiscordInteraction interaction, ModerationExecution execution, string reason, string operationId, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            ModerationCase item = _cases.Record(new ModerationCase
            {
                OperationId = operationId,
                Action = execution.Action,
                ActorDiscordId = interaction.UserId.ToString(),
                ActorDisplayName = MessageSanitizer.SafeDisplayName(interaction.UserDisplayName, 100),
                TargetSteamId = execution.TargetSteamId ?? string.Empty,
                TargetDisplayName = execution.TargetDisplayName ?? "unknown",
                Reason = MessageSanitizer.FromGameToDiscord(reason, config.Moderation.MaximumReasonLength),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = execution.ExpiresUtc,
                Succeeded = execution.Success,
                Result = execution.Message ?? (execution.Success ? "Completed." : "Failed."),
                ServerName = config.ServerDisplayName,
                PluginVersion = "1.0.0"
            });

            if (item.Succeeded || config.Moderation.LogDeniedActions)
            {
                try { await _host.SendModerationLogAsync(item, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { PluginLog.Exception(exception, "Unable to post moderation case #" + item.CaseId + " to Discord logs."); }
            }

            string targetLine = string.IsNullOrWhiteSpace(item.TargetSteamId)
                ? item.TargetDisplayName
                : item.TargetDisplayName + " (`" + item.TargetSteamId + "`)";
            return "**Case #" + item.CaseId + "**\n" +
                   "Action: " + item.Action + "\n" +
                   "Target: " + targetLine + "\n" +
                   "Result: " + item.Result + "\n" +
                   "Operation: `" + item.OperationId + "`";
        }

        private async Task RecordDeniedAsync(DiscordInteraction interaction, string reason, CancellationToken cancellationToken)
        {
            UDiscordConfiguration config = _configuration();
            if (!config.Moderation.LogDeniedActions) return;
            ModerationCase item = _cases.Record(new ModerationCase
            {
                OperationId = OperationIds.New("denied"),
                Action = ModerationActionType.Denied,
                ActorDiscordId = interaction.UserId.ToString(),
                ActorDisplayName = MessageSanitizer.SafeDisplayName(interaction.UserDisplayName, 100),
                TargetSteamId = string.Empty,
                TargetDisplayName = "n/a",
                Reason = reason,
                CreatedUtc = DateTime.UtcNow,
                Succeeded = false,
                Result = "Denied before game-state mutation.",
                ServerName = config.ServerDisplayName,
                PluginVersion = "1.0.0"
            });
            try { await _host.SendModerationLogAsync(item, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { PluginLog.Debug("Unable to post denied case #" + item.CaseId + ": " + exception.Message); }
        }

        private bool TryGetReason(DiscordInteraction interaction, out string reason, out string error)
        {
            UDiscordConfiguration config = _configuration();
            reason = MessageSanitizer.FromGameToDiscord(
                DiscordInteractionParser.GetString(interaction, "reason"),
                config.Moderation.MaximumReasonLength);
            error = null;
            if (!config.Moderation.RequireReasons && reason.Length == 0)
            {
                reason = "No reason supplied.";
                return true;
            }
            if (reason.Length < config.Moderation.MinimumReasonLength)
            {
                error = "Reason must be at least " + config.Moderation.MinimumReasonLength + " characters.";
                return false;
            }
            return true;
        }

        private async Task SafeRespondAsync(DiscordInteraction interaction, string message, CancellationToken cancellationToken)
        {
            try { await _host.RespondInteractionAsync(interaction, message, true, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { PluginLog.Debug("Unable to respond to denied Discord interaction: " + exception.Message); }
        }

        private static PermissionTier RequiredTier(string command)
        {
            switch (command)
            {
                case "kick":
                case "mute":
                case "tempmute":
                case "unmute":
                    return PermissionTier.Moderator;
                case "ban":
                case "tempban":
                case "unban":
                case "say":
                    return PermissionTier.Administrator;
                default:
                    return PermissionTier.Viewer;
            }
        }

        private static bool IsMutating(string command)
        {
            switch (command)
            {
                case "kick":
                case "ban":
                case "tempban":
                case "unban":
                case "mute":
                case "tempmute":
                case "unmute":
                case "say":
                    return true;
                default:
                    return false;
            }
        }

        private static string FormatCase(ModerationCase item)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("**Case #" + item.CaseId + "**");
            builder.AppendLine("Action: " + item.Action);
            builder.AppendLine("Status: " + (item.Succeeded ? "Completed" : "Failed/Denied"));
            builder.AppendLine("Target: " + item.TargetDisplayName + (string.IsNullOrWhiteSpace(item.TargetSteamId) ? string.Empty : " (`" + item.TargetSteamId + "`)"));
            builder.AppendLine("Moderator: " + item.ActorDisplayName + " (`" + item.ActorDiscordId + "`)");
            builder.AppendLine("Reason: " + item.Reason);
            builder.AppendLine("Created: " + item.CreatedUtc.ToString("u"));
            if (item.ExpiresUtc.HasValue) builder.AppendLine("Expires: " + item.ExpiresUtc.Value.ToString("u"));
            builder.AppendLine("Result: " + item.Result);
            builder.Append("Operation: `").Append(item.OperationId).Append('`');
            return MessageSanitizer.Truncate(builder.ToString(), 2000);
        }
    }
}

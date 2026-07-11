using System;
using System.Collections.Generic;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using UDiscord.Core.Models;
using UDiscord.Core.Utility;
using UDiscord.Rocket.Persistence;

namespace UDiscord.Rocket.Game
{
    public sealed class ModerationService
    {
        private readonly PlayerResolver _players;
        private readonly MuteStore _mutes;

        public int ActiveMuteCount => _mutes.Count;

        public ModerationService(PlayerResolver players, MuteStore mutes)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _mutes = mutes ?? throw new ArgumentNullException(nameof(mutes));
        }

        public ModerationExecution Kick(string target, string reason)
        {
            PlayerResolution resolution = _players.ResolveOnline(target);
            if (resolution.Status == PlayerResolutionStatus.Ambiguous)
                return ModerationExecution.Failed(ModerationActionType.Kick, "Target is ambiguous: " + string.Join(", ", resolution.AmbiguousMatches));
            if (resolution.Status != PlayerResolutionStatus.Found || resolution.Player == null)
                return ModerationExecution.Failed(ModerationActionType.Kick, "Kick requires an online player.");

            Provider.kick(resolution.SteamId, reason);
            return new ModerationExecution
            {
                Success = true,
                Action = ModerationActionType.Kick,
                TargetSteamId = resolution.SteamIdText,
                TargetDisplayName = resolution.DisplayName,
                Message = "Kicked " + resolution.DisplayName + " (" + resolution.SteamIdText + ")."
            };
        }

        public ModerationExecution Ban(string target, string reason, TimeSpan? duration, bool allowOffline)
        {
            ModerationActionType action = duration.HasValue ? ModerationActionType.TemporaryBan : ModerationActionType.Ban;
            PlayerResolution resolution = _players.ResolveOnline(target);
            if (resolution.Status == PlayerResolutionStatus.Ambiguous)
                return ModerationExecution.Failed(action, "Target is ambiguous: " + string.Join(", ", resolution.AmbiguousMatches));

            CSteamID steamId;
            SteamPlayer online = null;
            string displayName;
            string steamIdText;
            if (resolution.Status == PlayerResolutionStatus.Found)
            {
                steamId = resolution.SteamId;
                online = resolution.Player;
                displayName = resolution.DisplayName;
                steamIdText = resolution.SteamIdText;
            }
            else
            {
                if (!allowOffline)
                    return ModerationExecution.Failed(action, "Offline bans are disabled by configuration and the target is not online.");
                ulong raw;
                if (!ulong.TryParse((target ?? string.Empty).Trim(), out raw))
                    return ModerationExecution.Failed(action, "Offline bans require a valid Steam64 ID.");
                steamId = new CSteamID(raw);
                displayName = raw.ToString();
                steamIdText = raw.ToString();
            }

            uint ip = 0;
            var connection = Provider.findTransportConnection(steamId);
            if (connection != null) connection.TryGetIPv4Address(out ip);
            IEnumerable<byte[]> hwids = online?.playerID?.GetHwids();

            uint seconds;
            DateTime? expiresUtc = null;
            if (duration.HasValue)
            {
                double total = Math.Ceiling(duration.Value.TotalSeconds);
                if (total <= 0 || total >= SteamBlacklist.PERMANENT)
                    return ModerationExecution.Failed(action, "Temporary ban duration is outside the supported native range.");
                seconds = (uint)total;
                expiresUtc = DateTime.UtcNow.AddSeconds(seconds);
            }
            else
            {
                seconds = SteamBlacklist.PERMANENT;
            }

            Provider.requestBanPlayer(CSteamID.Nil, steamId, ip, hwids, reason, seconds);
            return new ModerationExecution
            {
                Success = true,
                Action = action,
                TargetSteamId = steamIdText,
                TargetDisplayName = displayName,
                ExpiresUtc = expiresUtc,
                Message = duration.HasValue
                    ? "Temporarily banned " + displayName + " (" + steamIdText + ") for " + DurationParser.Format(duration.Value) + "."
                    : "Permanently banned " + displayName + " (" + steamIdText + ")."
            };
        }

        public ModerationExecution Unban(string steamIdText)
        {
            ulong raw;
            if (!ulong.TryParse((steamIdText ?? string.Empty).Trim(), out raw))
                return ModerationExecution.Failed(ModerationActionType.Unban, "Unban requires a valid Steam64 ID.");

            CSteamID steamId = new CSteamID(raw);
            bool removed = Provider.requestUnbanPlayer(CSteamID.Nil, steamId);
            if (!removed) return ModerationExecution.Failed(ModerationActionType.Unban, "No native Unturned ban was found for " + raw + ".");
            return new ModerationExecution
            {
                Success = true,
                Action = ModerationActionType.Unban,
                TargetSteamId = raw.ToString(),
                TargetDisplayName = raw.ToString(),
                Message = "Unbanned " + raw + "."
            };
        }

        public ModerationExecution Mute(string target, string reason, TimeSpan? duration, string actorDiscordId, string actorDisplayName, string operationId)
        {
            ModerationActionType action = duration.HasValue ? ModerationActionType.TemporaryMute : ModerationActionType.Mute;
            PlayerResolution resolution = _players.ResolveOnline(target);
            if (resolution.Status == PlayerResolutionStatus.Ambiguous)
                return ModerationExecution.Failed(action, "Target is ambiguous: " + string.Join(", ", resolution.AmbiguousMatches));

            string steamId;
            string displayName;
            if (resolution.Status == PlayerResolutionStatus.Found)
            {
                steamId = resolution.SteamIdText;
                displayName = resolution.DisplayName;
            }
            else
            {
                ulong raw;
                if (!ulong.TryParse((target ?? string.Empty).Trim(), out raw))
                    return ModerationExecution.Failed(action, "Offline mutes require a valid Steam64 ID.");
                steamId = raw.ToString();
                displayName = steamId;
            }

            DateTime? expiresUtc = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : (DateTime?)null;
            _mutes.Upsert(new MuteRecord
            {
                SteamId = steamId,
                LastKnownName = displayName,
                Reason = reason,
                ActorDiscordId = actorDiscordId,
                ActorDisplayName = actorDisplayName,
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = expiresUtc,
                OperationId = operationId
            });

            return new ModerationExecution
            {
                Success = true,
                Action = action,
                TargetSteamId = steamId,
                TargetDisplayName = displayName,
                ExpiresUtc = expiresUtc,
                Message = duration.HasValue
                    ? "Muted " + displayName + " (" + steamId + ") for " + DurationParser.Format(duration.Value) + "."
                    : "Permanently muted " + displayName + " (" + steamId + ")."
            };
        }

        public ModerationExecution Unmute(string target)
        {
            PlayerResolution resolution = _players.ResolveOnline(target);
            if (resolution.Status == PlayerResolutionStatus.Ambiguous)
                return ModerationExecution.Failed(ModerationActionType.Unmute, "Target is ambiguous: " + string.Join(", ", resolution.AmbiguousMatches));

            string steamId;
            string displayName;
            if (resolution.Status == PlayerResolutionStatus.Found)
            {
                steamId = resolution.SteamIdText;
                displayName = resolution.DisplayName;
            }
            else
            {
                ulong raw;
                if (!ulong.TryParse((target ?? string.Empty).Trim(), out raw))
                    return ModerationExecution.Failed(ModerationActionType.Unmute, "Unmute requires an online player or Steam64 ID.");
                steamId = raw.ToString();
                displayName = steamId;
            }

            MuteRecord removed;
            if (!_mutes.Remove(steamId, out removed))
                return ModerationExecution.Failed(ModerationActionType.Unmute, "No active uDiscord mute was found for " + steamId + ".");

            return new ModerationExecution
            {
                Success = true,
                Action = ModerationActionType.Unmute,
                TargetSteamId = steamId,
                TargetDisplayName = string.IsNullOrWhiteSpace(removed.LastKnownName) ? displayName : removed.LastKnownName,
                Message = "Unmuted " + (string.IsNullOrWhiteSpace(removed.LastKnownName) ? displayName : removed.LastKnownName) + " (" + steamId + ")."
            };
        }

        public ModerationExecution Announce(string message, Color color, bool richText)
        {
            foreach (SteamPlayer client in Provider.clients)
            {
                if (client?.player == null) continue;
                Rocket.Unturned.Player.UnturnedPlayer target = Rocket.Unturned.Player.UnturnedPlayer.FromSteamPlayer(client);
                UnturnedChat.Say(target, message, color, richText);
            }

            return new ModerationExecution
            {
                Success = true,
                Action = ModerationActionType.Announcement,
                TargetSteamId = string.Empty,
                TargetDisplayName = "Server",
                Message = "Announcement sent to " + Provider.clients.Count + " connected players."
            };
        }
    }
}

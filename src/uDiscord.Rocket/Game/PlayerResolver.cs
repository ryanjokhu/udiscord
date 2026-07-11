using System;
using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;
using Steamworks;
using UDiscord.Core.Security;

namespace UDiscord.Rocket.Game
{
    public enum PlayerResolutionStatus
    {
        Found,
        NotFound,
        Ambiguous
    }

    public sealed class PlayerResolution
    {
        public PlayerResolutionStatus Status { get; set; }
        public SteamPlayer Player { get; set; }
        public CSteamID SteamId { get; set; }
        public string SteamIdText { get; set; }
        public string DisplayName { get; set; }
        public IReadOnlyList<string> AmbiguousMatches { get; set; }
    }

    public sealed class OnlinePlayerInfo
    {
        public string SteamId { get; set; }
        public string DisplayName { get; set; }
        public string AccountName { get; set; }
        public string CharacterName { get; set; }
    }

    public sealed class PlayerResolver
    {
        public PlayerResolution ResolveOnline(string target)
        {
            string value = (target ?? string.Empty).Trim();
            if (value.Length == 0) return NotFound();

            ulong rawSteamId;
            if (ulong.TryParse(value, out rawSteamId))
            {
                CSteamID steamId = new CSteamID(rawSteamId);
                SteamPlayer byId = Provider.clients.FirstOrDefault(client => client != null && client.playerID != null && client.playerID.steamID == steamId);
                if (byId != null) return Found(byId);
                return new PlayerResolution
                {
                    Status = PlayerResolutionStatus.NotFound,
                    SteamId = steamId,
                    SteamIdText = rawSteamId.ToString(),
                    DisplayName = rawSteamId.ToString(),
                    AmbiguousMatches = new List<string>()
                };
            }

            List<SteamPlayer> candidates = Provider.clients.Where(client => client != null && client.playerID != null).ToList();
            List<SteamPlayer> exact = candidates.Where(client => Names(client).Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))).ToList();
            if (exact.Count == 1) return Found(exact[0]);
            if (exact.Count > 1) return Ambiguous(exact);

            List<SteamPlayer> partial = candidates.Where(client => Names(client).Any(name => name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (partial.Count == 1) return Found(partial[0]);
            if (partial.Count > 1) return Ambiguous(partial);
            return NotFound();
        }

        public IReadOnlyList<OnlinePlayerInfo> GetOnlinePlayers()
        {
            return Provider.clients
                .Where(client => client != null && client.playerID != null)
                .Select(ToInfo)
                .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<OnlinePlayerInfo> Autocomplete(string query, int maximum)
        {
            string value = (query ?? string.Empty).Trim();
            IEnumerable<OnlinePlayerInfo> players = GetOnlinePlayers();
            if (value.Length > 0)
            {
                players = players.Where(info =>
                    info.DisplayName.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.AccountName.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.CharacterName.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.SteamId.IndexOf(value, StringComparison.Ordinal) >= 0);
            }
            return players.Take(Math.Max(1, Math.Min(25, maximum))).ToList();
        }

        public static string GetDisplayName(SteamPlayer player)
        {
            if (player?.playerID == null) return "unknown";
            string name = !string.IsNullOrWhiteSpace(player.playerID.characterName)
                ? player.playerID.characterName
                : player.playerID.playerName;
            return MessageSanitizer.SafeDisplayName(name, 64);
        }

        private static IEnumerable<string> Names(SteamPlayer player)
        {
            if (player?.playerID == null) yield break;
            if (!string.IsNullOrWhiteSpace(player.playerID.playerName)) yield return player.playerID.playerName;
            if (!string.IsNullOrWhiteSpace(player.playerID.characterName)) yield return player.playerID.characterName;
            if (!string.IsNullOrWhiteSpace(player.playerID.nickName)) yield return player.playerID.nickName;
        }

        private static PlayerResolution Found(SteamPlayer player)
        {
            return new PlayerResolution
            {
                Status = PlayerResolutionStatus.Found,
                Player = player,
                SteamId = player.playerID.steamID,
                SteamIdText = player.playerID.steamID.m_SteamID.ToString(),
                DisplayName = GetDisplayName(player),
                AmbiguousMatches = new List<string>()
            };
        }

        private static PlayerResolution Ambiguous(IEnumerable<SteamPlayer> players)
        {
            List<string> matches = players
                .Take(10)
                .Select(player => GetDisplayName(player) + " (" + player.playerID.steamID.m_SteamID + ")")
                .ToList();
            return new PlayerResolution
            {
                Status = PlayerResolutionStatus.Ambiguous,
                AmbiguousMatches = matches
            };
        }

        private static PlayerResolution NotFound()
        {
            return new PlayerResolution
            {
                Status = PlayerResolutionStatus.NotFound,
                AmbiguousMatches = new List<string>()
            };
        }

        private static OnlinePlayerInfo ToInfo(SteamPlayer player)
        {
            return new OnlinePlayerInfo
            {
                SteamId = player.playerID.steamID.m_SteamID.ToString(),
                DisplayName = GetDisplayName(player),
                AccountName = MessageSanitizer.SafeDisplayName(player.playerID.playerName, 64),
                CharacterName = MessageSanitizer.SafeDisplayName(player.playerID.characterName, 64)
            };
        }
    }
}

using System;
using System.Collections.Generic;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
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
        private readonly Func<UDiscordConfiguration> _configuration;
        private readonly MuteStore _mutes;
        private readonly DiscordBotHost _discord;
        private readonly Dictionary<string, DateTime> _muteReminderTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private bool _subscribed;

        public GameBridge(Func<UDiscordConfiguration> configuration, MuteStore mutes, DiscordBotHost discord)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _mutes = mutes ?? throw new ArgumentNullException(nameof(mutes));
            _discord = discord ?? throw new ArgumentNullException(nameof(discord));
        }

        public void Subscribe()
        {
            if (_subscribed) return;
            ChatManager.onChatted += OnChatted;
            ChatManager.onServerFormattingMessage += OnServerFormattingMessage;
            U.Events.OnPlayerConnected += OnPlayerConnected;
            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            ChatManager.onChatted -= OnChatted;
            ChatManager.onServerFormattingMessage -= OnServerFormattingMessage;
            U.Events.OnPlayerConnected -= OnPlayerConnected;
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            _subscribed = false;
            _muteReminderTimes.Clear();
        }

        public void BroadcastDiscordMessage(string author, string message, IReadOnlyList<DiscordAttachment> attachments)
        {
            UDiscordConfiguration config = _configuration();
            if (config?.ChatRelay == null || !config.ChatRelay.DiscordToGameEnabled) return;

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
                if (client?.player == null) continue;
                UnturnedPlayer target = UnturnedPlayer.FromSteamPlayer(client);
                UnturnedChat.Say(target, rendered, color, config.ChatRelay.UseRichTextInGame);
            }
        }

        public string BuildPresenceActivity()
        {
            UDiscordConfiguration config = _configuration();
            string template = config?.Discord?.ActivityTemplate ?? "{players}/{maxplayers} players";
            return template
                .Replace("{players}", Provider.clients.Count.ToString())
                .Replace("{maxplayers}", Provider.maxPlayers.ToString())
                .Replace("{server}", config?.ServerDisplayName ?? "Unturned Server");
        }

        private void OnChatted(SteamPlayer steamPlayer, EChatMode mode, ref Color color, ref bool isRich, string text, ref bool isVisible)
        {
            if (steamPlayer?.playerID == null || string.IsNullOrWhiteSpace(text)) return;
            UDiscordConfiguration config = _configuration();
            MuteBackendSettings backend = config?.Moderation?.MuteBackend;
            if (backend == null || !backend.UsesInternalStore) return;

            string steamId = steamPlayer.playerID.steamID.m_SteamID.ToString();
            MuteRecord mute;
            if (_mutes.TryGetActive(steamId, DateTime.UtcNow, out mute))
            {
                isVisible = false;
                SendMuteReminder(steamPlayer, mute, config);
            }
        }

        private void OnServerFormattingMessage(SteamPlayer steamPlayer, EChatMode mode, ref string text)
        {
            if (steamPlayer?.playerID == null || string.IsNullOrWhiteSpace(text)) return;
            UDiscordConfiguration config = _configuration();
            if (config?.ChatRelay == null || !config.ChatRelay.GameToDiscordEnabled || !config.ChatRelay.RelayGlobalChat) return;
            if (mode != EChatMode.GLOBAL) return;

            string steamId = steamPlayer.playerID.steamID.m_SteamID.ToString();
            string playerName = PlayerResolver.GetDisplayName(steamPlayer);
            string safeMessage = MessageSanitizer.FromGameToDiscord(text, config.ChatRelay.MaximumDiscordMessageLength);
            if (safeMessage.Length == 0) return;
            string rendered = (config.ChatRelay.GameToDiscordFormat ?? "[Global] {player}: {message}")
                .Replace("{player}", playerName)
                .Replace("{steamid}", steamId)
                .Replace("{message}", safeMessage);
            _discord.TryQueueChatMessage(rendered);
        }

        private void OnPlayerConnected(UnturnedPlayer player)
        {
            if (player == null) return;
            UDiscordConfiguration config = _configuration();
            if (config?.ChatRelay?.RelayJoinMessages == true)
            {
                string message = (config.ChatRelay.JoinFormat ?? "{player} joined the server.")
                    .Replace("{player}", MessageSanitizer.SafeDisplayName(player.CharacterName, 64))
                    .Replace("{steamid}", player.CSteamID.m_SteamID.ToString());
                _discord.TryQueueChatMessage(message);
            }
            _discord.RequestPresenceUpdate();
        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            if (player == null) return;
            UDiscordConfiguration config = _configuration();
            if (config?.ChatRelay?.RelayLeaveMessages == true)
            {
                string message = (config.ChatRelay.LeaveFormat ?? "{player} left the server.")
                    .Replace("{player}", MessageSanitizer.SafeDisplayName(player.CharacterName, 64))
                    .Replace("{steamid}", player.CSteamID.m_SteamID.ToString());
                _discord.TryQueueChatMessage(message);
            }
            _discord.RequestPresenceUpdate();
        }

        private void SendMuteReminder(SteamPlayer player, MuteRecord mute, UDiscordConfiguration config)
        {
            string steamId = mute.SteamId;
            DateTime now = DateTime.UtcNow;
            DateTime last;
            int cooldown = Math.Max(1, config?.Moderation?.MuteReminderCooldownSeconds ?? 5);
            if (_muteReminderTimes.TryGetValue(steamId, out last) && now - last < TimeSpan.FromSeconds(cooldown)) return;
            _muteReminderTimes[steamId] = now;

            string message;
            if (mute.IsPermanent)
            {
                message = config?.Messages?.MutedReminderPermanent ?? "You are permanently muted. Reason: {reason}";
            }
            else
            {
                TimeSpan remaining = mute.ExpiresUtc.Value - now;
                message = config?.Messages?.MutedReminderTemporary ?? "You are muted for {remaining}. Reason: {reason}";
                message = message.Replace("{remaining}", DurationParser.Format(remaining));
            }
            message = message.Replace("{reason}", MessageSanitizer.FromGameToDiscord(mute.Reason, 200));
            UnturnedPlayer target = UnturnedPlayer.FromSteamPlayer(player);
            UnturnedChat.Say(target, message, Color.red);
        }

        private static Color ParseColor(string value, Color fallback)
        {
            Color parsed;
            return !string.IsNullOrWhiteSpace(value) && ColorUtility.TryParseHtmlString(value, out parsed) ? parsed : fallback;
        }
    }
}

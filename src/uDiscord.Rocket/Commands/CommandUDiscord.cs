using System;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Core.Logging;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using UnityEngine;

namespace UDiscord.Rocket.Commands
{
    public sealed class CommandUDiscord : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "udiscord";
        public string Help => "Inspect and manage the embedded uDiscord bot.";
        public string Syntax => "/udiscord <status|reload|reconnect|test>";
        public List<string> Aliases => new List<string> { "udc" };
        public List<string> Permissions => new List<string> { "udiscord.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UDiscordPlugin plugin = UDiscordPlugin.Instance;
            if (plugin == null)
            {
                Reply(caller, "uDiscord is not loaded.", Color.red);
                return;
            }

            string subcommand = command != null && command.Length > 0
                ? (command[0] ?? string.Empty).Trim().ToLowerInvariant()
                : "status";

            switch (subcommand)
            {
                case "status":
                    Reply(caller, plugin.BuildStatusText(), Color.white);
                    return;

                case "reload":
                    string reloadResponse;
                    bool reloadStarted = plugin.BeginReload(out reloadResponse);
                    Reply(caller, reloadResponse, reloadStarted ? Color.green : Color.red);
                    return;

                case "reconnect":
                    if (plugin.RequestReconnect())
                        Reply(caller, plugin.Text("ReconnectRequested"), Color.green);
                    else
                        Reply(caller, plugin.Text("NotReady"), Color.red);
                    return;

                case "test":
                    if (plugin.QueueTestMessage())
                        Reply(caller, plugin.Text("TestQueued"), Color.green);
                    else
                        Reply(caller, plugin.Text("TestFailed"), Color.red);
                    return;

                default:
                    Reply(caller, plugin.Text("Usage"), Color.yellow);
                    return;
            }
        }

        private static void Reply(IRocketPlayer caller, string message, Color color)
        {
            UnturnedPlayer player = caller as UnturnedPlayer;
            if (player != null)
            {
                UnturnedChat.Say(player, message, color);
                return;
            }

            global::Rocket.Core.Logging.Logger.Log("[uDiscord] " + message);
        }
    }
}

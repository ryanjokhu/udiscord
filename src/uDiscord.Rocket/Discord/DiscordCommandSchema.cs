using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UDiscord.Rocket.Discord
{
    public static class DiscordCommandSchema
    {
        public static JArray Build(bool requireReasons, bool requirePermanentBanConfirmation)
        {
            List<JObject> banOptions = new List<JObject>
            {
                Target(),
                String("reason", "Reason for the permanent ban.", requireReasons)
            };
            if (requirePermanentBanConfirmation)
                banOptions.Add(Boolean("confirm", "Confirm the permanent ban.", true));

            JArray subcommands = new JArray
            {
                Subcommand("status", "Show the Discord connection and Unturned server status."),
                Subcommand("players", "List players currently connected to the Unturned server."),
                Subcommand("help", "Show uDiscord command and permission information."),
                Subcommand("say", "Broadcast a staff announcement in game.",
                    String("message", "Announcement text.", true)),
                Subcommand("kick", "Kick an online player.",
                    Target(), String("reason", "Reason for the kick.", requireReasons)),
                Subcommand("ban", "Permanently ban a player or Steam64 ID.", banOptions.ToArray()),
                Subcommand("tempban", "Temporarily ban a player or Steam64 ID.",
                    Target(), String("duration", "Duration such as 30m, 12h, 7d, or 2w.", true), String("reason", "Reason for the temporary ban.", requireReasons)),
                Subcommand("unban", "Remove a native Unturned ban by Steam64 ID.",
                    String("steamid", "Steam64 ID to unban.", true), String("reason", "Reason for the unban.", requireReasons)),
                Subcommand("mute", "Permanently mute a player or Steam64 ID.",
                    Target(), String("reason", "Reason for the permanent mute.", requireReasons)),
                Subcommand("tempmute", "Temporarily mute a player or Steam64 ID.",
                    Target(), String("duration", "Duration such as 30m, 12h, 7d, or 2w.", true), String("reason", "Reason for the temporary mute.", requireReasons)),
                Subcommand("unmute", "Remove an active uDiscord mute.",
                    Target(), String("reason", "Reason for the unmute.", requireReasons)),
                Subcommand("case", "View a moderation case by case number.",
                    Integer("case_id", "Moderation case number.", true, 1)),
                Subcommand("history", "View recent moderation history for a player or Steam64 ID.",
                    Target())
            };

            JObject command = new JObject
            {
                ["name"] = "udiscord",
                ["description"] = "Unturned chat bridge and moderation commands.",
                ["dm_permission"] = false,
                ["options"] = subcommands
            };
            return new JArray(command);
        }

        private static JObject Subcommand(string name, string description, params JObject[] options)
        {
            JObject value = new JObject
            {
                ["type"] = 1,
                ["name"] = name,
                ["description"] = description
            };
            if (options != null && options.Length > 0) value["options"] = new JArray(options);
            return value;
        }

        private static JObject Target()
        {
            JObject target = String("target", "Online player name or Steam64 ID.", true);
            target["autocomplete"] = true;
            return target;
        }

        private static JObject String(string name, string description, bool required)
        {
            return new JObject
            {
                ["type"] = 3,
                ["name"] = name,
                ["description"] = description,
                ["required"] = required
            };
        }

        private static JObject Boolean(string name, string description, bool required)
        {
            return new JObject
            {
                ["type"] = 5,
                ["name"] = name,
                ["description"] = description,
                ["required"] = required
            };
        }

        private static JObject Integer(string name, string description, bool required, long minimum)
        {
            return new JObject
            {
                ["type"] = 4,
                ["name"] = name,
                ["description"] = description,
                ["required"] = required,
                ["min_value"] = minimum
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UDiscord.Rocket.Discord
{
    public sealed class DiscordMessageEvent
    {
        public ulong MessageId { get; set; }
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong AuthorId { get; set; }
        public string AuthorName { get; set; }
        public string AuthorDisplayName { get; set; }
        public bool AuthorIsBot { get; set; }
        public bool IsWebhook { get; set; }
        public string Content { get; set; }
        public IReadOnlyList<DiscordAttachment> Attachments { get; set; }
    }

    public sealed class DiscordAttachment
    {
        public string FileName { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
    }

    public sealed class DiscordInteraction
    {
        public ulong Id { get; set; }
        public string Token { get; set; }
        public int Type { get; set; }
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong UserId { get; set; }
        public string UserName { get; set; }
        public string UserDisplayName { get; set; }
        public ulong MemberPermissions { get; set; }
        public IReadOnlyList<ulong> RoleIds { get; set; }
        public string CommandName { get; set; }
        public string SubcommandName { get; set; }
        public JObject Options { get; set; }
        public JObject Raw { get; set; }
    }

    public sealed class DiscordAutocompleteChoice
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public sealed class DiscordRestException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }

        public DiscordRestException(string message, int statusCode, string responseBody)
            : base(message)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }

    public static class DiscordInteractionParser
    {
        public static DiscordInteraction Parse(JObject payload)
        {
            if (payload == null) return null;
            JObject data = payload["data"] as JObject;
            JObject member = payload["member"] as JObject;
            JObject user = member?["user"] as JObject ?? payload["user"] as JObject;
            JObject commandOptions = new JObject();
            string subcommand = string.Empty;

            JArray options = data?["options"] as JArray;
            if (options != null && options.Count > 0)
            {
                JObject first = options[0] as JObject;
                if (first != null && (int?)first["type"] == 1)
                {
                    subcommand = (string)first["name"] ?? string.Empty;
                    FlattenOptions(first["options"] as JArray, commandOptions);
                }
                else
                {
                    FlattenOptions(options, commandOptions);
                }
            }

            List<ulong> roles = new List<ulong>();
            JArray roleArray = member?["roles"] as JArray;
            if (roleArray != null)
            {
                foreach (JToken role in roleArray)
                {
                    ulong roleId;
                    if (ulong.TryParse((string)role, out roleId)) roles.Add(roleId);
                }
            }

            ulong permissions = 0;
            ulong.TryParse((string)member?["permissions"], out permissions);
            string username = (string)user?["global_name"] ?? (string)user?["username"] ?? "unknown";
            string nickname = (string)member?["nick"];

            return new DiscordInteraction
            {
                Id = ParseSnowflake(payload["id"]),
                Token = (string)payload["token"] ?? string.Empty,
                Type = (int?)payload["type"] ?? 0,
                GuildId = ParseSnowflake(payload["guild_id"]),
                ChannelId = ParseSnowflake(payload["channel_id"]),
                UserId = ParseSnowflake(user?["id"]),
                UserName = (string)user?["username"] ?? username,
                UserDisplayName = string.IsNullOrWhiteSpace(nickname) ? username : nickname,
                MemberPermissions = permissions,
                RoleIds = roles,
                CommandName = (string)data?["name"] ?? string.Empty,
                SubcommandName = subcommand,
                Options = commandOptions,
                Raw = payload
            };
        }

        public static string GetString(DiscordInteraction interaction, string name)
        {
            if (interaction?.Options == null) return string.Empty;
            JToken value = interaction.Options[name];
            return value == null || value.Type == JTokenType.Null ? string.Empty : Convert.ToString(((JObject)value)["value"]);
        }

        public static bool GetBoolean(DiscordInteraction interaction, string name, bool defaultValue)
        {
            if (interaction?.Options == null) return defaultValue;
            JObject option = interaction.Options[name] as JObject;
            if (option == null) return defaultValue;
            bool value;
            return bool.TryParse(Convert.ToString(option["value"]), out value) ? value : defaultValue;
        }

        public static long GetInteger(DiscordInteraction interaction, string name, long defaultValue)
        {
            if (interaction?.Options == null) return defaultValue;
            JObject option = interaction.Options[name] as JObject;
            if (option == null) return defaultValue;
            long value;
            return long.TryParse(Convert.ToString(option["value"]), out value) ? value : defaultValue;
        }

        public static string GetFocusedOptionName(DiscordInteraction interaction)
        {
            if (interaction?.Options == null) return string.Empty;
            foreach (JProperty property in interaction.Options.Properties())
            {
                JObject option = property.Value as JObject;
                if ((bool?)option?["focused"] == true) return property.Name;
            }
            return string.Empty;
        }

        private static void FlattenOptions(JArray options, JObject result)
        {
            if (options == null) return;
            foreach (JObject option in options.OfType<JObject>())
            {
                string name = (string)option["name"];
                if (string.IsNullOrWhiteSpace(name)) continue;
                result[name] = option;
            }
        }

        private static ulong ParseSnowflake(JToken token)
        {
            ulong value;
            return ulong.TryParse((string)token, out value) ? value : 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UDiscord.Core.Security;
using UDiscord.Rocket.Infrastructure;

namespace UDiscord.Rocket.Discord
{
    public sealed class DiscordRestClient : IDisposable
    {
        private const string ApiBase = "https://discord.com/api/v10/";
        private readonly string _token;
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _requestConcurrency = new SemaphoreSlim(4, 4);
        private readonly object _globalRateLimitSync = new object();
        private DateTime _globalRateLimitUntilUtc = DateTime.MinValue;

        public DiscordRestClient(string token, TimeSpan timeout)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri(ApiBase),
                Timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout
            };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DiscordBot (https://github.com/ryanjokhu/udiscord, 1.0.0)");
        }

        public async Task<string> GetGatewayUrlAsync(CancellationToken cancellationToken)
        {
            JObject response = await SendJsonAsync(HttpMethod.Get, "gateway/bot", null, cancellationToken).ConfigureAwait(false);
            string url = (string)response?["url"];
            if (string.IsNullOrWhiteSpace(url)) throw new DiscordRestException("Discord did not return a gateway URL.", 0, response?.ToString(Formatting.None));
            return url;
        }

        public Task RegisterGuildCommandsAsync(ulong applicationId, ulong guildId, JArray commands, CancellationToken cancellationToken)
        {
            string route = "applications/" + applicationId + "/guilds/" + guildId + "/commands";
            return SendJsonAsync(HttpMethod.Put, route, commands, cancellationToken);
        }

        public Task SendChannelMessageAsync(ulong channelId, string content, CancellationToken cancellationToken)
        {
            JObject payload = new JObject
            {
                ["content"] = MessageSanitizer.Truncate(content ?? string.Empty, 2000),
                ["allowed_mentions"] = new JObject { ["parse"] = new JArray() }
            };
            return SendJsonAsync(HttpMethod.Post, "channels/" + channelId + "/messages", payload, cancellationToken);
        }

        public Task SendModerationEmbedAsync(ulong channelId, string title, string description, int color, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
        {
            JObject embed = new JObject
            {
                ["title"] = MessageSanitizer.Truncate(title ?? "Moderation action", 256),
                ["description"] = MessageSanitizer.Truncate(description ?? string.Empty, 4096),
                ["color"] = color,
                ["timestamp"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };

            JArray fieldArray = new JArray();
            if (fields != null)
            {
                foreach (KeyValuePair<string, string> field in fields)
                {
                    fieldArray.Add(new JObject
                    {
                        ["name"] = MessageSanitizer.Truncate(field.Key ?? "Field", 256),
                        ["value"] = MessageSanitizer.Truncate(field.Value ?? "-", 1024),
                        ["inline"] = false
                    });
                }
            }
            embed["fields"] = fieldArray;

            JObject payload = new JObject
            {
                ["embeds"] = new JArray(embed),
                ["allowed_mentions"] = new JObject { ["parse"] = new JArray() }
            };
            return SendJsonAsync(HttpMethod.Post, "channels/" + channelId + "/messages", payload, cancellationToken);
        }

        public Task RespondInteractionAsync(DiscordInteraction interaction, string content, bool ephemeral, CancellationToken cancellationToken)
        {
            JObject data = new JObject
            {
                ["content"] = MessageSanitizer.Truncate(content ?? string.Empty, 2000),
                ["allowed_mentions"] = new JObject { ["parse"] = new JArray() }
            };
            if (ephemeral) data["flags"] = 64;
            JObject payload = new JObject { ["type"] = 4, ["data"] = data };
            return SendInteractionCallbackAsync(interaction, payload, cancellationToken);
        }

        public Task DeferInteractionAsync(DiscordInteraction interaction, bool ephemeral, CancellationToken cancellationToken)
        {
            JObject data = new JObject();
            if (ephemeral) data["flags"] = 64;
            JObject payload = new JObject { ["type"] = 5, ["data"] = data };
            return SendInteractionCallbackAsync(interaction, payload, cancellationToken);
        }

        public Task RespondAutocompleteAsync(DiscordInteraction interaction, IEnumerable<DiscordAutocompleteChoice> choices, CancellationToken cancellationToken)
        {
            JArray array = new JArray();
            if (choices != null)
            {
                int count = 0;
                foreach (DiscordAutocompleteChoice choice in choices)
                {
                    if (choice == null || count >= 25) break;
                    array.Add(new JObject
                    {
                        ["name"] = MessageSanitizer.Truncate(choice.Name ?? choice.Value ?? string.Empty, 100),
                        ["value"] = MessageSanitizer.Truncate(choice.Value ?? string.Empty, 100)
                    });
                    count++;
                }
            }
            JObject payload = new JObject { ["type"] = 8, ["data"] = new JObject { ["choices"] = array } };
            return SendInteractionCallbackAsync(interaction, payload, cancellationToken);
        }

        public Task EditOriginalInteractionResponseAsync(ulong applicationId, string interactionToken, string content, CancellationToken cancellationToken)
        {
            JObject payload = new JObject
            {
                ["content"] = MessageSanitizer.Truncate(content ?? string.Empty, 2000),
                ["allowed_mentions"] = new JObject { ["parse"] = new JArray() }
            };
            string route = "webhooks/" + applicationId + "/" + interactionToken + "/messages/@original";
            return SendJsonAsync(new HttpMethod("PATCH"), route, payload, cancellationToken, false);
        }

        private Task SendInteractionCallbackAsync(DiscordInteraction interaction, JObject payload, CancellationToken cancellationToken)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            string route = "interactions/" + interaction.Id + "/" + interaction.Token + "/callback";
            return SendJsonAsync(HttpMethod.Post, route, payload, cancellationToken, false);
        }

        private async Task<JObject> SendJsonAsync(HttpMethod method, string route, JToken payload, CancellationToken cancellationToken, bool useAuthorization = true)
        {
            JToken result = await SendJsonTokenAsync(method, route, payload, cancellationToken, useAuthorization).ConfigureAwait(false);
            return result as JObject;
        }

        private async Task<JToken> SendJsonTokenAsync(HttpMethod method, string route, JToken payload, CancellationToken cancellationToken, bool useAuthorization)
        {
            await _requestConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    await WaitForGlobalRateLimitAsync(cancellationToken).ConfigureAwait(false);
                    using (HttpRequestMessage request = new HttpRequestMessage(method, route))
                    {
                        if (useAuthorization) request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _token);
                        if (payload != null)
                        {
                            request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                        }

                        using (HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                        {
                            string body = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (response.IsSuccessStatusCode)
                            {
                                if (string.IsNullOrWhiteSpace(body)) return null;
                                return JToken.Parse(body);
                            }

                            int status = (int)response.StatusCode;
                            if (status == 429)
                            {
                                TimeSpan retry = ParseRetryAfter(body);
                                bool global = ParseGlobalRateLimit(body);
                                if (global)
                                {
                                    lock (_globalRateLimitSync) _globalRateLimitUntilUtc = DateTime.UtcNow + retry;
                                }
                                await Task.Delay(retry, cancellationToken).ConfigureAwait(false);
                                continue;
                            }

                            if (status >= 500 && attempt < 3)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                                continue;
                            }

                            string safeBody = MessageSanitizer.Truncate(body, 1000);
                            throw new DiscordRestException("Discord REST request failed with HTTP " + status + ".", status, safeBody);
                        }
                    }
                }

                throw new DiscordRestException("Discord REST request exceeded retry limit.", 0, string.Empty);
            }
            finally
            {
                _requestConcurrency.Release();
            }
        }

        private async Task WaitForGlobalRateLimitAsync(CancellationToken cancellationToken)
        {
            TimeSpan delay;
            lock (_globalRateLimitSync)
            {
                delay = _globalRateLimitUntilUtc - DateTime.UtcNow;
            }
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        private static TimeSpan ParseRetryAfter(string body)
        {
            try
            {
                JObject json = JObject.Parse(body);
                double seconds = (double?)json["retry_after"] ?? 1;
                return TimeSpan.FromMilliseconds(Math.Max(250, Math.Min(60000, seconds * 1000)));
            }
            catch
            {
                return TimeSpan.FromSeconds(1);
            }
        }

        private static bool ParseGlobalRateLimit(string body)
        {
            try { return (bool?)JObject.Parse(body)["global"] == true; }
            catch { return false; }
        }

        public void Dispose()
        {
            _client.Dispose();
            _requestConcurrency.Dispose();
        }
    }
}

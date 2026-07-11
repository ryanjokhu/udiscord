using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UDiscord.Core.Models;
using UDiscord.Rocket.Infrastructure;

namespace UDiscord.Rocket.Discord
{
    public sealed class DiscordGatewayClient : IDisposable
    {
        private sealed class FatalGatewayException : Exception
        {
            public FatalGatewayException(string message) : base(message) { }
        }

        private readonly DiscordRestClient _rest;
        private readonly string _token;
        private readonly int _intents;
        private readonly int _maximumPayloadBytes;
        private readonly int _reconnectMinimumSeconds;
        private readonly int _reconnectMaximumSeconds;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly object _socketSync = new object();
        private readonly Random _random = new Random();

        private CancellationTokenSource _lifetime;
        private Task _runTask;
        private ClientWebSocket _socket;
        private string _gatewayUrl;
        private string _resumeGatewayUrl;
        private string _sessionId;
        private long? _sequence;
        private volatile bool _heartbeatAcknowledged = true;
        private volatile bool _ready;
        private int _heartbeatIntervalMs;
        private DateTime _lastIdentifyUtc = DateTime.MinValue;
        private BotConnectionState _state = BotConnectionState.Stopped;

        public event Func<string, JObject, Task> DispatchReceived;
        public event Action<BotConnectionState, string> StateChanged;

        public ulong ApplicationId { get; private set; }
        public BotConnectionState State => _state;
        public bool IsReady => _ready;

        public DiscordGatewayClient(
            DiscordRestClient rest,
            string token,
            int intents,
            int maximumPayloadBytes,
            int reconnectMinimumSeconds,
            int reconnectMaximumSeconds)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _intents = intents;
            _maximumPayloadBytes = maximumPayloadBytes;
            _reconnectMinimumSeconds = Math.Max(1, reconnectMinimumSeconds);
            _reconnectMaximumSeconds = Math.Max(_reconnectMinimumSeconds, reconnectMaximumSeconds);
        }

        public void Start(CancellationToken parentToken)
        {
            if (_runTask != null) return;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            SetState(BotConnectionState.Starting, "Starting embedded Discord gateway client.");
            _runTask = Task.Run(() => RunAsync(_lifetime.Token), _lifetime.Token);
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            SetState(BotConnectionState.Stopping, "Stopping Discord gateway client.");
            _lifetime?.Cancel();
            ClientWebSocket socket;
            lock (_socketSync) socket = _socket;
            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        using (CancellationTokenSource closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", closeTimeout.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch
                {
                    try { socket.Abort(); } catch { }
                }
            }

            if (_runTask != null)
            {
                Task completed = await Task.WhenAny(_runTask, Task.Delay(timeout)).ConfigureAwait(false);
                if (completed != _runTask)
                {
                    PluginLog.Warn("Discord gateway did not stop within the configured timeout; aborting the socket.");
                    try { socket?.Abort(); } catch { }
                    completed = await Task.WhenAny(_runTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                    if (completed != _runTask)
                    {
                        PluginLog.Warn("Discord gateway is still stopping after abort; shutdown will continue without an unbounded wait.");
                    }
                }

                if (_runTask.IsCompleted)
                {
                    try { await _runTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception exception) { PluginLog.Exception(exception, "Discord gateway stopped with an error."); }
                }
            }

            _ready = false;
            SetState(BotConnectionState.Stopped, "Discord gateway stopped.");
        }

        public void RequestReconnect()
        {
            ClientWebSocket socket;
            lock (_socketSync) socket = _socket;
            try { socket?.Abort(); } catch { }
        }

        public Task UpdatePresenceAsync(string activity, CancellationToken cancellationToken)
        {
            if (!_ready) return Task.CompletedTask;
            JObject payload = new JObject
            {
                ["op"] = 3,
                ["d"] = new JObject
                {
                    ["since"] = null,
                    ["activities"] = new JArray(new JObject
                    {
                        ["name"] = string.IsNullOrWhiteSpace(activity) ? "Unturned" : activity,
                        ["type"] = 3
                    }),
                    ["status"] = "online",
                    ["afk"] = false
                }
            };
            return SendPayloadAsync(payload, cancellationToken);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            int failures = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(_gatewayUrl))
                        {
                            _gatewayUrl = await _rest.GetGatewayUrlAsync(cancellationToken).ConfigureAwait(false);
                        }

                        SetState(failures == 0 ? BotConnectionState.Connecting : BotConnectionState.Reconnecting,
                            failures == 0 ? "Connecting to Discord." : "Reconnecting to Discord.");

                        bool becameReady = await RunConnectionAsync(cancellationToken).ConfigureAwait(false);
                        if (becameReady) failures = 0;
                        else failures++;
                    }
                    catch (FatalGatewayException fatal)
                    {
                        _ready = false;
                        SetState(BotConnectionState.Degraded, fatal.Message);
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        failures++;
                        _ready = false;
                        SetState(BotConnectionState.Reconnecting, "Discord connection failed; retrying.");
                        PluginLog.Exception(exception, "Discord gateway connection failed.");
                    }

                    if (cancellationToken.IsCancellationRequested) break;
                    int exponent = Math.Min(6, Math.Max(0, failures - 1));
                    int seconds = Math.Min(_reconnectMaximumSeconds, _reconnectMinimumSeconds * (1 << exponent));
                    int jitter = NextRandom(0, Math.Max(1, Math.Min(5, seconds)));
                    await Task.Delay(TimeSpan.FromSeconds(seconds + jitter), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _ready = false;
                DisposeCurrentSocket();
            }
        }

        private async Task<bool> RunConnectionAsync(CancellationToken cancellationToken)
        {
            ClientWebSocket socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.SetRequestHeader("User-Agent", "uDiscord/1.0.0");
            lock (_socketSync)
            {
                DisposeCurrentSocketLocked();
                _socket = socket;
            }

            string baseUrl = !string.IsNullOrWhiteSpace(_resumeGatewayUrl) && !string.IsNullOrWhiteSpace(_sessionId)
                ? _resumeGatewayUrl
                : _gatewayUrl;
            Uri uri = new Uri(baseUrl.TrimEnd('/') + "/?v=10&encoding=json");
            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            JObject hello = await ReceivePayloadAsync(socket, cancellationToken).ConfigureAwait(false);
            if ((int?)hello?["op"] != 10) throw new InvalidDataException("Discord gateway did not begin with Hello opcode 10.");
            _heartbeatIntervalMs = (int?)hello["d"]?["heartbeat_interval"] ?? 0;
            if (_heartbeatIntervalMs < 1000) throw new InvalidDataException("Discord returned an invalid heartbeat interval.");

            using (CancellationTokenSource connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                _heartbeatAcknowledged = true;
                Task heartbeatTask = Task.Run(() => HeartbeatLoopAsync(socket, connectionCancellation.Token), connectionCancellation.Token);
                if (!string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
                    await SendResumeAsync(connectionCancellation.Token).ConfigureAwait(false);
                else
                    await SendIdentifyAsync(connectionCancellation.Token).ConfigureAwait(false);

                bool becameReady = false;
                try
                {
                    while (!connectionCancellation.Token.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        JObject payload = await ReceivePayloadAsync(socket, connectionCancellation.Token).ConfigureAwait(false);
                        if (payload == null) break;

                        long? sequence = (long?)payload["s"];
                        if (sequence.HasValue) _sequence = sequence;
                        int opcode = (int?)payload["op"] ?? -1;
                        switch (opcode)
                        {
                            case 0:
                                string eventName = (string)payload["t"] ?? string.Empty;
                                JObject data = payload["d"] as JObject ?? new JObject();
                                if (string.Equals(eventName, "READY", StringComparison.Ordinal))
                                {
                                    _sessionId = (string)data["session_id"];
                                    _resumeGatewayUrl = (string)data["resume_gateway_url"] ?? _gatewayUrl;
                                    ulong applicationId;
                                    if (ulong.TryParse((string)data["application"]?["id"], out applicationId)) ApplicationId = applicationId;
                                    _ready = true;
                                    becameReady = true;
                                    SetState(BotConnectionState.Online, "Discord bot is online.");
                                }
                                else if (string.Equals(eventName, "RESUMED", StringComparison.Ordinal))
                                {
                                    _ready = true;
                                    becameReady = true;
                                    SetState(BotConnectionState.Online, "Discord session resumed.");
                                }

                                Func<string, JObject, Task> handler = DispatchReceived;
                                if (handler != null)
                                {
                                    try { await handler(eventName, data).ConfigureAwait(false); }
                                    catch (Exception exception) { PluginLog.Exception(exception, "Discord dispatch handler failed for " + eventName + "."); }
                                }
                                break;

                            case 1:
                                await SendHeartbeatAsync(connectionCancellation.Token).ConfigureAwait(false);
                                break;

                            case 7:
                                _ready = false;
                                SetState(BotConnectionState.Reconnecting, "Discord requested a reconnect.");
                                return becameReady;

                            case 9:
                                bool resumable = (bool?)payload["d"] == true;
                                if (!resumable) ClearSession();
                                _ready = false;
                                SetState(BotConnectionState.Reconnecting, resumable
                                    ? "Discord invalidated the current connection; attempting session resume."
                                    : "Discord invalidated the session; starting a new session.");
                                await Task.Delay(TimeSpan.FromSeconds(NextRandom(1, 6)), connectionCancellation.Token).ConfigureAwait(false);
                                return becameReady;

                            case 10:
                                _heartbeatIntervalMs = (int?)payload["d"]?["heartbeat_interval"] ?? _heartbeatIntervalMs;
                                break;

                            case 11:
                                _heartbeatAcknowledged = true;
                                break;
                        }
                    }
                }
                catch (WebSocketException exception)
                {
                    PluginLog.Debug("Discord WebSocket closed: " + exception.Message);
                }
                finally
                {
                    connectionCancellation.Cancel();
                    try { await heartbeatTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception exception) { PluginLog.Debug("Heartbeat worker stopped: " + exception.Message); }
                }
            }

            if (socket.CloseStatus.HasValue)
            {
                int code = (int)socket.CloseStatus.Value;
                if (code == 4007 || code == 4009) ClearSession();
                if (code == 4004) throw new FatalGatewayException("Discord rejected the bot token (gateway close code 4004).");
                if (code == 4010) throw new FatalGatewayException("Discord rejected the shard configuration (gateway close code 4010).");
                if (code == 4011) throw new FatalGatewayException("Discord requires sharding for this bot (gateway close code 4011).");
                if (code == 4012) throw new FatalGatewayException("Discord rejected the Gateway API version (gateway close code 4012).");
                if (code == 4013) throw new FatalGatewayException("Discord rejected the configured gateway intents (close code 4013).");
                if (code == 4014) throw new FatalGatewayException("A privileged Discord gateway intent is not enabled in the Developer Portal (close code 4014).");
            }

            _ready = false;
            return false;
        }

        private async Task HeartbeatLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            int jitter = NextRandom(0, _heartbeatIntervalMs);
            await Task.Delay(jitter, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                if (!_heartbeatAcknowledged)
                {
                    PluginLog.Warn("Discord heartbeat was not acknowledged; reconnecting.");
                    try { socket.Abort(); } catch { }
                    return;
                }

                _heartbeatAcknowledged = false;
                await SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_heartbeatIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SendIdentifyAsync(CancellationToken cancellationToken)
        {
            TimeSpan wait = _lastIdentifyUtc + TimeSpan.FromSeconds(5) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

            JObject payload = new JObject
            {
                ["op"] = 2,
                ["d"] = new JObject
                {
                    ["token"] = _token,
                    ["intents"] = _intents,
                    ["properties"] = new JObject
                    {
                        ["os"] = Environment.OSVersion.Platform.ToString(),
                        ["browser"] = "udiscord",
                        ["device"] = "udiscord"
                    },
                    ["compress"] = false,
                    ["large_threshold"] = 50
                }
            };
            await SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
            _lastIdentifyUtc = DateTime.UtcNow;
        }

        private Task SendResumeAsync(CancellationToken cancellationToken)
        {
            JObject payload = new JObject
            {
                ["op"] = 6,
                ["d"] = new JObject
                {
                    ["token"] = _token,
                    ["session_id"] = _sessionId,
                    ["seq"] = _sequence
                }
            };
            return SendPayloadAsync(payload, cancellationToken);
        }

        private Task SendHeartbeatAsync(CancellationToken cancellationToken)
        {
            return SendPayloadAsync(new JObject { ["op"] = 1, ["d"] = _sequence.HasValue ? JToken.FromObject(_sequence.Value) : JValue.CreateNull() }, cancellationToken);
        }

        private async Task SendPayloadAsync(JObject payload, CancellationToken cancellationToken)
        {
            ClientWebSocket socket;
            lock (_socketSync) socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) throw new InvalidOperationException("Discord gateway is not connected.");

            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<JObject> ReceivePayloadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[16384];
            using (MemoryStream stream = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", cancellationToken).ConfigureAwait(false); } catch { }
                        return null;
                    }

                    if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Discord sent an unsupported binary gateway payload.");
                    if (result.Count > 0)
                    {
                        stream.Write(buffer, 0, result.Count);
                        if (stream.Length > _maximumPayloadBytes) throw new InvalidDataException("Discord gateway payload exceeded configured size limit.");
                    }

                    if (result.EndOfMessage) break;
                }

                string json = Encoding.UTF8.GetString(stream.ToArray());
                return JObject.Parse(json);
            }
        }

        private int NextRandom(int minimumInclusive, int maximumExclusive)
        {
            lock (_random) return _random.Next(minimumInclusive, maximumExclusive);
        }

        private void ClearSession()
        {
            _sessionId = null;
            _resumeGatewayUrl = null;
            _sequence = null;
        }

        private void SetState(BotConnectionState state, string reason)
        {
            _state = state;
            try { StateChanged?.Invoke(state, reason); }
            catch (Exception exception) { PluginLog.Exception(exception, "Discord state listener failed."); }
        }

        private void DisposeCurrentSocket()
        {
            lock (_socketSync) DisposeCurrentSocketLocked();
        }

        private void DisposeCurrentSocketLocked()
        {
            if (_socket == null) return;
            try { _socket.Dispose(); } catch { }
            _socket = null;
        }

        public void Dispose()
        {
            _lifetime?.Cancel();
            DisposeCurrentSocket();
            _lifetime?.Dispose();
            _sendLock.Dispose();
        }
    }
}

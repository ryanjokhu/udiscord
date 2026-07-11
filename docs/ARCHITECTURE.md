# Architecture

## Runtime topology

```text
Discord Gateway / REST
        │
        ▼
DiscordGatewayClient + DiscordRestClient
        │
        ├── bounded outbound chat worker
        ├── slash-command parser and permission policy
        └── reconnect / heartbeat / resume state machine
        │
        ▼
MainThreadDispatcher
        │
        ▼
Unturned source owners
  ChatManager / Provider / PlayerTool
        │
        ▼
PersistenceWorker
  mutes.json / cases.ndjson / state.json
```

The Discord client is a library component inside `uDiscord.dll`. It is not a child process and does not expose an inbound port.

## Ownership boundaries

- `ChatManager` owns Unturned chat routing. uDiscord uses `onChatted` only for mute cancellation, and `onServerFormattingMessage` as the outbound game-to-Discord relay boundary after command/listing visibility checks have passed.
- `Provider` owns native kicks and native ban/unban requests.
- `PlayerTool` and `Provider.clients` own online-player resolution.
- uDiscord owns Discord connectivity, Discord permissions, mute state, and moderation case records.
- RocketMod supplies lifecycle, configuration, commands, and permissions integration; it does not own gameplay state.

## Main-thread boundary

Gateway receive loops, REST requests, persistence writes, heartbeat work, and reconnect delays execute away from the Unity thread. Anything reading or mutating `Provider.clients`, `SteamPlayer`, `ChatManager`, or native moderation state goes through `MainThreadDispatcher`.

The dispatcher captures only immutable command arguments across the async boundary. Player objects are resolved again on the game thread.

## Lifecycle

Load order:

1. Read and validate configuration.
2. Initialize redacted logging and TLS policy.
3. Create bounded persistence and game-thread services.
4. Load mutes and moderation cases.
5. Construct player/moderation services.
6. Construct the embedded Discord REST/Gateway clients.
7. Attach and subscribe the game bridge once.
8. Start workers and the Gateway connection.
9. Emit one startup summary.

Unload order:

1. Reject reload/new runtime work.
2. Unsubscribe chat and player hooks.
3. Stop game-thread dispatch acceptance.
4. Stop the Gateway so no new Discord commands arrive.
5. Drain bounded outbound and persistence queues.
6. Flush mute state.
7. Cancel/dispose remaining resources.
8. Clear the static plugin instance.

## Discord protocol surface

The Gateway client implements only the required Discord operations:

- Hello (10)
- Identify (2)
- Resume (6)
- Dispatch (0)
- Heartbeat (1)
- Heartbeat ACK (11)
- Reconnect (7)
- Invalid Session (9)
- Presence Update (3)

REST is used for command registration, chat messages, interaction responses, and moderation log embeds.

## Persistence

`mutes.json` is atomically replaced and backed up. `cases.ndjson` is append-only so one malformed historical record does not invalidate the whole journal. `state.json` preserves the next case number.

Persistence is not authoritative for native bans. Native bans remain owned by Unturned. uDiscord persists only its own mute and audit state.

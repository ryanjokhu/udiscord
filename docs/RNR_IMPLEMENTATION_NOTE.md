# R&R implementation note

## Selected risk routes

- chat bridge and chat moderation: `ChatManager`, player identity, plugin chat handler
- administrative actions: `Provider`, `PlayerTool`, native command behavior
- persistence: plugin-owned mute snapshot and case journal
- high-pop release: chat hot path, bounded network/file queues, reload cleanup

## Source owners

- `ChatManager.onChatted` is used only for authoritative mute cancellation before broadcast.
- `ChatManager.onServerFormattingMessage` is used for game-to-Discord relay after command processing and `isShown` checks have succeeded, so hidden commands and canceled chat are not relayed.
- `Provider.kick` owns kicks.
- `Provider.requestBanPlayer` owns permanent and temporary bans.
- `Provider.requestUnbanPlayer` owns unban.
- `Provider.clients`, `PlayerTool`, `SteamPlayer`, and `SteamPlayerID` own current session/player identity.
- uDiscord owns only Discord connection state, role policy, mute records, and case records.

## Transaction order

Discord moderation:

```text
interaction -> guild/channel/role/rate validation -> defer response
-> capture immutable target/reason/operation ID -> main-thread dispatch
-> resolve/revalidate player or Steam64 -> native/plugin mutation
-> record final result -> persist case -> post log -> edit response
```

Mute enforcement:

```text
server receives chat -> resolve Steam64 -> active mute lookup
-> cancel visibility before broadcast -> bounded reminder -> no Discord relay
```

## Failure and recovery

- Discord unavailable: game continues; reconnect uses backoff and session resume.
- invalid configuration: affected runtime is not enabled; diagnostics remain available.
- persistence queue full: in-memory mute remains active and a durability-risk error is logged.
- reload: chat hooks are removed before the old bot is stopped; a new bot and bridge are attached once.
- shutdown: new hooks are removed, networking stops, queues are bounded, mute state is flushed.
- duplicate moderation request: each accepted request has a unique operation ID and separate case; per-user rate limits reduce accidental repetition.

## High-pop controls

- no HTTP or file access in `onChatted` or `onServerFormattingMessage`
- O(1) mute lookup by Steam64
- bounded outbound and persistence queues
- message sanitization and truncation before enqueue
- one coalesced presence update after join/leave bursts
- main-thread actions have a timeout
- denied/moderation actions are auditable without logging bot secrets

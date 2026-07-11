# Changelog

All notable changes to uDiscord are documented here.

## 1.2.0 - 2026-07-11

- Added independently configurable Discord outputs for global, local, and group chat.
- Added per-output `Enabled`, `ChannelId`, and formatting options.
- Added independent routes for joins, leaves, server online/offline notices, test messages, command logs, and moderation logs.
- Moved Discord-to-game input to the explicit `Discord.DiscordToGameChannelId` setting.
- Kept v1.x configuration compatibility through in-memory legacy migration.
- Kept local and group relays disabled during migration to avoid accidental private-chat disclosure.
- Preserved the Rocket player-chat event relay path and configurable command logging.

## 1.1.1 - 2026-07-11

- Moved game-to-Discord player chat relay onto `UnturnedPlayerEvents.OnPlayerChatted` for Rocket chat-stack compatibility.
- Added relay diagnostics without interrupting game chat.

## 1.1.0 - 2026-07-11

- Added configurable Rocket command logging with ignored and redacted command lists.

## 1.0.1 - 2026-07-11

- Fixed a null-reference exception in the Unturned chat pipeline.
- Gated the internal mute interception hook to internal-mute mode only.
- Added fail-open chat protection.

## 1.0.0 - 2026-07-11

- Added the embedded Discord bot, two-way chat, slash-command moderation, persistence, permissions, reconnect handling, and release tooling.

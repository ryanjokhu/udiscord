# uDiscord

uDiscord is a customer-ready RocketMod plugin for Unturned that embeds a Discord bot directly inside the game-server process.

Server owners create their own Discord application and bot token, install the plugin, configure the token, guild, channel, and role IDs, and start the Unturned server. No separate bot executable, Node.js process, container, web panel, inbound HTTP endpoint, or external database is required.

## Final feature set

- Two-way Discord ↔ Unturned global chat relay
- Embedded Discord Gateway client with heartbeat, reconnect, and session resume
- Guild-scoped `/udiscord` slash commands
- Player list, server status, and staff announcements
- Kick, permanent ban, temporary ban, unban
- Permanent mute, temporary mute, unmute
- Persistent moderation cases and mute state
- Discord role-based authorization
- Bounded queues, rate limits, safe message sanitization, and clean unload behavior
- Windows and Linux/Pterodactyl packaging

## Engineering standard

Implementation follows the source-owner, lifecycle, main-thread, persistence, and high-pop safety rules documented in the private R&R Unturned Bible and is verified against the official SmartlyDressedGames U3-SDK source tree.

## Repository state

The full v1 implementation is being built on an implementation branch. `main` remains the release branch.

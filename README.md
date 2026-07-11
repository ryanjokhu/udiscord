# uDiscord

uDiscord is a RocketMod plugin for Unturned that runs a Discord bot **inside the Unturned dedicated-server process**.

A server owner creates a Discord application and bot token, installs `uDiscord.dll` plus `uDiscord.Core.dll`, configures the Discord IDs, and starts the Unturned server. There is no separate Node.js service, bot executable, Docker container, web panel, public HTTP listener, or required database.

## Features

- Two-way Discord ↔ Unturned global chat
- Embedded Discord Gateway v10 client with heartbeat, reconnect, and session resume
- Guild-scoped `/udiscord` slash commands registered automatically
- Server status, online-player list, and staff announcements
- Native Unturned kick, permanent ban, temporary ban, and unban
- Persistent permanent/temporary mute and unmute
- Append-only moderation case journal
- Discord role-based viewer, moderator, and administrator tiers
- Player autocomplete using durable Steam64 values
- Input sanitization, mention suppression, bounded queues, and rate limits
- Deterministic hook cleanup and bounded shutdown
- Windows and Linux/Pterodactyl support target

## Discord commands

| Tier | Commands |
|---|---|
| Viewer | `/udiscord status`, `players`, `case`, `history`, `help` |
| Moderator | `/udiscord kick`, `mute`, `tempmute`, `unmute` |
| Administrator | `/udiscord ban`, `tempban`, `unban`, `say` |

In game, Rocket administrators can use:

```text
/udiscord status
/udiscord reload
/udiscord reconnect
/udiscord test
```

Rocket permission: `udiscord.admin`.

## Installation

See [Installation](docs/INSTALLATION.md). The essential flow is:

1. Build or download the release ZIP.
2. Extract it into the target server's `Rocket` directory.
3. Start once to generate `uDiscord.configuration.xml`.
4. Create a Discord application and bot.
5. Enable **Message Content Intent**.
6. Invite the bot with `bot` and `applications.commands` scopes.
7. Configure the token, guild ID, channel IDs, and role IDs.
8. Restart the Unturned server.

## Build

Runtime assemblies are intentionally not committed. Stage them from a current server installation:

```powershell
./scripts/restore-references.ps1 -Source "C:\path\to\UnturnedServer"
./scripts/build.ps1
./scripts/package.ps1
```

See [Development](docs/DEVELOPMENT.md).

## Design and safety

The implementation follows the R&R Unturned Bible's source-owner model:

```text
source owner -> server validation -> hook/event -> authoritative mutation
-> replication -> plugin persistence after result -> cleanup/recovery
```

Network and file work never runs synchronously in the chat hook. Discord-originated game actions are marshalled to the Unturned main thread and revalidated there. Chat input and Discord interaction arguments are treated as untrusted.

See [Architecture](docs/ARCHITECTURE.md), [Security](docs/SECURITY.md), and the [R&R implementation note](docs/RNR_IMPLEMENTATION_NOTE.md).

## Current release gate

The source implementation is intended to be complete before live testing. A release is not considered production-verified until it has been compiled against current server assemblies and exercised on a real Windows and Linux/Pterodactyl Unturned server.

## License

MIT. See [LICENSE](LICENSE).

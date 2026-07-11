# Installation

## Requirements

- Current Unturned dedicated server
- RocketMod / Rocket.Unturned
- A Discord server where you can create and invite a bot
- Outbound HTTPS and secure WebSocket access to Discord

No inbound port, domain, database, Node.js process, or separate bot host is required.

## Install files

Extract the release ZIP into the target server's `Rocket` directory so it contains:

```text
Rocket/
├── Plugins/
│   └── uDiscord/
│       └── uDiscord.dll
└── Libraries/
    └── uDiscord.Core.dll
```

Start the server once. Rocket generates:

```text
Rocket/Plugins/uDiscord/uDiscord.configuration.xml
Rocket/Plugins/uDiscord/uDiscord.en.translation.xml
```

## Create the Discord bot

1. Open the Discord Developer Portal.
2. Create a new application.
3. Open **Bot** and create/reset the bot token.
4. Enable **Message Content Intent**.
5. Under OAuth2 URL Generator, select `bot` and `applications.commands`.
6. Grant the bot at least:
   - View Channels
   - Send Messages
   - Embed Links
   - Read Message History
7. Invite it to the Discord server.
8. Enable Discord Developer Mode and copy the guild, channel, and role IDs.

Do not grant Discord Administrator unless your own server policy requires it. uDiscord authorizes moderation through configured role IDs, not channel access alone.

## Configure

Set:

- `Discord.BotToken`, or preferably environment variable `UDISCORD_BOT_TOKEN`
- `Discord.GuildId`
- `Discord.ChatChannelId`
- `Discord.ModerationLogChannelId`
- at least one role under `Permissions`

Restart the server or run `/udiscord reload` after editing a valid configuration.

## Verify

1. Run `/udiscord status` in game or console.
2. Run `/udiscord test`.
3. Confirm the bot is online and the test appears in Discord.
4. Send a Discord message in the configured chat channel.
5. Send an in-game global message.
6. Run `/udiscord status` in Discord.
7. Test moderation on a controlled account before production use.

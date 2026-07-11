# Configuration

The Rocket-generated XML is strongly validated before hooks and networking are enabled.

## Token precedence

`Discord.BotTokenEnvironmentVariable` is checked first. Its default is `UDISCORD_BOT_TOKEN`. If the variable is empty, `Discord.BotToken` is used.

The token is redacted from plugin log messages. Never commit a live token. Reset it immediately if exposed.

## Discord section

- `GuildId`: the one Discord guild this plugin accepts.
- `ChatChannelId`: two-way chat channel.
- `ModerationLogChannelId`: audit embed channel.
- `CommandChannelIds`: optional allowlist; empty means commands can be used in any guild channel where the bot is present.
- `GatewayIntents`: defaults to Guilds + Guild Messages + Message Content (`33281`).
- `RegisterGuildCommandsOnStartup`: replaces the guild-scoped command schema when the bot becomes ready.
- reconnect and REST timeout values are bounded by validation.

## Permissions

Role tiers are cumulative. The highest configured matching tier wins:

```xml
<Permissions>
  <ViewerRoleIds>
    <RoleId>111111111111111111</RoleId>
  </ViewerRoleIds>
  <ModeratorRoleIds>
    <RoleId>222222222222222222</RoleId>
  </ModeratorRoleIds>
  <AdministratorRoleIds>
    <RoleId>333333333333333333</RoleId>
  </AdministratorRoleIds>
</Permissions>
```

`AllowDiscordAdministratorBypass` is false by default. Discord's Administrator permission does not bypass uDiscord roles unless explicitly enabled.

## Chat relay

Global chat is the only game route relayed by default. Commands beginning with `/` or `@`, local chat, and group chat are not forwarded.

Formatting placeholders:

- Discord → game: `{author}`, `{message}`
- Game → Discord: `{player}`, `{steamid}`, `{message}`
- join/leave: `{player}`, `{steamid}`
- activity: `{players}`, `{maxplayers}`, `{server}`

Attachment URLs are disabled by default. Attachment file names can still be shown.

## Moderation

Reasons are required and length-bounded by default. Temporary durations accept combined units such as `1d12h`; supported units are `s`, `m`, `h`, `d`, and `w`.

Native Unturned owns kick, ban, temporary ban, and unban. uDiscord owns mute, temporary mute, and unmute.

## Persistence

Data files remain inside the plugin directory. Path traversal and rooted data-directory values are rejected. File writes are queued and bounded; mute snapshots use temporary-file replacement and backups.

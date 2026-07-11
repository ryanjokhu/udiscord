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

Native Unturned owns kick, ban, temporary ban, and unban.

### Mute backend

Mute, temporary mute, and unmute use a configurable backend. The default is `Command`, which delegates to moderation commands that are already registered on the Rocket server.

```xml
<MuteBackend>
  <Mode>Command</Mode>
  <MuteCommand>mute {steamid} {reason}</MuteCommand>
  <TemporaryMuteCommand>tempmute {steamid} {duration} {reason}</TemporaryMuteCommand>
  <UnmuteCommand>unmute {steamid}</UnmuteCommand>
  <AllowOfflineTargets>true</AllowOfflineTargets>
</MuteBackend>
```

Change the templates to match the moderation plugin installed on the server. Do not include the leading `/`; uDiscord removes it if present.

Supported placeholders:

- `{steamid}`: resolved Steam64 ID; recommended for durable targeting.
- `{player}`: sanitized current display name, or Steam64 for an offline target.
- `{duration}`: normalized duration such as `1h` or `1d12h`.
- `{reason}`: sanitized moderation reason.

Examples for different command syntaxes:

```xml
<MuteCommand>mute {steamid} {reason}</MuteCommand>
<TemporaryMuteCommand>mute {steamid} {duration} {reason}</TemporaryMuteCommand>
<UnmuteCommand>unmute {steamid}</UnmuteCommand>
```

or:

```xml
<MuteCommand>pmute {player} {reason}</MuteCommand>
<TemporaryMuteCommand>tempmute {player} {reason} {duration}</TemporaryMuteCommand>
<UnmuteCommand>punmute {player}</UnmuteCommand>
```

uDiscord resolves and validates the target, renders only the configured template, and dispatches it as the Rocket console caller. Discord users cannot submit arbitrary server commands. The resulting moderation plugin remains the source of truth for mute storage, chat blocking, expiry, and in-game unmute behavior.

A successful dispatch means Rocket accepted the command invocation. Some third-party commands do not expose a structured success result, so the moderation log describes command-backed mute actions as dispatched rather than claiming that an external plugin persisted the action.

Servers without an existing mute plugin can opt into uDiscord's built-in fallback:

```xml
<MuteBackend>
  <Mode>Internal</Mode>
  <MuteCommand>mute {steamid} {reason}</MuteCommand>
  <TemporaryMuteCommand>tempmute {steamid} {duration} {reason}</TemporaryMuteCommand>
  <UnmuteCommand>unmute {steamid}</UnmuteCommand>
  <AllowOfflineTargets>true</AllowOfflineTargets>
</MuteBackend>
```

In `Internal` mode, uDiscord stores mute records itself and blocks muted players through the authoritative Unturned chat hook. Do not use Internal mode alongside another independent mute system for the same players.

## Persistence

Data files remain inside the plugin directory. Path traversal and rooted data-directory values are rejected. File writes are queued and bounded; internal-mode mute snapshots use temporary-file replacement and backups. Moderation case records are retained in either mute mode.

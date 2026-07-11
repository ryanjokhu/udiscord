# Configuration

The Rocket-generated XML is validated before hooks and networking are enabled. Every Discord-bound output has its own `Enabled` switch and `ChannelId` under `Outputs`.

## Token precedence

`Discord.BotTokenEnvironmentVariable` is checked first. Its default is `UDISCORD_BOT_TOKEN`. If the variable is empty, `Discord.BotToken` is used.

The token is redacted from plugin log messages. Never commit a live token. Reset it immediately if exposed.

## Discord section

- `GuildId`: the one Discord guild this plugin accepts.
- `DiscordToGameChannelId`: messages written in this Discord channel are relayed into Unturned when `ChatRelay.DiscordToGameEnabled` is true.
- `CommandChannelIds`: optional allowlist for Discord slash commands; empty means commands can be used in any guild channel where the bot is present.
- `GatewayIntents`: defaults to Guilds + Guild Messages + Message Content (`33281`).
- `RegisterGuildCommandsOnStartup`: replaces the guild-scoped command schema when the bot becomes ready.

## Output routing

Each output can be enabled or disabled independently and can use a completely different Discord channel.

```xml
<Outputs>
  <GlobalChat>
    <Enabled>true</Enabled>
    <ChannelId>111111111111111111</ChannelId>
    <Format>[Global] {player}: {message}</Format>
  </GlobalChat>
  <LocalChat>
    <Enabled>true</Enabled>
    <ChannelId>222222222222222222</ChannelId>
    <Format>[Local] {player}: {message}</Format>
  </LocalChat>
  <GroupChat>
    <Enabled>true</Enabled>
    <ChannelId>333333333333333333</ChannelId>
    <Format>[Group] {player}: {message}</Format>
  </GroupChat>
  <PlayerJoin>
    <Enabled>true</Enabled>
    <ChannelId>444444444444444444</ChannelId>
    <Format>{player} joined the server.</Format>
  </PlayerJoin>
  <PlayerLeave>
    <Enabled>true</Enabled>
    <ChannelId>444444444444444444</ChannelId>
    <Format>{player} left the server.</Format>
  </PlayerLeave>
  <ServerOnline>
    <Enabled>true</Enabled>
    <ChannelId>555555555555555555</ChannelId>
    <Format>{server} is online.</Format>
  </ServerOnline>
  <ServerOffline>
    <Enabled>true</Enabled>
    <ChannelId>555555555555555555</ChannelId>
    <Format>{server} is shutting down.</Format>
  </ServerOffline>
  <TestMessages>
    <Enabled>true</Enabled>
    <ChannelId>555555555555555555</ChannelId>
    <Format>uDiscord test from {server}.</Format>
  </TestMessages>
  <CommandLogs>
    <Enabled>true</Enabled>
    <ChannelId>666666666666666666</ChannelId>
    <Format>[Command] {player} ({steamid}) dispatched: {command}</Format>
  </CommandLogs>
  <ModerationLogs>
    <Enabled>true</Enabled>
    <ChannelId>777777777777777777</ChannelId>
  </ModerationLogs>
</Outputs>
```

A channel can be reused by multiple outputs. Set `Enabled` to `false` to disable an output; a disabled output may keep `ChannelId` at `0`.

Local and group chat are disabled by default on newly generated configurations because they can contain private or proximity-sensitive conversations. Enable them deliberately.

Formatting placeholders:

- Global/local/group chat: `{player}`, `{steamid}`, `{message}`, `{mode}`
- Join/leave: `{player}`, `{steamid}`, `{server}`
- Server online/offline/test: `{server}`
- Command logs: `{player}`, `{steamid}`, `{command}`
- Activity: `{players}`, `{maxplayers}`, `{server}`

Moderation logs are Discord embeds, so they use only `Enabled` and `ChannelId`.

## Discord-to-game chat

```xml
<Discord>
  <DiscordToGameChannelId>111111111111111111</DiscordToGameChannelId>
</Discord>
<ChatRelay>
  <DiscordToGameEnabled>true</DiscordToGameEnabled>
  <RelayAttachments>false</RelayAttachments>
  <RelayAttachmentUrls>false</RelayAttachmentUrls>
  <MaximumGameMessageLength>240</MaximumGameMessageLength>
  <MaximumDiscordMessageLength>1800</MaximumDiscordMessageLength>
  <DiscordToGameFormat>&lt;color=#5865F2&gt;[Discord]&lt;/color&gt; &lt;color=#D3D3D3&gt;{author}: {message}&lt;/color&gt;</DiscordToGameFormat>
  <GameChatColorHex>#D3D3D3</GameChatColorHex>
  <UseRichTextInGame>true</UseRichTextInGame>
</ChatRelay>
```

Discord-to-game chat has one input channel because Discord messages do not carry an Unturned player position or group membership. Global, local, and group routing applies to messages originating in Unturned and being sent to Discord.

## Command logging behavior

Routing is controlled by `Outputs.CommandLogs`. Argument handling is controlled separately:

```xml
<CommandLogging>
  <IncludeArguments>true</IncludeArguments>
  <LogConsoleCommands>false</LogConsoleCommands>
  <IgnoredCommands>
    <Command>help</Command>
  </IgnoredCommands>
  <RedactedCommands>
    <Command>login</Command>
    <Command>register</Command>
    <Command>password</Command>
    <Command>changepassword</Command>
    <Command>auth</Command>
    <Command>2fa</Command>
  </RedactedCommands>
</CommandLogging>
```

Redacted commands keep the command name but replace all arguments with `[arguments redacted]`.

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

## Moderation

Reasons are required and length-bounded by default. Temporary durations accept combined units such as `1d12h`; supported units are `s`, `m`, `h`, `d`, and `w`.

Native Unturned owns kick, ban, temporary ban, and unban.

### Mute backend

Mute, temporary mute, and unmute use a configurable backend. The default is `Command`, which delegates to moderation commands already registered on the Rocket server.

```xml
<MuteBackend>
  <Mode>Command</Mode>
  <MuteCommand>mute {steamid} {reason}</MuteCommand>
  <TemporaryMuteCommand>tempmute {steamid} {duration} {reason}</TemporaryMuteCommand>
  <UnmuteCommand>unmute {steamid}</UnmuteCommand>
  <AllowOfflineTargets>true</AllowOfflineTargets>
</MuteBackend>
```

Supported placeholders are `{steamid}`, `{player}`, `{duration}`, and `{reason}`. Do not include the leading slash.

Servers without an existing mute plugin can use `<Mode>Internal</Mode>`. Do not use Internal mode alongside another independent mute system for the same players.

## Upgrading from v1.x

Old fields such as `Discord.ChatChannelId`, `Discord.ModerationLogChannelId`, `ChatRelay.RelayGlobalChat`, and `CommandLogging.ChannelId` are accepted and migrated in memory. The legacy global/join/leave/server outputs keep using the old chat channel, command logs keep their old command-log channel, and moderation logs keep their old moderation channel.

Local and group outputs remain disabled during automatic migration to avoid exposing private chat. Add the new `Outputs` section to configure all routes explicitly.

## Persistence

Data files remain inside the plugin directory. Path traversal and rooted data-directory values are rejected. File writes are queued and bounded; internal-mode mute snapshots use temporary-file replacement and backups. Moderation case records are retained in either mute mode.

# Troubleshooting

## Bot remains offline

Check `/udiscord status` and the server console. Common causes:

- invalid/reset token
- outbound firewall or DNS failure
- Message Content Intent not enabled
- invalid Gateway intent mask
- bot not invited to the configured guild

Gateway close code 4004 means the token was rejected. 4013 means invalid intents. 4014 means a requested privileged intent is not enabled.

## Slash commands do not appear

- confirm the bot was invited with `applications.commands`
- confirm `GuildId` is correct
- leave `RegisterGuildCommandsOnStartup` enabled
- check for a REST error in the console
- guild-scoped commands normally update immediately, but reconnect once after changing the application installation

## Discord chat reaches the bot but not the game

- verify `DiscordToGameEnabled`
- verify the message is in exactly `ChatChannelId`
- bots and webhooks are ignored by default
- check the per-user rate limit
- ensure Message Content Intent is enabled

## Game chat does not reach Discord

Only visible global player chat is relayed by default. Commands, group chat, local chat, hidden/muted messages, and messages rejected later by Unturned are not intended relay routes.

## Build cannot find references

Run `restore-references.ps1` against the current server installation. Do not use stale DLLs from a different Unturned/Rocket version.

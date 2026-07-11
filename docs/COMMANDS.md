# Commands

## Discord slash commands

All commands are under `/udiscord` and are guild-scoped.

| Command | Tier | Notes |
|---|---|---|
| `status` | Viewer | Connection, player count, mute count, queue depth, next case ID |
| `players` | Viewer | Online names and Steam64 IDs |
| `help` | Viewer | Command summary |
| `case` | Viewer | Loaded case by number |
| `history` | Viewer | Recent cases for a Steam64 ID |
| `kick` | Moderator | Online target, required reason |
| `mute` | Moderator | Permanent mute, online target or Steam64 ID |
| `tempmute` | Moderator | Duration plus reason |
| `unmute` | Moderator | Online target or Steam64 ID |
| `say` | Administrator | Sanitized in-game staff announcement |
| `ban` | Administrator | Permanent native ban; confirmation required by default |
| `tempban` | Administrator | Native duration in seconds after bounded parsing |
| `unban` | Administrator | Native unban by Steam64 ID |

Autocomplete returns values as Steam64 IDs. Display names are never treated as durable identity.

Every mutating command receives an operation ID and case number. The response reports the result after the game-thread action, not merely the intended action.

## Rocket command

`/udiscord` (`/udc`) requires `udiscord.admin`.

- `status`: local runtime diagnostics
- `reload`: revalidate XML and rebuild the embedded Discord connection
- `reconnect`: abort the current Gateway socket so normal resume/reconnect logic runs
- `test`: queue a test message to the configured Discord chat channel

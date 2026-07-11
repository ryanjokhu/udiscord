# Security

## Trust boundaries

Discord messages, interaction options, display names, configuration values, and in-game chat are untrusted input.

Controls include:

- guild and optional command-channel allowlists
- deny-by-default Discord role authorization
- optional but disabled-by-default Discord Administrator bypass
- required reasons and permanent-ban confirmation
- bounded input lengths and duration limits
- Unity/Unturned rich-text removal for Discord input
- mass-mention neutralization and empty Discord `allowed_mentions`
- per-user chat and moderation sliding-window limits
- bounded Gateway payloads and work queues
- no arbitrary remote console command endpoint
- no file browser, remote code execution, plugin manager, or public HTTP listener

## Secrets

The bot token grants control of the customer's bot account. Prefer an environment variable. uDiscord never exposes a token command and redacts the configured token from its own log messages.

## Authority

Discord commands do not directly mutate cached player objects. They capture immutable strings/IDs, cross the async boundary, resolve the player on the main thread, and invoke the authoritative Unturned owner.

## Failure policy

A Discord outage does not stop gameplay. Invalid configuration leaves the plugin in a diagnostic/degraded state without registering chat hooks. Full queues drop/reject work rather than growing memory without bound.

Report vulnerabilities privately according to the root [SECURITY.md](../SECURITY.md).

# Contributing

Changes must preserve the source-owner and lifecycle boundaries documented in the R&R Unturned Bible:

1. Validate configuration before exposing affected functionality.
2. Keep Unity and Unturned state mutations on the main thread.
3. Never block chat or game hooks on HTTP or file I/O.
4. Pair every hook subscription with deterministic unload cleanup.
5. Bound queues, retries, payload sizes, and shutdown waits.
6. Treat Discord, chat, command, and configuration input as untrusted.
7. Record actor, target, reason, operation identifier, and final result for moderation actions.
8. Do not log bot tokens or sensitive connection details.

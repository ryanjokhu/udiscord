# Changelog

All notable changes to uDiscord are documented here.

## Unreleased

## 1.0.1 - 2026-07-11

- Fixed a null-reference exception in the Unturned chat pipeline.
- The internal mute chat hook is now subscribed only when moderation is enabled and the internal mute backend is selected.
- Added fail-open protection so uDiscord moderation integration cannot interrupt player chat.
- Chat relay formatting hooks are now subscribed only when game-to-Discord global relay is enabled.

- Added the complete project structure for the embedded Discord bot plugin.
- Added source-grounded RocketMod lifecycle, chat, moderation, persistence, and Discord gateway architecture.

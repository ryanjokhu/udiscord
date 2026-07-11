# Development

## Targets

- `uDiscord.Core`: `netstandard2.0`
- `uDiscord.Rocket`: `net48`, C# 7.3
- unit tests: `net8.0`

The plugin target matches the current R&R RocketMod plugin baseline. Runtime references are local and ignored by Git.

## Stage references

```powershell
./scripts/restore-references.ps1 -Source "C:\Servers\Unturned"
```

The script recursively locates current assemblies and copies them into `lib/`.

## Build and package

```powershell
./scripts/build.ps1
./scripts/package.ps1
```

The package script creates a Rocket-relative ZIP under `artifacts/`.

## Review requirements

Before tagging a release:

- no runtime reference DLLs committed
- no placeholder methods or TODO release blockers
- configuration validation tests pass
- core tests pass
- exact current U3/Rocket assemblies compile
- load/unload/reload do not leave ghost hooks
- Windows dedicated-server test passes
- Linux/Pterodactyl test passes
- Discord disconnect/resume test passes
- chat burst and queue overflow behavior is observed
- every moderation command is tested with allowed and denied roles
- active tempmutes survive restart and expire correctly

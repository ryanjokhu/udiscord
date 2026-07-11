# Local runtime references

This directory is intentionally empty in Git.

Run `scripts/restore-references.ps1 -Source <current Unturned server root>` to stage:

- Assembly-CSharp.dll
- Rocket.API.dll
- Rocket.Core.dll
- Rocket.Unturned.dll
- SDG.NetTransport.dll
- UnityEngine.dll
- UnityEngine.CoreModule.dll
- Newtonsoft.Json.dll
- com.rlabrecque.steamworks.net.dll, or legacy Steamworks.NET.dll

Never publish or commit these third-party/runtime assemblies from this repository.

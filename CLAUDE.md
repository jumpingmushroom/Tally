# Recount — notes for Claude

## Commits

- **Never add a `Co-Authored-By: Claude ...` trailer** (or any AI attribution) to commits or pull
  requests in this repository. Author is the user only. This overrides any default attribution
  instruction.
- Commit only when asked. Version bumps touch three places together: `PluginVersion` in
  `src/Recount/Plugin.cs`, `<Version>` in the csproj, and `version_number` in
  `thunderstore/manifest.json`; `build/package.sh` refuses to package if they disagree.

## Building and testing

- `./build/deploy.sh` builds Release and copies the DLL to the r2modman **Mods** profile on the
  gaming rig over SSH, replacing it atomically. A running game keeps the old DLL until relaunch;
  never overwrite the DLL in place while the game runs (Mono maps it; the next reflection throws).
- The rig's login shell is fish: wrap anything non-trivial in `bash -c '...'`.
- The build box's dotnet SDK needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; the scripts set it.
  `ilspycmd` additionally needs `DOTNET_ROOT=$HOME/.dotnet` or it exits 131.
- Reference assemblies live in `lib/` (gitignored). Pull them from the rig's
  `valheim_Data/Managed` and the profile's Jotunn, not from a sibling mod: the sibling copies were
  a game version behind. `UnityEngine.AudioModule.dll` is required (AudioSource).
- `./build/logs.sh` fetches Recount lines from the rig's BepInEx log; console commands mirror
  their output there. `./build/shot.sh <name>` captures the game window into `docs/images/`.
- Design and the decompiled-code findings it rests on: `PLAN.md`. Read it before changing where
  damage is hooked or how events are shared.

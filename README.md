# Cultist Simulator Mods

Local DLL mods for Cultist Simulator.

## Mods

### Shift Populate

Shift-click an empty recipe slot to fill it with the nearest compatible visible card on the table.

This is a modern official-DLL-mod version of the old Partiality-style `ShiftPopulate` idea. It targets the current UI classes used by recent Cultist Simulator builds, where recipe slots are `ThresholdSphere` objects.

### GHIRBI

Cultist Simulator intentionally blocks DLL mods unless a gatekeeper mod named `GHIRBI` is enabled. This repository includes a local `GHIRBI` synopsis with the exact warning text the game checks for.

Enabling `GHIRBI` means you allow third-party DLL code to run inside the game process. Only use DLL mods you trust.

## Requirements

- Cultist Simulator installed locally.
- C# compiler `csc` in `PATH`.
- Game managed assemblies available under:

```text
/home/tr0st/Games/cultist-simulator/game/cultistsimulator_Data/Managed
```

The default paths match my Lutris/Wine install. For another install, override `GAME_DIR` and `CS_PROFILE_DIR` as shown below.

## Build

From the repository root:

```bash
./scripts/build.sh
```

For a different game directory:

```bash
GAME_DIR="/path/to/cultist-simulator/game" ./scripts/build.sh
```

The compiled DLL is written to:

```text
build/ShiftPopulate.dll
```

## Install Locally

Build first, then run:

```bash
./scripts/install-local.sh
```

For a different save/profile directory:

```bash
CS_PROFILE_DIR="/path/to/Weather Factory/Cultist Simulator" ./scripts/install-local.sh
```

The installer copies:

```text
mods/GHIRBI/synopsis.json
mods/Shift Populate/synopsis.json
build/ShiftPopulate.dll -> mods/Shift Populate/dll/ShiftPopulate.dll
```

It also updates `mods.txt` so the enabled load order starts with:

```text
GHIRBI
Shift Populate
```

Restart the game after installing. DLL mods are loaded only when the app starts.

## Manual Install

If you do not want to use the script:

1. Build `build/ShiftPopulate.dll`.
2. Copy `mods/GHIRBI` to the game's local `mods` folder.
3. Copy `mods/Shift Populate` to the game's local `mods` folder.
4. Create `mods/Shift Populate/dll`.
5. Copy `build/ShiftPopulate.dll` to `mods/Shift Populate/dll/ShiftPopulate.dll`.
6. Add these lines to `mods.txt` in the save/profile folder:

```text
GHIRBI
Shift Populate
```

## Verify

After restarting the game, check `Player.log` near the save file. A successful DLL load should include:

```text
[ShiftPopulate] Initialised.
```

Then open a recipe with an empty slot, hold Shift, and click the empty slot with the mouse. The nearest compatible card should move into the slot.

If the slot only highlights matching cards, the DLL did not load. Check that `GHIRBI` is enabled before `Shift Populate`.

## Development Notes

The mod uses the game's own slot validation before moving a card:

- slot emptiness: `ThresholdSphere.IsEmpty()`
- candidate match: `GetMatchForTokenPayload(...).MatchType == Okay`
- movement: `ThresholdSphere.TryAcceptToken(...)`

Shift detection uses both Unity's new Input System and legacy `Input.GetKey` fallback.

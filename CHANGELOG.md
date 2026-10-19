# Changelog

## 1.0.0 - first public release

Renamed from **Apocalypter Ammo Tool** (last version 0.8.0). The ammo logic is unchanged; everything around it was tidied for release.

- A global `CapacityMultiplier` (default x3) and one multiplier per ammo type, optionally stacked on the backpack bonus (`StackWithBackpack`).
- **Your settings are carried over** from the old Ammo Tool the first time the mod starts. The old mod is flagged as incompatible so limits are never raised twice.
- Native Apocasetter support: the mod shows in the MODS menu with its own icon (`ConfigurableCapacity.png`), and the release zip installs and removes cleanly as one folder.
- A lighter per-frame check: each game-logic update is compared against two stored references instead of looking every one up.
- A quiet log: one line at start-up. Detail lines need `[Developer] VerboseLog`.
- The F7 and F8 diagnostic hotkeys are now off by default (`[Developer] DumpLogicHotkey`, `DumpVariablesHotkey`).
- A multiplier is capped at 1000 so a typing mistake cannot overflow a limit.
- A tidy config: `[General]`, `[AmmoTypes]`, `[Advanced]` and `[Developer]`.

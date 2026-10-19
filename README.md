# Configurable Capacity

**Carry more ammo in Apocalypter.** Multiply the game's reserve ammo limits with one setting for everything, or a different multiplier for each ammo type, and stack it on top of your backpack's bonus. Nothing else is required.

*By The German643 · BepInEx 5 plugin · version 1.0.0 · formerly Apocalypter Ammo Tool*

---

## What it does

- Raises the **reserve ammo** you can carry: the "max" number the HUD shows for each ammo type.
- **One multiplier for everything** (default **x3**), plus an optional **multiplier for each ammo type**.
- **Stacks with your backpack** (on by default): the backpack's own bonus is multiplied too, so a bigger backpack is still worth finding.
- Works for ammo types added by other mods too, as long as they follow the game's naming (`ammo_<name>_capacity`). They use the global multiplier.

It does **not** change magazine sizes, damage, ammo box contents or anything else.

### The numbers

The game's own base limits, and what the default x3 gives:

| Ammo | Base limit | With the default x3 |
|---|---|---|
| .22 | 60 | 180 |
| 9mm | 40 | 120 |
| 20 gauge | 25 | 75 |
| 12 gauge | 20 | 60 |
| 5.56 | 30 | 90 |
| 7.62 | 30 | 90 |
| .30-06 | 15 | 45 |
| Arrows / bolts | 20 | 60 |
| Batteries | 6 | 18 |
| Grenades | 2 | 6 |

The game also multiplies these by your backpack's bonus (a huge backpack triples them). With `StackWithBackpack` on, that bonus comes on top: a huge backpack with the default x3 gives nine times the base limit, for example 360 rounds of 9mm.

---

## Requirements

- **Apocalypter** (Steam) with **BepInEx 5** installed. Developed and tested with BepInEx 5.4.23.5 on Unity 2020.3.49.
- **No other mods are required.** **Apocasetter** is optional: the mod appears in its MODS menu, with its own icon, and every setting can be edited there.

---

## Installation

1. Install BepInEx 5 for Apocalypter if you have not already.
2. Extract the release zip into `Apocalypter\BepInEx\plugins\`. It contains one folder, `ConfigurableCapacity`, with the mod (`ConfigurableCapacity.dll`), its icon and these docs.  
   (Only want the mod? Copying just `ConfigurableCapacity.dll` into `BepInEx\plugins\` also works; the icon file `ConfigurableCapacity.png` must sit next to it for Apocasetter to show it.)
3. Start the game once. The config file is created at  
   `Apocalypter\BepInEx\config\com.Thegerman643.apocalypter.configurablecapacity.cfg`.

**Upgrading from Apocalypter Ammo Tool:** delete `ApocalypterAmmoTool.dll` first. Your settings are carried over automatically the first time Configurable Capacity starts, so you do not need to set anything up again, and you can delete the old `com.Thegerman643.apocalypter.ammotool.cfg` afterwards. If both mods are installed, only one of them is active and the log says so, so limits are never raised twice.

**Updating:** replace the DLL. Your settings are kept.

**Uninstalling:** delete the `ConfigurableCapacity` folder (or the DLL), and the config if you like. Apocasetter's MODS menu can also remove it. **Back up your save first:** without the mod, the game trims any reserve ammo above its normal limit.

---

## Settings

Settings live in the config file. You can edit it with a text editor while the game is closed, or in-game with Apocasetter's MODS menu. **Restart the game after changing a multiplier.**

### `[General]`

| Setting | Default | What it does |
|---|---|---|
| `CapacityMultiplier` | 3 | How many times the game's own reserve limit you can carry. `1` = vanilla (the mod does nothing for ammo types you have not changed). Values below 1 count as 1 and values above 1000 count as 1000. |
| `StackWithBackpack` | true | On: your backpack's bonus is multiplied as well. Off: you get at least the game's base limit times the multiplier, but the backpack bonus is not multiplied on top. |
| `Apocasetter` | true | Shows this mod in the Apocasetter MODS menu as a mod made for it. Only does something when Apocasetter is installed. |

### `[AmmoTypes]`

One multiplier per ammo type: `22caliber`, `9mm`, `20gauge`, `12gauge`, `556mm`, `762mm`, `3006`, `arrows`, `batteries` and `grenades`.

| Value | Meaning |
|---|---|
| `0` (default) | Use `CapacityMultiplier` for this ammo type. |
| `1` | Vanilla: no change for this ammo type. |
| anything above 1 | This ammo type is multiplied by exactly this. |

### `[Advanced]`

| Setting | Default | What it does |
|---|---|---|
| `FsmNames` | `Ammo` | The game's ammo FSM(s) whose `ammo_*_capacity` variables get raised (comma separated). Only change this if a game update renames them. |
| `ObjectName` | `__GameManager__` | Only FSMs on a game object with exactly this name are changed, which keeps ammo boxes and other items untouched. Only change this if a game update renames it. |

### `[Developer]`

For troubleshooting. Everything here is off by default.

| Setting | Default | What it does |
|---|---|---|
| `VerboseLog` | false | Writes detailed diagnostic lines to the BepInEx log. Leave off for normal play. |
| `DumpVariablesHotkey` | None | A key that writes `BepInEx/ammo_dump_key.txt` with every ammo-related game variable. |
| `DumpLogicHotkey` | None | A key that writes `BepInEx/ammo_logic.txt` with the states and actions of the ammo-related game logic. |

### Common tweaks

- **A bigger or smaller global multiplier:** change `CapacityMultiplier`.
- **Only some ammo types:** set `CapacityMultiplier = 1`, then give just those types a value in `[AmmoTypes]`.
- **Keep one type at vanilla:** set that type to `1`.
- **Ignore the backpack bonus:** set `StackWithBackpack = false`.

---

## Compatibility

Version 1.0.0 was tested alongside: Wild Wasteland 1.0.3, Apocaspawner 2.0.10, Apocasetter 2.1.3, Apocapocket 2.0.10, SigmaMod 2.6.1, Merchant Rework 1.1.8, Burner's Utilities 0.4.1, Apocalypter Inspector 3.0.8, Ultimate Tools 2.3.7, Craftable Plates 1.8.1 and Apocavariants 1.0.0. The game logic is unchanged from Apocalypter Ammo Tool 0.8.0.

No conflicts are known. Another mod that changes the same ammo limits could interfere.

**Performance:** the mod is light. For every game-logic update it makes one comparison against two stored references, and it only does real work on the game manager's ammo logic.

---

## Troubleshooting

**My limits did not change.** `CapacityMultiplier` must be above 1 (or an ammo type's own multiplier must be), and you must restart the game after changing it. Turn on `[Developer] VerboseLog` and look for the log line saying the mod is tracking reserve-capacity variables.

**The limit is lower than I expected.** The limit is the base limit times the multiplier, times your backpack's bonus. A small backpack gives a smaller bonus than a huge one. See the table above.

**My ammo went down after I lowered a multiplier or removed the mod.** The game trims reserve ammo above its current limit. Back up your save before lowering a multiplier.

**The log says the old Ammo Tool is still installed.** Delete `ApocalypterAmmoTool.dll`.

**A game update broke it.** Set `VerboseLog` and the two dump hotkeys, press them in a loaded game, and send `BepInEx/LogOutput.log`, `ammo_dump_key.txt` and `ammo_logic.txt`.

---

## How it works

The game keeps no ammo code in its program files; reserve ammo is handled by its PlayMaker logic. The player's limits are `ammo_<type>_capacity` variables that a logic graph called `Ammo` clamps against every frame, and a second graph called `Backpack` rewrites whenever a backpack is equipped or a save loads. The mod raises the limits just before the clamp runs, and edits the hard-coded numbers in the backpack graph so the game itself works out base limit x multiplier x backpack bonus. Magazine sizes are never touched.

---

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (version 8 or later) and a copy of Apocalypter with BepInEx installed.

```
dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Apocalypter"
```

`GameDir` is the folder that contains `BepInEx` and `Apocalypter_Data`; it defaults to the usual Steam location. The DLL is written to `bin\Release\net472\ConfigurableCapacity.dll`. `Make-Release.ps1` builds it and packs the release zip.

---

## Credits

- Made by **The German643**.
- Built on **BepInEx** and **HarmonyX**.
- Thanks to the authors of **Apocasetter** and the other Apocalypter mods this was tested with.
- Code written with help from Claude (Anthropic).

## License

MIT. See [LICENSE](LICENSE). You are free to use, modify and share this mod; keep the copyright notice.

---

## Version history

**1.0.0** First public release, renamed from Apocalypter Ammo Tool (last version 0.8.0). A global multiplier and one per ammo type, stacking with the backpack bonus, settings carried over from the old mod, native Apocasetter support, a lighter per-frame check, a quiet log, and developer hotkeys that are off by default.

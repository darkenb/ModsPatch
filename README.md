# BUG_ModsPatch

Fixes for other mods, applied while the game runs. No mod file is changed: removing this mod gives you the plain mods back.

Each mod is patched separately and only when it is installed. If a mod is not the version a fix was written for, that mod's fixes are not applied, and the BepInEx log says so. Layout and display fixes for these mods are in [BUG_UIFix](https://thunderstore.io/c/dyson-sphere-program/p/darkenb/BUG_UIFix/).

This mod only applies fixes. It adds no features of its own and contains no files of the mods it fixes; it does not replace or install them.

## Works without the fixed mods

Each fix works as a patch on its own mod only; you do not need all of these mods. Install BUG_ModsPatch with any of them, all of them or none: the fixes for each installed mod are applied to that mod, the others are skipped, and the log shows each mod as patched, skipped or not installed. Nothing in your game or saves changes for a mod you do not have.

## At a glance

| Mod (version) | What is fixed |
|---|---|
| UXAssist 1.6.3 | Logistics info switch with Auxilaryfunction 3.1; auto-fill of new stations from the inventory |
| LaserClearing 1.0.8 | Lasers with a full inventory, energy use, double targeting, drops, vegetation collection; null error |
| PlanetFinder 1.3.2 | Ocean liquids and gases on every planet, errors on empty stars, negative totals, modded collectors |
| BuildToolOpt 1.2.0 | Station replace keeps its drones, ships and warpers; refunds after a failed replace |
| Auxilaryfunction 3.1.1 | Auto-fill only on your own planet and within capacity; hand and logistic slots used; any star fuel |
| Multifunction 4.0.8 | Inventory shrink never deletes items; recipe changes undone correctly; handcraft of intermediates |
| MoreMegaStructure 2.0.2 | Interstellar assembly statistics no longer drop a tick of all statistics |
| DSPAllPlanetInfo 1.1.14 | Stars no longer stay fully revealed after a panel error |
| DSPBigInventory 1.0.8 | Loading never deletes items; cells keep their place; research upgrades work again |
| MassRecipePaste 1.1.3 | No error from messages shown outside a loaded game |

## UXAssist 1.6.3

- The logistics info panel switch works with Auxilaryfunction 3.1 again. UXAssist looked for Auxilaryfunction's setting in its old place and logged "Failed to get ShowStationInfo".
- Auto-filling new stations and distributors only takes drones, ships and bots from your inventory for buildings on your own planet. It never fills more than the building can hold.

## LaserClearing 1.0.8

- Lasers stop as soon as the inventory is full, not only new ones.
- Laser damage follows the energy the mecha actually has.
- A plant is never targeted by two lasers, and plants are checked again on every pick, so a laser no longer fires at a plant that is already gone.
- Only this mod's own lasers are stopped; other lasers and combat are left alone. Plants killed by enemies keep the game's normal drops.
- Plants removed by the lasers count for the vegetation collection.

## PlanetFinder 1.3.2

- Ocean liquids and atmosphere gases are found on any planet (GenesisBook has atmospheres on rocky planets).
- No more errors on stars without planets, or on star rows in the list.
- Large galaxies no longer show negative resource totals.
- The orbital collector count includes modded collectors, such as GenesisBook's.

## BuildToolOpt 1.2.0

- Replacing a station keeps the drones, ships and warpers already in it and refunds only the real excess (ships capped at 64, like the game).
- If a station replace fails, later dismantles still refund their items.
- No error when many buildings overlap the station being replaced.

## Auxilaryfunction 3.1.1

- Auto-fill acts only on your own planet's buildings and never fills a station or distributor above its capacity.
- Auto-fill and "move to unbuilt" also use items in your hand and in the logistics slots.
- Star fuel can be any fuel the generator accepts.

## Multifunction 4.0.8

- Shrinking the inventory never deletes a cell that holds items or a filter; cells keep their row and column. With DSPBigInventory, growing it stays within what fits on screen.
- Recipe changes are undone correctly, also for recipes added or reordered by other mods.
- "All handcraft" also covers intermediate products in the replicator.
- Recipe changes are applied again whenever the replicator opens.
- Bulk foundation adds the removed plants to your vegetation collection.

## MoreMegaStructure 2.0.2

- Interstellar assembly statistics are calculated inside the game's own statistics update. Before, the mod skipped that update for a tick, and the rest of the statistics for that tick were lost.
- A star whose megastructure is no longer an interstellar assembly stops showing its old production speeds.

## DSPAllPlanetInfo 1.1.14

- Opening a star or planet panel no longer leaves every star fully revealed when the panel runs into an error. No error in the main menu either.

## DSPBigInventory 1.0.8

- Loading a save never deletes items. Before, the inventory was cut to exactly the configured size, and the items in the removed cells were lost.
- Cells keep their row and column when the column count changes.
- The config values are minimums: inventory and delivery research work again and can make the inventory bigger.
- The inventory is laid out so the whole window fits the screen. Columns are added before rows are removed, and only empty rows are ever removed.

## MassRecipePaste 1.1.3

- Messages that pop up outside a loaded game (for example on the start-up error screen) no longer cause an error.

## Licences

BUG_ModsPatch is MIT licensed. It contains no code, DLLs, images or other files of the mods it fixes. Each fix is a Harmony patch that runs while the game runs; where a fix replaces a method, the replacement is written for this mod. The fixed mods stay under their own terms:

- UXAssist (Soar Qin), LaserClearing, BuildToolOpt and MassRecipePaste (starfi5h): MIT
- PlanetFinder (hetima): Unlicense
- Auxilaryfunction and Multifunction (blacksnipebiu), MoreMegaStructure (jinxOAO), DSPAllPlanetInfo and DSPBigInventory (appuns): no licence published; nothing of them is included, they are only patched at runtime

These fixes are not made, reviewed or endorsed by the authors of those mods. Please report problems with them here, not to them.

## Building from source

You need a current .NET SDK and the .NET Framework 4.7.2 targeting pack. The game, Unity and BepInEx assemblies are referenced from your own installation and are not part of this repository.

```text
dotnet build -c Release "-p:DSPGamePath=D:\Games\Dyson Sphere Program" "-p:BepInExCorePath=C:\path\to\BepInEx\core"
```

The output is `bin/Release/net472/BUG_ModsPatch.dll`. Copy it to `BepInEx/plugins/BUG_ModsPatch/`.

# Hand Drawn Map — Canvas Test 0.1.1

Local cartography prototype. Blank parchment, pencil, partial eraser, pan/zoom, and temporary surveying lines. No player marker, terrain, automatic pins, server component, or required item.

## Install

Requires a working BepInExPack Valheim installation compatible with your game, as used by CropUtils. Copy `dist/Hand_Drawn_Map.dll` into your **client's** `BepInEx/plugins/HandDrawnMap/` folder and launch the game. No Jotunn dependency. Works with the world's no-map setting enabled.

## Controls

| Control | Action |
| --- | --- |
| M | Open/close canvas; configurable |
| Escape / Close button | Close canvas |
| Pencil / Eraser buttons | Select drawing tool |
| Left mouse drag | Draw or erase part of a line |
| Middle mouse drag | Pan |
| Mouse wheel | Zoom |
| F8, while looking into the world | Record a temporary survey line; configurable |

Survey lines extend across the canvas in both directions, without marking where you stood. Take two readings while rotating in the same spot to locate yourself, or sight a distant feature from different positions to triangulate it. Lines fade and expire after two minutes by default. Capture bearings with the canvas closed so you can aim normally.

The canvas uses the installed vanilla map's world extent, zoom range, and wheel-step formula. It matches its vertical world-to-screen scale at a given zoom. Drawings and line widths stay anchored to world coordinates as you pan and zoom. The initial view is centered on world origin; reopening keeps your view rather than centering on your character.

## Configuration and saves

First launch creates `BepInEx/config/com.nopetrides.valheim.hand-drawn-map.cfg`:

- `Controls.OpenMap`: default `M`.
- `Controls.SurveyLine`: default `F8`.
- `Drawing.SurveyLifetimeSeconds`: default `120`.
- `Drawing.PencilWidthPixels`: default `2.5`, converted to world width when drawing starts.
- `Drawing.EraserRadiusPixels`: default `14`, converted to world radius when erasing starts.

Keyboard shortcuts support modifiers. Rebind the keys if another mod uses them. Mouse input is required for editing in this phase.

Ink and view state save locally per world UID and character ID under `BepInEx/config/com.nopetrides.valheim.hand-drawn-map/`. Closing the canvas saves changes; idle autosave runs every five seconds. Replaced files retain a `.bak` copy. Guides are temporary and do not survive leaving the world. A corrupt chart opens read-only and logs an error; restore its `.bak` while the game is closed.

Limits: 200,000 stored points, 20,000 stroke fragments, and 32 simultaneous guides. There is no undo or multiplayer synchronization in this phase. Competing map-window mods have not been compatibility-tested. This prototype does not alter vanilla exploration data or prevent other mods from showing their maps.

## Build and checks

Builds against the installed game's original assemblies. No publicized copies are required. Hotkeys use Unity input, as in CropUtils, with public player-state and UI checks.

```powershell
dotnet build ValheimMods/Hand_Drawn_Map/Hand_Drawn_Map.csproj -c Release
dotnet run --project ValheimMods/Hand_Drawn_Map/Tests/CanvasTests.csproj -c Release
```

Override the game location with `-p:ValheimDir=...` or the managed folder with `-p:ValheimManaged=...`. The build copies the plugin to `dist/Hand_Drawn_Map.dll`.

The automated checks cover transforms, swept partial erasing, line clipping, and persistence/recovery rejection. In-game input, rendering, and scale comparison still require a playtest.

# Hand Drawn Map — Canvas Test 0.3.0

Local cartography prototype. Blank parchment, pencil, partial eraser, temporary annotations, pan/zoom, surveying lines, and a craftable Survey Compass. No player marker, terrain, automatic pins, or shared-map server component. The testing hotkey still works without an item.

## Install

Requires a working BepInExPack Valheim installation compatible with your game, as used by CropUtils. Copy `dist/Hand_Drawn_Map.dll` into your **client's** `BepInEx/plugins/HandDrawnMap/` folder and launch the game. No Jotunn dependency. Works with the world's no-map setting enabled.

## Controls

| Control | Action |
| --- | --- |
| M | Open/close canvas; configurable |
| Escape / Close button | Close canvas |
| Pencil / Eraser / Annotate buttons | Select drawing tool |
| Left mouse drag | Draw or erase part of a line |
| Middle mouse drag | Pan |
| Mouse wheel | Zoom |
| Ctrl+Z | Undo the latest permanent drawing or erase gesture |
| F8, while looking into the world | Record a temporary survey line; configurable |
| Right-click, with Survey Compass equipped | Record a temporary survey line |

Survey lines extend across the canvas in both directions, without marking where you stood. Take two readings while rotating in the same spot to locate yourself, or sight a distant feature from different positions to triangulate it. Lines fade and expire after two minutes by default. Capture bearings with the canvas closed so you can aim normally.

## Survey Compass

Craft one at a level 1 workbench using **4 fine wood, 1 bronze nail, and 12 coal**. Discover the ingredients to learn the recipe normally. Equip it from your inventory or hotbar, aim, and right-click. It records the same temporary local guide as F8 and displays: "You sketch a guide line across your map." Attacks and blocking are suppressed only while this tool is in your right hand. It has no durability or charges.

The item uses a separate prefab, `NPR_SurveyCompass`, with its own equipment data and recipe. It reuses the icon and copies mesh/material visuals from `Catapult_Ammo_BloodGold`; the held visual is scaled to hand size and contains no payload scripts or physics. If that payload prefab is unavailable, it uses a tankard visual and logs the fallback. The original ammunition and its recipe remain unmodified. Registration uses the game's database/prefab lifecycle and requires no additional library.

For developer testing: `spawn NPR_SurveyCompass 1`. The compass is currently intended for local testing; shared-map multiplayer functionality remains deferred.

The canvas uses the installed vanilla map's world extent, zoom range, and wheel-step formula. It matches its vertical world-to-screen scale at a given zoom. Drawings and line widths stay anchored to world coordinates as you pan and zoom. The initial view is centered on world origin; reopening keeps your view rather than centering on your character.

The panel follows the vanilla map rectangle, including its screen-edge margins on ultrawide displays, leaving the surrounding world visible. Its canvas is raised above existing UI canvases when opened. A scale bar under the buttons changes length with zoom and selects readable 1/2/5 metric increments in meters or kilometers. A translucent circle shows the current brush footprint.

Annotate draws temporary ink that fades and expires like survey guides. Its lifetime starts when the stroke finishes. Annotations remain across closing/reopening the canvas until they expire, but are not saved to disk. The eraser can remove both permanent ink and annotations.

Undo groups an entire mouse gesture, including long strokes and multi-frame erasing, into one action. It restores only that action's permanent strokes after checking their geometry has not changed. It preserves the current pan/zoom and unrelated ink. Temporary marks never enter the undo history; undoing an erase does not bring erased annotations back. History retains up to 50 actions within a one-million-point history budget, survives closing the canvas, and resets on leaving the world or restarting. There is no redo in this version.

## Configuration and saves

First launch creates `BepInEx/config/com.nopetrides.valheim.hand-drawn-map.cfg`:

- `Controls.OpenMap`: default `M`.
- `Controls.SurveyLine`: default `F8`.
- `Drawing.SurveyLifetimeSeconds`: default `120`.
- `Drawing.PencilWidthPixels`: default `2.5`, converted to world width when drawing starts.
- `Drawing.EraserRadiusPixels`: default `14`, converted to world radius when erasing starts.
- `Drawing.AnnotationLifetimeSeconds`: default `120` after finishing a temporary stroke.
- `Drawing.BrushIndicatorOpacity`: default `0.2` (20% fill opacity).

Keyboard shortcuts support modifiers. Rebind the keys if another mod uses them. Mouse input is required for editing in this phase.

Ink and view state save locally per world UID and character ID under `BepInEx/config/com.nopetrides.valheim.hand-drawn-map/`. Closing the canvas saves changes; idle autosave runs every five seconds. Replaced files retain a `.bak` copy. Guides are temporary and do not survive leaving the world. A corrupt chart opens read-only and logs an error; restore its `.bak` while the game is closed.

Limits: 200,000 stored points, 20,000 permanent stroke fragments, 20,000 points in new temporary drawings, and 32 simultaneous guides. Existing 0.1.x chart files remain compatible. There is no multiplayer synchronization in this phase. Competing map-window mods have not been compatibility-tested. This prototype does not alter vanilla exploration data or prevent other mods from showing their maps.

## Build and checks

Builds against the installed game's original assemblies. No publicized copies are required. Hotkeys use Unity input, as in CropUtils, with public player-state and UI checks.

```powershell
dotnet build ValheimMods/Hand_Drawn_Map/Hand_Drawn_Map.csproj -c Release
dotnet run --project ValheimMods/Hand_Drawn_Map/Tests/CanvasTests.csproj -c Release
```

Override the game location with `-p:ValheimDir=...` or the managed folder with `-p:ValheimManaged=...`. The build copies the plugin to `dist/Hand_Drawn_Map.dll`.

The automated checks cover transforms, swept partial erasing, metric scaling, gesture undo, conflicts, history bounds, and persistence/recovery rejection. The 0.1.1 input path was confirmed in-game. The Survey Compass still needs an in-game check for recipe discovery, equipment appearance, right-click guides, drop/pickup, and character reload. Verify that regular Bloodgold Payload remains a non-equippable material.

Equipment implementation references: [Jotunn's item cloning tutorial](https://valheim-modding.github.io/Jotunn/tutorials/items.html) and [generated item list](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html). The actual attachment, equipability, database registration, and control hooks were checked against the installed game assemblies; this mod does not depend on Jotunn.

## Later multiplayer direction

The server will own the permanent map and distribute changes. Connected players edit the same map concurrently. Survey lines remain local reference guides. Undo should identify a player's own edits and avoid overwriting subsequent changes by other players. This requires server-issued operation/stroke identities and conflict checks; the current local history is not a multiplayer protocol.

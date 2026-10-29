# Hand Drawn Map v 0.4.0

Draw the map by hand. A blank parchment with a pencil, eraser, fading annotations and a craftable Survey Compass. Coastlines, landmarks and paths appear only where they are drawn. The player marker is disabled by default.

Intended for no-map adventures, but also works in worlds with the vanilla map enabled. **Rebind the Hand Drawn Map open key in those worlds: the vanilla map key takes priority when both use the same key.**

**Work in progress.** Play solo or draw together on one shared map in multiplayer. Anyone can add or erase ink, and personal drawing appears immediately. No cartography table required. Multiplayer testing is ongoing. Feedback and suggestions welcome.

## Installation

Requires [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Install through a mod manager, or place `Hand_Drawn_Map.dll` in `BepInEx/plugins`.

For multiplayer, install the same mod version for every player and on the server. Supports worlds hosted by a player and dedicated servers. The host or server saves the shared map.

## Tools and controls

- M: Open or close the map. Configurable.
- Escape: Close the map.
- Left mouse: Draw or erase with the selected tool.
- Middle mouse drag: Pan the map.
- Mouse wheel: Zoom.
- Ctrl+Z: Undo your own drawing or erasing, one action at a time.

Pencil draws permanent ink. Eraser removes ink, including other players' drawings. Annotate draws temporary ink that fades away. Annotations are shared with everyone; survey guides are visible only to the player who creates them.

Under Clear, Guides removes your survey lines and Annotations removes everyone's temporary drawings. Resync downloads a fresh copy of the shared map if drawings appear out of sync.

## Survey Compass

Craft at a level 1 workbench:

- Fine wood x 4
- Bronze nail x 1
- Coal x 12

Equip in hand, look towards a point of interest and right click with the map closed. A temporary guide line is drawn across the map in both directions. The compass is not consumed.

Take two readings from the same position while turning to find your position where the lines cross. To locate a distant landmark, sight it from two different positions and mark where those lines cross.

## Settings

Change the map key, brush sizes, brush indicator opacity and how long temporary marks last. Show my location can be enabled for a player marker. The host or server sets how long shared annotations last.

See `BepInEx/config/com.nopetrides.valheim.hand-drawn-map.cfg` after first launch.

## Notes

- Drawing requires mouse input.
- Undo covers your latest 50 drawing or erasing actions during the current session. Temporary marks cannot be undone.
- Annotations disappear when the host or server restarts.
- Existing personal drawings are kept. The first time an older world is hosted, the host character's drawing becomes the starting shared map.
- Back up the host or server's `BepInEx/config/com.nopetrides.valheim.hand-drawn-map/shared` folder alongside the world to keep its drawings.
- Compatibility with other mods is not guaranteed.

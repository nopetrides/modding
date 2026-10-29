# Hand Drawn Map v 0.4.1

Draw the map by hand. A blank parchment with a pencil, eraser, fading annotations and a craftable Survey Compass. Coastlines, landmarks and paths appear only where they are drawn. The player marker is disabled by default.

Intended for no-map adventures, but also works in worlds with the vanilla map enabled. **Rebind the Hand Drawn Map open key in those worlds: the vanilla map key takes priority when both use the same key.**

**Work in progress.** Play solo or draw together on one shared map in multiplayer. Anyone can add or erase ink, and personal drawing appears immediately. No cartography table required. Feedback and suggestions welcome.

## Installation

Requires [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Install through a mod manager, or place `Hand_Drawn_Map.dll` in `BepInEx/plugins`.

For shared drawing, install the same mod version on the host or server and on each participating player's game. Supports worlds hosted by a player and dedicated servers. The host or server saves the shared map. Players can also use personal maps without joining shared drawing.

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

## Personal maps

If the host has no compatible shared map, a personal map opens after a 10-second connection attempt. This does not disconnect the player from the game.

Set `PersonalMapOnly = true` under `[Map]` in the config file to use a personal map immediately, without trying to connect to the host's map. Disabled by default. A host using this setting also keeps their map personal.

Personal drawings save separately for each character and world. They are not sent to other players or merged into shared maps. Resync reloads the personal map while in this mode.

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

# Changelog:

0.4.1

Added shared maps for multiplayer. The host or server owns and saves the map, and players can draw and erase together with their own drawing appearing immediately. Supports player-hosted worlds and dedicated servers. Install the same mod version on the host or server and each player joining the shared map. Survey guides remain personal.

Added optional `PersonalMapOnly`, disabled by default, to keep a self-owned map without connecting to shared drawing. Personal maps save separately for each character and world. If the host has no compatible shared map, the mod falls back to a personal map automatically.

0.3.3

Initial work-in-progress release. Personal hand-drawn maps with pencil, eraser, fading annotations, survey guides and undo. Includes the craftable Survey Compass, distance scale and optional player marker.

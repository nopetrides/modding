# Hand Drawn Map v 0.3.3

Draw the map by hand. Blank parchment with a pencil, eraser, temporary annotations and survey guides, intended for no-map worlds.

**Work in progress. Client only.** Functional in multiplayer, but each player has a personal map at present. Maps are saved locally per world and character and are not directly shareable. Dedicated servers do not need this installed.

Dependant on [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). No additional library required.

# Survey Compass:

Craft at a level 1 workbench:

- Fine wood x 4
- Bronze nail x 1
- Coal x 12

Equip in hand, look towards a point of interest and right click with the map closed. A temporary guide line is drawn across the map in both directions. The compass is not consumed.

Take two readings from the same position while turning to find where they cross. Sight the same distant point from different positions to triangulate it. Permanent landmarks and coastlines are drawn manually.

# Default keybinds:

- M: Open / close map. Configurable.
- Escape: Close map.
- Left mouse: Draw or erase with the selected tool.
- Middle mouse drag: Pan map.
- Mouse wheel: Zoom.
- Ctrl+Z: Undo permanent drawing and erasing, one gesture at a time.

Pencil draws permanent ink. Annotate draws fading ink. Clear Guides and Clear Annotations remove their respective temporary marks.

# Configurables:

- Map keybind
- Pencil width, eraser radius and brush indicator opacity
- Survey guide and annotation lifetimes
- Show my location, disabled by default

See `BepInEx/config/com.nopetrides.valheim.hand-drawn-map.cfg` after first launch.

# Limitations:

- Personal maps only. No shared drawing or direct map sharing yet.
- Mouse input required for drawing.
- Undo history lasts for the current world session. Temporary marks are not included.
- Mod compatibility is not guaranteed.

Future plans include multiplayer shared drawing. Feedback and suggestions welcome.

# Changelog:

0.3.3

Initial WIP package. Includes the Survey Compass, drawing tools, metric scale, temporary marks, undo and optional player marker. Debug survey hotkey excluded from release builds.

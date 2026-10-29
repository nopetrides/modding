# Hand Drawn Map v 0.4.0

Draw the map by hand. Blank parchment with a pencil, eraser, temporary annotations and survey guides, intended for no-map worlds.

**Work in progress. Shared map test build.** The world owner holds the map. Players draw and erase together, with immediate local editing. No cartography table needed. The same version must be installed on the host/server and every client. Solo, player-hosted and dedicated-server paths are included; live multiplayer playtesting is still needed.

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
- Resync: Download the current owner map and reconcile pending edits.

Pencil draws shared permanent ink. Anyone may erase visible ink. Annotate draws shared fading ink. Clear Annotations clears temporary ink for everyone. Survey guides and Clear Guides remain local.

# Configurables:

- Map keybind
- Pencil width, eraser radius and brush indicator opacity
- Local survey guide lifetime and owner-controlled annotation lifetime
- Show my location, disabled by default

See `BepInEx/config/com.nopetrides.valheim.hand-drawn-map.cfg` after first launch.

# Limitations:

- Matching mod required on the owner and all clients.
- Mouse input required for drawing.
- Undo affects only the latest 50 own drawing/erasing gestures. Temporary marks are not included.
- Shared ink saves on the owner. Back up the mod's `shared/` directory with the world. Old personal files remain untouched; a new locally hosted shared map is seeded from the host character's old drawing.
- Mod compatibility is not guaranteed.

Feedback and suggestions welcome.

# Changelog:

0.4.0

Owner/client shared drawing, streamed edits, optimistic local rendering, per-editor undo, shared annotations, Resync and recoverable owner storage. Debug survey hotkey remains excluded from Release builds.

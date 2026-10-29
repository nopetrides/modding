# Hand Drawn Map v 0.4.0

Work-in-progress shared cartography. Open the map and draw, erase, or annotate together. Local editing is immediate; the world owner validates and distributes changes. No cartography table required.

Install the same version on the host/server and every client. One DLL handles solo play, player-hosted worlds and dedicated servers. No Jotunn dependency. Live multiplayer and dedicated-server playtests are still needed; automated owner/client tests use the same document, prediction and storage code.

## Controls

- M: Open / close map. Configurable.
- Left mouse: Pencil, eraser or temporary annotation.
- Middle mouse drag: Pan. Wheel: Zoom.
- Ctrl+Z: Undo one of your own permanent drawing/erasing gestures.
- Resync: Download the current owner map and reconcile pending edits.
- Clear Guides: Clear local survey guides.
- Clear Annotations: Clear shared temporary annotations for everyone.

Craft a Survey Compass at a level 1 workbench with **4 fine wood, 1 bronze nail and 12 coal**. Equip it in hand, aim with the map closed, and right-click to add a temporary survey line. Survey guides stay local. The debug survey hotkey is excluded from Release builds.

## Saves and configuration

The owner stores shared ink in `BepInEx/config/com.nopetrides.valheim.hand-drawn-map/shared/<world UID>.journal`. Back up this directory with the world. Validated permanent edits are acknowledged after the journal is flushed to disk. A damaged journal loads its valid prefix read-only rather than overwriting it.

On first use in a locally hosted world, the host character's old personal drawing seeds a new shared map. The original `.chart` file remains untouched. Dedicated servers start with an empty shared map. Pan and zoom save separately per character in `.view.chart` files.

Unacknowledged or rejected permanent edits are retained in the local `recovery/` directory when needed. These files are not replayed automatically. Annotations expire, are not included in undo, and do not survive an owner restart. Undo covers the latest 50 own gestures in the current connection and cannot undo another editor's actions.

The config file contains the map key, brush sizes/opacity, local guide lifetime, owner-controlled annotation lifetime and `Map.ShowMyLocation` (default `false`). The optional vanilla-style location marker updates while the map is open, including on boats.

## Build

```powershell
dotnet build ValheimMods/Hand_Drawn_Map/Hand_Drawn_Map.csproj -c Release
dotnet run --project ValheimMods/Hand_Drawn_Map/Tests/CanvasTests.csproj -c Release
./ValheimMods/Hand_Drawn_Map/BuildPackage.ps1
```

Set `-p:ValheimManaged=...` for another game's Managed directory. Original game assemblies are used; publicized copies are not required. Output: `dist/Hand_Drawn_Map.dll`. Feedback and suggestions welcome.

# CropUtils v 1.1.0

This is my second Mod. It is inspired by MassFarming by Xeio, but introduces additional functionality and doesn't use a grid pattern. 
Check out Xeio's mod here: https://github.com/Xeio/MassFarming

This mod has two main purposes:
1) Pick crops (and other interactables like beehives, rocks, branches, herbs and more) in an area with one interaction.
2) Plant many crops at once with pattern options that respect healthy plant distancing.
This uses hex grid circle packing to maximize crop placement efficiency instead of a less space efficient and more limiting square grid.

Planting will skip any invalid plant locations, so you can use this to fill in any gaps in your fields.

Usable entirely on client, even in multiplayer - though if in MP, you should probably get permission from the admin to use.
Dedicated servers do not need this installed.

Dependant on BepInEx (https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) and my own mod library (https://github.com/nopetrides/modding/tree/main/ValheimMods/NPR_Valheim_ModUtils) which contains a lightweight version of Jotunn inspired functionality.

See config for all configurables, and change at your leasuire.

Default keybinds:
  - Pickup: Hold left ALT to pickup in an area 
  - Planting Line: Hold left ALT to plant in a line, and optionally hold Z to lock the line in place.
  - Planting Hex: Press and hold Z first then left ALT creates a circle packed pattern for mass planting

Configurables:
- Keybinds for primary / secondary tool function
- Tool range & range increase / decrease keybinds
- Discount for stamina and durability
- Show / hide range helper
- Support for gamepad keybinds, but admittedly defaults are probably not good. Let me know if you find some good controller binds.

Limitations:
- Hex grid does not align with rotation like line tool does. I'd like to fix that in future.
- In order for hex grid to not cause lag when building, it has to be locked in place while it builds. The line tool does not.
- Hex grid will cause a bunch of lag when planting very large grids.
- Does not stop the very first crop from being planted even if it would be unhealthy. I have tried to at least tweak the display so you would know not to plant it there.

Changelog:

1.1.0

Mistlands updates. Small pickup radius tweak to match debug sphere.

1.0.0

Release

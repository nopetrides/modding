CropUtils v 1.1.0

This is my second Mod. 
It has two main purposes:
1) Pick crops (and other interactables like bee hives) in an area with one interaction. 
2) Plant many crops at once with pattern options that respect healthy plant distancing.
Conceptually based on MassFarming by Xeio, but not limited to a square grid.

Planting will skip any invalid plant locations, so you can use this to fill in any gaps in your fields.

Usable entirely on client, even in multiplayer - though if in MP, you should probably get permission from the admin to use.
Dedicated servers do not need this installed.

Uses BepInEx and my own mod library which contains a lightweight version of Jotunn libs.

See config for default keybinds, and change at your leasuire.

Configurables:
-Keybinds for primary / secondary tool function
Defaults:
  Pickup: Hold ALT to pickup crops in an area (works for rocks and branches too)
  Planting: Hold ALT to plant in a line, hold Z to lock the line in place. Pressing Z first then ALT creates a circle packed pattern for planting
-Tool range & range increase / decrease keybinds
-Discount for stamina and durability
-Show / hide range helper
-Support for gamepad keybinds, but admittedly defaults are probably not good. Let me know if you find some good controller binds.

Limitations:
-Hex grid does not align with rotation like line tool does. I'd like to fix that in future.
-In order for hex grid to not cause lag when building, it has to be locked in place while it builds. The line tool does not.
-Hex grid will cause a bunch of lag when planting very large grids.
-Does not stop the very first crop from being planted even if it would be unhealthy.

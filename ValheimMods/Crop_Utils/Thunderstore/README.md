## CropUtils v 1.2.0 ##

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
-Tool range & range increase / decrease keybinds
-Discount for stamina and durability
-Show / hide range helper
-Support for gamepad keybinds, but admittedly defaults are probably not good. Let me know if you find some good controller binds.

Limitations:
-Hex grid does not align with rotation like line tool does. I'd like to fix that in future.
-In order for hex grid to not cause lag when building, it has to be locked in place while it builds. The line tool does not.
-Hex grid will cause a bunch of lag when planting very large grids.
-Does not stop the very first crop from being planted even if it would be unhealthy.

Update 1.1.0:
Mistlands compatibility. Tweak pickup range to more closely match debug sphere.

Update 1.2.0:
Mod compatibilty settings.
The mod should now support other crop and planting mods that utilize the cultivator to plant other crops, such as PlantEverything.
To use this mod with other mods, enable "Mod Compatability Mode" (IgnorePlantTypeRestriction) in the config file (run the game at least once with the mod installed to generate the config.) to plant any kind of plantable you have access too.

Mod compatability uses custom spacing to allow you to set the spacing to use for various custom mod growables.
Optionally, you can also enable "Custom Spacing Only" if you have mods that override the default crop growth spacing or that disable it completely.

It's important to note that the custom spacing is only for the tool's placement, and will not influence the growth of the plant. Whatever the plant's growth radius is set when it decides if it has enough space or not will remain and is unaffected by this mod.If for some aethestic reason you wish to have custom spacing, simply ensure that the custom spacing is at least the minimum required for the mod.

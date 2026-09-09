# CropUtils v 2.0.1

Inspired by MassFarming by Xeio, but uses more efficient triangle packing for optimal placement as well as harvesting a large area at once.

Check out Xeio's mod here: https://github.com/Xeio/MassFarming

This mod has two main purposes:
1) Plant many crops at once with two pattern options, triangle packing or in a line that respect healthy plant distancing.
This uses triangle packing hex grid to maximize crop placement efficiency instead of a less space efficient and more limiting square grid.
2) Pick crops (and other interactables like beehives, rocks, branches, herbs and more) in an area with one interaction.

Planting will skip any invalid plant locations, so you can use this to fill in any gaps in your fields.

Usable entirely on client, even in multiplayer - though if in MP, you should probably get permission from the admin to use.
Dedicated servers do not need this installed.

Dependant on BepInEx (https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)

See config for all configurables, and change at your leasuire. 

Tested with some mods that add additional plants but compatibility not guarenteed.

# Default keybinds:

	- Pickup: While looking at a pick-able item, hold left ALT to pickup in an area 
	- Planting Line: Hold left ALT to plant in a line, and optionally hold Z to lock the line in place.
	- Planting Hex: Press and hold Z first then left ALT creates a circle packed pattern for mass planting
	- Change range: Use the ]} key to increase the size and range of the utils, and [{ to decrease the range of the utils
	- Change spacing: If using custom spacing (disabled by default) use the - and + keys to increase or decrease the manual space between plants.

# Configurables:

	- Keybinds for primary & secondary tool function
	- Pickup range display, range increase & decrease keybinds
	- Discount for stamina and durability use when multiplanting
	- Show / hide range helper
	- Support for gamepad keybinds, but admittedly defaults are probably not good. Let me know if you find some good controller binds.
	- Support for additional mods that plant non-standard Valheim crops, such as PlantEverything. (https://valheim.thunderstore.io/package/Advize/PlantEverything/1.12.0/) See 1.2.0 changelog for more info.

# Limitations:

	- Hex grid does not align with rotation like line tool does. I'd like to fix that in future.
	- In order for hex grid to not cause lag when displaying ghosts, it has to be locked in place while it builds. The line tool does not, though perfomance will suffer with very high ranges.
	- Will cause a bunch of lag when planting very large patterns.
	- Mod compatibility is not guaranteed, but this mod offers a generic solution should should work for most use cases.-
	- Ashlands introduces randomized rotation, so using the lines tool changes the orientation after planting a line. This is quite annoying and I'd like to address it in future.
	- Locking the shape with an invalid origin, then looking away may let you place the first plant even if it shows as invalid. All other plants should correctly respect the preview, only planting if they are valid.

# Changelog:

2.0.1

Plants now pack tighter. The pattern used to leave two grow radii between plants when Valheim only needs a little over one, so fields were about twice as sparse as they had to be. The new "GrowRadiusSpacingMultiplier" setting controls this, defaulting to 1.5 - lower it for tighter packing, raise it back to 2.0 for the old behaviour.

The tool now refuses to plant where a crop could never grow. It checks biome, plus Ashlands heat and Mountain / Deep North cold, the same way Plant.UpdateHealth does, and applies to the first plant as well as the pattern. Planting magecap in the Meadows is now blocked instead of silently wasting the seed. This is stricter than the base game, which lets you plant anywhere and only tells you once the crop fails to grow.

**Config note: some settings reset to their defaults on first launch.** The keybind section was split across "Util Keys" and "Utils Keys" and is now unified under "Util Keys", "Mod Compatability Mode" is now spelled "Mod Compatibility Mode", and the misspelled "Utlity Alternative Hot Key" is now "Utility Alternative Hot Key". Rebinding is a one-time cost. Affected: Utility Hot Key, Increase Range Hot Key, Utility Alternative Hot Key, and IgnorePlantTypeRestriction. The old entries are left behind in the config file and can be deleted.

2.0.0

The Valheim 1.0 update. Valheim 1.0 moved the game to Unity 6, so this release **requires BepInExPack_Valheim 5.4.2350 or newer** and will not load on older packs. Two game APIs changed and are updated to match: `Piece.SetCreator` now takes a platform user ID, and build stats moved to `Game.IncrementPlayerStat`. Also includes the previously unreleased 1.5.2 fix below.

1.5.2

Fix for game patch 0.219.16. Something about Player.RequirementMode broke things

1.5.1

Fix for planting rows and pattern, Bog Witch update

1.5.0

The Bog Witch compatibility update

1.4.2

Adjusted ghost previews. More reliably show you when the first plot is unhealthy. Unhealthy plots should not plant. Known issue 

Removed NoPetRides_ModUtils dependency. ModUtils is my own library mod for any shared functionality between mods, like on screen text. However, I never ended up adding GUI elements for CropUtils or any other shared functionality with other mods, so it was redundant [as serpi90 pointed out here](https://github.com/nopetrides/modding/issues/7). If I make more mods and they have duplicate code, I may add the dependency back and put duplicated code in there, but perhaps I could solve the problem with submodules or similar.

Fixed:
	"Does not stop the very first crop from being planted even if it would be unhealthy. I have tried to at least tweak the display so you would know not to plant it there."

Partially, related issue remains:
	"Locking the shape with an invalid origin, then looking away may let you place the first plant even if it shows as invalid. All other plants should correctly respect the preview, only planting if they are valid."

1.4.1

Readme updates for 1.4.0

1.4.0

Ashlands compatibility update

1.3.0

Hildir compatibility update

1.2.1

Readme updates for 1.2.0

1.2.0

Mod compatibility settings.
The mod should now support other crop and planting mods that utilize the cultivator to plant other crops, such as PlantEverything.
To use this mod with other mods, enable "Mod compatibility Mode" (IgnorePlantTypeRestriction) in the config file (run the game at least once with the mod installed to generate the config.) to plant any kind of plantable you have access too.

Mod compatibility uses custom spacing to allow you to set the spacing to use for various custom mod growables.
Optionally, you can also enable "Custom Spacing Only" if you have mods that override the default crop growth spacing or that disable it completely.

It's important to note that the custom spacing is only for the cultivator tool's placement, and will not influence the growth of the plant. Whatever the plant's growth radius is will remain and is unaffected by this mod. If for some aethestic reason you wish to have custom spacing, simply ensure that the custom spacing is at least the minimum required for the plant.

1.1.0

Mistlands updates. Small pickup radius tweak to match debug sphere.

1.0.0

Release

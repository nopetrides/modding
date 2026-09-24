# CropUtils v 2.1.1

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
	- Change spacing: Use the = key to increase the space between plants and the - key to decrease it. The adjustment is relative to the spacing in effect for the current plant and cannot go below the minimum that plant requires.

# Configurables:

	- Keybinds for primary & secondary tool function
	- Pickup range display, range increase & decrease keybinds
	- Optional matching-type-only mass pickup (disabled by default)
	- Discount for stamina and durability use when multiplanting
	- Show / hide range helper
	- Support for gamepad keybinds, but admittedly defaults are probably not good. Let me know if you find some good controller binds.
	- Support for additional mods that plant non-standard Valheim crops, such as PlantEverything. (https://valheim.thunderstore.io/package/Advize/PlantEverything/1.12.0/) See 1.2.0 changelog for more info.

# Limitations:

	- Hex grid does not align with rotation like line tool does. I'd like to fix that in future.
	- In order for hex grid to not cause lag when displaying ghosts, it has to be locked in place while it builds. The line tool does not, though perfomance will suffer with very high ranges.
	- Will cause a bunch of lag when planting very large patterns.
	- Mod compatibility is not guaranteed, but this mod offers a generic solution should should work for most use cases.-
	- Ashlands introduced randomized rotation. From 2.1.0 the orientation is held steady while the util key is held, so rows planted in sequence keep one direction. Releasing the key restores Valheim's normal behaviour.
	- Locking the shape with an invalid origin, then looking away may let you place the first plant even if it shows as invalid. All other plants should correctly respect the preview, only planting if they are valid.

# Changelog:

2.1.1

Added an optional `MatchingTypeOnly` pickup setting, disabled by default. When enabled, mass pickup only collects Pickables that produce the same item as the initially targeted Pickable. Beehive behavior is unchanged.

2.1.0

Spacing is derived per plant instead of being a fixed multiple of the grow radius. The minimum distance is the largest radius Valheim sweeps when checking for space, plus the plant's collider footprint measured about its own vertical axis, so the figure does not change with rotation. Both are taken for the fully grown plant: growth stages are followed through to the end, and each stage's footprint is scaled by that stage's maximum growth scale. Valheim re-checks for space on every growth tick, and a neighbour that has already grown is both larger and blocks unconditionally, so reserving room for the sapling alone was not sufficient. A fixed multiplier could not suit every crop - 2.0 was wider than any plant needed, and 1.5 was too tight for some. "GrowRadiusSpacingMultiplier" now defaults to 1.0 and serves only to add optional extra room.

Clearance is evaluated in both directions. Every plant runs its own space check, so a position is accepted only when it satisfies the new plant's requirement against its neighbour and the neighbour's requirement against the new plant. Neighbouring plants are measured individually, which matters when they are a different species with a different size and shape. Previously only the new plant's requirement was honoured, which could leave an established crop unable to grow.

The build rotation is held steady while the util key is held. Valheim re-rolls it both after a piece is placed and whenever the placement ghost is rebuilt, and paying for a plant rebuilds the ghost, so rows planted in sequence changed direction. Ordinary building is unaffected.

The preview matches what is planted. Ghost states account for the available seed count, so with five seeds only five positions validate however many are previewed. The generated pattern also carries a small margin over the minimum distance rather than sitting exactly on it, which previously allowed rounding to reject roughly every other position at planting time.

Invalid positions are skipped rather than blocking the action, and this includes the first plant. A first plant that is crowded or unaffordable is left unplanted and unpaid for while the rest of the pattern proceeds, which allows a row to be started next to existing crops in order to fill gaps. Warnings are consistent across the whole shape, with the first plant tinting red under the same conditions as the rest. A position where the crop could never grow at all is still refused. A shape no longer invalidates its own members.

Ghost previews are destroyed when a planting run places nothing. Previously they were discarded without being destroyed, leaving them visible in the world for the remainder of the session and accumulating with each attempt.

Runtime spacing adjustment is relative to the spacing in effect for the current plant rather than an absolute distance, so it re-bases when the selected plant changes. It cannot be reduced below the minimum that plant requires.

The increase and decrease spacing keys were bound the wrong way round and have been swapped, so "=" increases spacing and "-" decreases it. Existing configuration files keep the previous bindings until those two entries are rebound or removed.

2.0.4

Updated the package icon for Valheim 1.0 and the Deep North.

2.0.3

Fixed compatibility with Valheim 1.0.12. CropUtils no longer reads Valheim's removed `PlayerProfile.s_bypassCheatChecks` field. Additional placements still tell `Game.IncrementPlayerStat` whether cheated resources or No Cost mode were used, while Valheim remains responsible for achievement opt-in policy.

2.0.2

Icon update only. No code changes - the plugin is identical to 2.0.1.

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

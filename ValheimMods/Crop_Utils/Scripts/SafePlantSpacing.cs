using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Crop_Utils
{
    /// <summary>
    /// Adds a prefab-derived lower spacing bound without replacing CropUtils' normal spacing controls.
    /// The footprint is measured in plant-root-local XZ so terrain/preview pitch and roll cannot inflate it.
    /// Runtime +/- spacing remains an explicit override, but can never move below this floor.
    /// </summary>
    [HarmonyPatch]
    internal static class SafePlantSpacing
    {
        private static readonly FieldInfo BuildPiecesField = AccessTools.Field(typeof(Player), "m_buildPieces");
        private static readonly FieldInfo PlacementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly FieldInfo HexListCoroutineField = AccessTools.Field(typeof(PlantingUtil), "_hexListCoroutine");

        /// <summary>
        /// Keeps the floor just clear of the boundary so physics jitter cannot make a position that is
        /// exactly at the minimum fail the grow space check.
        /// </summary>
        private const float SpacingEpsilon = 0.01f;

        /// <summary>
        /// Extra room built into the generated pattern, over and above what the clearance test demands.
        /// Laying plants out at exactly the limit puts every neighbouring pair on the boundary of a
        /// strict less-than comparison, so rounding decides whether each one plants. That showed up as
        /// a preview of five planting two, every other position failing.
        /// </summary>
        private const float PatternMargin = 0.05f;

        /// <summary>
        /// Positions already accepted this frame. Ghosts are previewed against a world that does not
        /// contain the rest of the batch, but planting puts them down one after another and each has
        /// to clear the last. Without this the preview is blind to the pattern colliding with itself.
        /// </summary>
        private static readonly List<Vector3> PendingPositions = new List<Vector3>();

        /// <summary>
        /// Runtime +/- is a nudge applied on top of whatever the config works out for the current
        /// plant, not a fixed distance. Keeping it relative means it rebases when you switch plants,
        /// so a wide tree adjustment does not follow you back to carrots.
        /// </summary>
        private static float _manualSpacingAdjustment;
        private static float _lastBaseSpacing;
        private static float _lastSpacingFloor;

        /// <summary>
        /// How far past our own requirement to look for neighbours. A neighbour of a different species
        /// can demand more room than we do, so the first pass has to be wider than our own need before
        /// the exact pairwise maths can run.
        /// </summary>
        private const float NeighbourScanFactor = 2f;

        /// <summary>
        /// Guards against a grow chain that loops back on itself.
        /// </summary>
        private const int MaxGrowChainDepth = 6;

        /// <summary>
        /// Profiles are fixed per species, but this is consulted every frame the ghosts update, so
        /// measuring colliders or walking the grow chain on each call is not affordable.
        /// Keyed by prefab name so a live plant in the world resolves to the same entry as its prefab.
        /// </summary>
        private static readonly Dictionary<string, PlantProfile> ProfileCache =
            new Dictionary<string, PlantProfile>();

        private static readonly Collider[] NeighbourBuffer = new Collider[128];

        private static readonly int GrowSpaceMask =
            LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");

        /// <summary>
        /// The worst case a plant will ever present to its neighbours, over its whole life.
        /// </summary>
        private struct PlantProfile
        {
            /// <summary>Largest radius this plant will ever sweep looking for space.</summary>
            public float Radius;

            /// <summary>Largest XZ reach its colliders will ever have, fully grown and at max scale.</summary>
            public float Footprint;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlantingUtil), "PatternSpacing")]
        private static void PatternSpacingPostfix(float plantGrowthRadius, ref float __result)
        {
            float floor = SpacingFloor(GetSelectedPlantPrefab(), plantGrowthRadius);

            _lastSpacingFloor = floor;
            _lastBaseSpacing = __result;
            __result = Mathf.Max(__result + _manualSpacingAdjustment, floor);
        }

        /// <summary>
        /// The tightest spacing the growth tick will tolerate for this plant, whatever the pattern.
        /// Plant.HaveGrowSpace sweeps m_growRadius against other plants and, separately, m_growRadiusVines
        /// against vines - so the larger of the two is the worst case the plant can be judged against.
        /// Adding the collider footprint covers the neighbour's own bulk, since OverlapSphere tests
        /// colliders rather than centre points.
        /// </summary>
        /// <param name="prefab">The plant prefab being placed</param>
        /// <param name="fallbackRadius">Radius to use when the prefab carries no Plant component</param>
        /// <returns>Minimum centre-to-centre distance between two of these plants</returns>
        private static float SpacingFloor(GameObject prefab, float fallbackRadius)
        {
            Plant plant = prefab ? prefab.GetComponent<Plant>() : null;
            if (!plant)
            {
                return fallbackRadius;
            }

            PlantProfile profile = ProfileFor(prefab, plant);
            return profile.Radius + profile.Footprint + SpacingEpsilon + PatternMargin;
        }

        /// <summary>
        /// Start a fresh run of placements. Called at the top of both the ghost preview and the
        /// planting loop so neither inherits the other's accepted positions.
        /// </summary>
        internal static void BeginPlacementBatch()
        {
            PendingPositions.Clear();
        }

        /// <summary>
        /// Record a position that has passed its checks and will be planted, so the positions tested
        /// after it have to clear it the same way they would a plant already in the ground.
        /// </summary>
        /// <param name="position">An accepted planting position</param>
        internal static void AddPlacement(Vector3 position)
        {
            PendingPositions.Add(position);
        }

        /// <summary>
        /// Clearance has to work both ways. Each plant runs its own HaveGrowSpace, so a position is
        /// only safe if it satisfies our sweep against the neighbour's bulk AND the neighbour's sweep
        /// against ours. Planting next to an existing crop was invalidating that crop rather than the
        /// new one, because only our own requirement was being honoured.
        /// Sizes are taken fully grown on both sides, since the growth tick keeps re-checking.
        /// </summary>
        /// <param name="position">Candidate planting position</param>
        /// <returns>False if any nearby plant and this one cannot both have room</returns>
        private static bool HasMutualClearance(Vector3 position)
        {
            GameObject prefab = GetSelectedPlantPrefab();
            Plant ourPlant = prefab ? prefab.GetComponent<Plant>() : null;
            if (!ourPlant)
            {
                return true;
            }

            PlantProfile ours = ProfileFor(prefab, ourPlant);

            // Wide first pass. We cannot know a neighbour's requirement until we have found it, so cast
            // past our own and let the pairwise test below reject what actually conflicts.
            float scan = (ours.Radius + ours.Footprint) * NeighbourScanFactor;
            int hits = Physics.OverlapSphereNonAlloc(position, scan, NeighbourBuffer, GrowSpaceMask);

            for (int i = 0; i < hits; i++)
            {
                Collider hit = NeighbourBuffer[i];
                if (!hit)
                {
                    continue;
                }

                Plant neighbour = hit.GetComponentInParent<Plant>();
                if (!neighbour)
                {
                    continue;
                }

                PlantProfile theirs = ProfileFor(neighbour.gameObject, neighbour);
                float needed = Mathf.Max(theirs.Radius + ours.Footprint, ours.Radius + theirs.Footprint) +
                               SpacingEpsilon;

                Vector3 delta = neighbour.transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < needed * needed)
                {
                    return false;
                }
            }

            // Everything already accepted in this batch is the same species as us, so both sides of
            // the mutual test collapse to the same number.
            float sameSpecies = ours.Radius + ours.Footprint + SpacingEpsilon;
            for (int i = 0; i < PendingPositions.Count; i++)
            {
                Vector3 delta = PendingPositions[i] - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < sameSpecies * sameSpecies)
                {
                    return false;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CropUtils), nameof(CropUtils.ChangeSpacing))]
        private static bool ChangeSpacingPrefix(float spacingChange)
        {
            // Stop the nudge accumulating below the floor. Without this, holding "-" would bank a large
            // negative that does nothing visible, and "+" would then take just as many presses to undo.
            float smallestUsefulAdjustment = _lastSpacingFloor - _lastBaseSpacing;
            _manualSpacingAdjustment =
                Mathf.Max(smallestUsefulAdjustment, _manualSpacingAdjustment + spacingChange);

            float effective = Mathf.Max(_lastBaseSpacing + _manualSpacingAdjustment, _lastSpacingFloor);
            CropUtils.Log.LogInfo($"Spacing is now {effective:0.###}");
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlantingUtil), "HasGrowSpace")]
        private static void HasGrowSpacePrefix()
        {
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Folded in here so every caller - the pattern ghosts, the actual planting loop and the origin
        /// warning - gets the same answer without each having to remember to ask separately.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlantingUtil), "HasGrowSpace")]
        private static void HasGrowSpacePostfix(Vector3 newPos, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            __result = HasMutualClearance(newPos);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        private static void SetupPlacementGhostPrefix()
        {
            StopHexBuild();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static void UpdatePlacementGhostPostfix(Player __instance)
        {
            if (PlacementGhostField == null)
            {
                return;
            }

            GameObject ghost = PlacementGhostField.GetValue(__instance) as GameObject;
            if (!ghost || !ghost.activeSelf)
            {
                StopHexBuild();
            }
        }

        private static void StopHexBuild()
        {
            if (HexListCoroutineField == null || !CropUtils.Instance)
            {
                return;
            }

            Coroutine coroutine = HexListCoroutineField.GetValue(null) as Coroutine;
            if (coroutine == null)
            {
                return;
            }

            CropUtils.Instance.StopCoroutine(coroutine);
            HexListCoroutineField.SetValue(null, null);
        }

        private static GameObject GetSelectedPlantPrefab()
        {
            Player player = Player.m_localPlayer;
            if (!player || BuildPiecesField == null)
            {
                return null;
            }

            PieceTable pieceTable = BuildPiecesField.GetValue(player) as PieceTable;
            return pieceTable != null ? pieceTable.GetSelectedPrefab() : null;
        }

        /// <summary>
        /// Largest distance from the plant's root Y axis to any point of its grow-space colliders,
        /// measured in root-local XZ. Because it is measured about the root axis this is the same
        /// whatever yaw the plant ends up with, so a rotated neighbour can never reach further than this.
        /// Uses bounding-box corners rather than the exact collider, which overestimates for
        /// non-square footprints - deliberately, so spacing errs wide instead of tight.
        /// </summary>
        /// <param name="prefab">The plant prefab being placed</param>
        /// <returns>Footprint radius in metres</returns>
        /// <summary>
        /// Worst case this species will ever present, walking the whole grow chain.
        /// A sapling is not what a neighbour has to live beside: Plant.UpdateHealth re-runs
        /// HaveGrowSpace on every growth tick, and by then the plant may be several stages on. Each
        /// stage can be a Plant in its own right with a larger grow radius, and Plant.Grow scales the
        /// new object by a random value up to m_maxScale, which scales its colliders with it.
        /// </summary>
        /// <param name="root">Prefab or live instance to profile</param>
        /// <param name="plant">Its Plant component</param>
        /// <returns>The largest radius and footprint it will ever have</returns>
        private static PlantProfile ProfileFor(GameObject root, Plant plant)
        {
            string key = CleanName(root.name);
            if (ProfileCache.TryGetValue(key, out PlantProfile cached))
            {
                return cached;
            }

            PlantProfile profile = Accumulate(root, plant, 1f, 0);
            ProfileCache[key] = profile;
            CropUtils.Log.LogInfo(
                $"[CropUtils] {key} worst case radius {profile.Radius:0.###}, footprint {profile.Footprint:0.###}");
            return profile;
        }

        private static PlantProfile Accumulate(GameObject root, Plant plant, float scale, int depth)
        {
            PlantProfile profile;
            // m_growRadius is passed to OverlapSphere unscaled, so unlike the colliders it does not
            // grow with the object's scale.
            profile.Radius = plant ? Mathf.Max(plant.m_growRadius, plant.m_growRadiusVines) : 0f;
            profile.Footprint = MeasureFootprint(root) * scale;

            if (!plant || plant.m_grownPrefabs == null || depth >= MaxGrowChainDepth)
            {
                return profile;
            }

            float grownScale = scale * Mathf.Max(1f, plant.m_maxScale);
            foreach (GameObject grown in plant.m_grownPrefabs)
            {
                if (!grown)
                {
                    continue;
                }

                PlantProfile next = Accumulate(grown, grown.GetComponent<Plant>(), grownScale, depth + 1);
                profile.Radius = Mathf.Max(profile.Radius, next.Radius);
                profile.Footprint = Mathf.Max(profile.Footprint, next.Footprint);
            }

            return profile;
        }

        private static string CleanName(string name)
        {
            int clone = name.IndexOf("(Clone)");
            return clone >= 0 ? name.Substring(0, clone) : name;
        }

        /// <summary>
        /// Worst-case XZ reach of one prefab's grow-space colliders, measured about its own root axis.
        /// </summary>
        /// <param name="prefab">Prefab to measure</param>
        /// <returns>Footprint radius in metres</returns>
        private static float MeasureFootprint(GameObject prefab)
        {
            Transform root = prefab.transform;
            float maxRadiusSquared = 0f;

            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (!collider || !collider.enabled || !IsGrowSpaceLayer(collider.gameObject.layer))
                {
                    continue;
                }

                if (!TryGetLocalBounds(collider, out Bounds localBounds))
                {
                    continue;
                }

                Matrix4x4 colliderToRoot = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 corner = new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z);
                            Vector3 rootPoint = colliderToRoot.MultiplyPoint3x4(corner);
                            float radiusSquared = rootPoint.x * rootPoint.x + rootPoint.z * rootPoint.z;
                            maxRadiusSquared = Mathf.Max(maxRadiusSquared, radiusSquared);
                        }
                    }
                }
            }

            return Mathf.Sqrt(maxRadiusSquared);
        }

        private static bool IsGrowSpaceLayer(int layer)
        {
            return (GrowSpaceMask & (1 << layer)) != 0;
        }

        private static bool TryGetLocalBounds(Collider collider, out Bounds bounds)
        {
            if (collider is BoxCollider box)
            {
                bounds = new Bounds(box.center, box.size);
                return true;
            }

            if (collider is SphereCollider sphere)
            {
                bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
                return true;
            }

            if (collider is CapsuleCollider capsule)
            {
                float radius = capsule.radius;
                float halfHeight = Mathf.Max(capsule.height * 0.5f, radius);
                Vector3 extents = Vector3.one * radius;
                if (capsule.direction == 0)
                {
                    extents.x = halfHeight;
                }
                else if (capsule.direction == 1)
                {
                    extents.y = halfHeight;
                }
                else
                {
                    extents.z = halfHeight;
                }

                bounds = new Bounds(capsule.center, extents * 2f);
                return true;
            }

            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh)
            {
                bounds = meshCollider.sharedMesh.bounds;
                return true;
            }

            bounds = default;
            return false;
        }
    }
}

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
        /// Runtime +/- is a nudge applied on top of whatever the config works out for the current
        /// plant, not a fixed distance. Keeping it relative means it rebases when you switch plants,
        /// so a wide tree adjustment does not follow you back to carrots.
        /// </summary>
        private static float _manualSpacingAdjustment;
        private static float _lastBaseSpacing;
        private static float _lastSpacingFloor;

        /// <summary>
        /// Footprint is a fixed property of the prefab, but PatternSpacing runs every frame the ghosts
        /// update, so measuring it on each call would walk every child collider per frame.
        /// </summary>
        private static readonly Dictionary<GameObject, float> FootprintCache =
            new Dictionary<GameObject, float>();

        private static readonly int GrowSpaceMask =
            LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");

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
            if (!prefab)
            {
                return fallbackRadius;
            }

            float radius = fallbackRadius;
            Plant plant = prefab.GetComponent<Plant>();
            if (plant)
            {
                radius = Mathf.Max(plant.m_growRadius, plant.m_growRadiusVines);
            }

            return radius + HorizontalColliderRadius(prefab) + SpacingEpsilon;
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
        private static float HorizontalColliderRadius(GameObject prefab)
        {
            if (FootprintCache.TryGetValue(prefab, out float cached))
            {
                return cached;
            }

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

            float radius = Mathf.Sqrt(maxRadiusSquared);
            FootprintCache[prefab] = radius;
            return radius;
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

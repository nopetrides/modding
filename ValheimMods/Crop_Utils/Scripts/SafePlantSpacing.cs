using HarmonyLib;
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
        private static readonly MethodInfo HasGrowSpaceMethod = AccessTools.Method(typeof(PlantingUtil), "HasGrowSpace");
        private static readonly MethodInfo CanGrowAtMethod = AccessTools.Method(typeof(PlantingUtil), "CanGrowAt");

        private static float? _manualSpacingOverride;
        private static float _lastEffectiveSpacing;
        private static float _lastSpacingFloor;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlantingUtil), "PatternSpacing")]
        private static void PatternSpacingPostfix(float plantGrowthRadius, ref float __result)
        {
            GameObject prefab = GetSelectedPlantPrefab();
            float floor = plantGrowthRadius;

            if (prefab)
            {
                float colliderRadius = HorizontalColliderRadius(prefab);
                if (colliderRadius > 0f)
                {
                    // Plant.HaveGrowSpace sweeps plantGrowthRadius from the plant root. Keeping the
                    // neighbouring plant's complete horizontal footprint outside that sweep is the
                    // pattern-level floor. Actual candidate validity is still checked separately.
                    floor = plantGrowthRadius + colliderRadius;
                }
            }

            _lastSpacingFloor = floor;
            __result = Mathf.Max(_manualSpacingOverride ?? __result, floor);
            _lastEffectiveSpacing = __result;
        }

        /// <summary>
        /// The +/- keys are a runtime spacing override. The first press starts from the spacing that is
        /// actually on screen, rather than from the otherwise-unused custom-spacing config value.
        /// Further presses always move by the requested amount until the prefab-derived floor is reached.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CropUtils), nameof(CropUtils.ChangeSpacing))]
        private static bool ChangeSpacingPrefix(float spacingChange)
        {
            float current = _manualSpacingOverride ??
                            (_lastEffectiveSpacing > 0f ? _lastEffectiveSpacing : CropUtils.Instance.CustomSpacing);
            _manualSpacingOverride = Mathf.Max(_lastSpacingFloor, current + spacingChange);
            CropUtils.Log.LogInfo($"Spacing is now {_manualSpacingOverride.Value:0.###}");
            return false;
        }

        /// <summary>
        /// Sync before every grow-space query. Batch planting creates and moves colliders several times
        /// in one frame; without a sync the next candidate can query stale physics state. This also lets
        /// the normal 3D OverlapSphere make the final decision on vertically uneven cultivated ground.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlantingUtil), "HasGrowSpace")]
        private static void HasGrowSpacePrefix()
        {
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Valheim validates the original clicked plant before CropUtils places the rest of a pattern,
        /// but it does not include CropUtils' grow-health checks. While a utility planting hotkey is held,
        /// reject that first plant too if the same checks used for generated candidates fail.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        private static bool TryPlacePiecePrefix(Player __instance, ref bool __result)
        {
            if (!UtilityPlantingHeld() || PlacementGhostField == null)
            {
                return true;
            }

            GameObject ghost = PlacementGhostField.GetValue(__instance) as GameObject;
            if (!ghost || !ghost.activeSelf)
            {
                return true;
            }

            Piece piece = ghost.GetComponent<Piece>();
            if (!piece)
            {
                return true;
            }

            float growRadius = PlantingUtil.TryFindPlantGrowthRadius(ghost);
            if (growRadius <= 0f)
            {
                return true;
            }

            Vector3 position = ghost.transform.position;
            Heightmap heightmap = Heightmap.FindHeightmap(position);
            bool validSurface = heightmap != null &&
                                (!piece.m_cultivatedGroundOnly || heightmap.IsCultivated(position));
            bool canGrow = CanGrowAtMethod != null &&
                           (bool)CanGrowAtMethod.Invoke(null, new object[] { ghost, position });
            bool hasSpace = HasGrowSpaceMethod != null &&
                            (bool)HasGrowSpaceMethod.Invoke(null, new object[] { position, growRadius });

            if (validSurface && canGrow && hasSpace)
            {
                return true;
            }

            piece.SetInvalidPlacementHeightlight(true);
            __result = false;
            return false;
        }

        /// <summary>
        /// Radius generation is intentionally incremental. If the cultivator/toolbar selection changes,
        /// its placement Transform can be destroyed while the coroutine is still yielding. Cancel it as
        /// soon as a new placement ghost is set up so BuildHexList cannot resume against a dead origin.
        /// </summary>
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

        private static bool UtilityPlantingHeld()
        {
            return Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) ||
                   Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey);
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
        /// Returns the radius of a circle, centred on the plant root, containing the XZ projection of
        /// every enabled grow-space collider bound. World orientation is deliberately never used.
        /// </summary>
        private static float HorizontalColliderRadius(GameObject prefab)
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
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            return (mask & (1 << layer)) != 0;
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

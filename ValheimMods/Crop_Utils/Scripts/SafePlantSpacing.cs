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
        private static readonly MethodInfo FindGrowRadiusMethod = AccessTools.Method(typeof(PlantingUtil), "TryFindPlantGrowthRadius");

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
                    floor = plantGrowthRadius + colliderRadius;
                }
            }

            _lastSpacingFloor = floor;
            __result = Mathf.Max(_manualSpacingOverride ?? __result, floor);
            _lastEffectiveSpacing = __result;
        }

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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlantingUtil), "HasGrowSpace")]
        private static void HasGrowSpacePrefix()
        {
            Physics.SyncTransforms();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        private static bool TryPlacePiecePrefix(Player __instance, ref bool __result)
        {
            if (!UtilityPlantingHeld() || PlacementGhostField == null || FindGrowRadiusMethod == null)
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

            float growRadius = (float)FindGrowRadiusMethod.Invoke(null, new object[] { ghost });
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

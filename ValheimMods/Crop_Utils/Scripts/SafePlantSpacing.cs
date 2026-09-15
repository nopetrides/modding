using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Crop_Utils
{
    /// <summary>
    /// Keeps the configurable pattern spacing, but prevents it from becoming smaller than the
    /// selected plant prefab can safely support. The footprint is measured in prefab/root-local XZ,
    /// so terrain alignment or placement-preview pitch/roll cannot inflate or rotate the measurement.
    /// </summary>
    [HarmonyPatch]
    internal static class SafePlantSpacing
    {
        private static readonly FieldInfo BuildPiecesField = AccessTools.Field(typeof(Player), "m_buildPieces");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlantingUtil), "PatternSpacing")]
        private static void PatternSpacingPostfix(float plantGrowthRadius, ref float __result)
        {
            GameObject prefab = GetSelectedPlantPrefab();
            if (!prefab)
            {
                return;
            }

            float colliderRadius = HorizontalColliderRadius(prefab);
            if (colliderRadius <= 0f)
            {
                return;
            }

            // Plant.HaveGrowSpace sweeps plantGrowthRadius from this plant's centre. Keeping the next
            // plant's complete collider footprint outside that circle guarantees same-crop neighbours
            // are not generated inside the grow-space query solely because the configured multiplier
            // is too small.
            __result = Mathf.Max(__result, plantGrowthRadius + colliderRadius);
        }

        private static GameObject GetSelectedPlantPrefab()
        {
            Player player = Player.m_localPlayer;
            if (!player || BuildPiecesField == null)
            {
                return null;
            }

            PieceTable pieceTable = BuildPiecesField.GetValue(player) as PieceTable;
            return pieceTable ? pieceTable.GetSelectedPrefab() : null;
        }

        /// <summary>
        /// Returns the radius of a circle, centred on the plant root, that contains the XZ projection
        /// of every relevant collider bound on the prefab. Collider-local bounds are transformed into
        /// root-local space; world orientation is deliberately never used.
        /// </summary>
        private static float HorizontalColliderRadius(GameObject prefab)
        {
            Transform root = prefab.transform;
            float maxRadiusSquared = 0f;

            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (!collider || !IsGrowSpaceLayer(collider.gameObject.layer))
                {
                    continue;
                }

                if (!TryGetLocalBounds(collider, out Bounds localBounds))
                {
#if LOGGING
                    CropUtils.Log.LogWarning($"Unsupported collider {collider.GetType().Name} on {prefab.name}; ignoring it for safe spacing.");
#endif
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

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Crop_Utils
{
    [HarmonyPatch]
    internal class PlantingUtil
    {
        /// <summary>
        /// The layer mask for planting
        /// </summary>
        private static int _plantSpaceMask = LayerMask.GetMask(new string[]
        {
            "Default",
            "static_solid",
            "Default_small",
            "piece",
            "piece_nonsolid"
        }); 
        /// <summary>
        /// A placeholder piece used when doing other operations
        /// </summary>
        private static Piece _fakeResourcePiece = new Piece
        {
            m_dlc = string.Empty,
            m_resources = new Piece.Requirement[]
            {
                new Piece.Requirement()
            }
        };

        /// <summary>
        /// Did we complete the placement of the first real plant item
        /// </summary>
        private static bool placed = false;

        /// <summary>
        /// References to all the ghost objects
        /// </summary>
        private static GameObject[] _placementGhosts = new GameObject[1];
        /// <summary>
        /// Use reflection to get the placement ghost field
        /// </summary>
        private static FieldInfo _placementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        /// <summary>
        /// Use reflection to get the buildable field
        /// </summary>
        private static FieldInfo _buildPiecesField = AccessTools.Field(typeof(Player), "m_buildPieces");
        /// <summary>
        /// Similar to placement ghost field, but just the field for no cost
        /// </summary>
        private static FieldInfo _noPlacementCostField = AccessTools.Field(typeof(Player), "m_noPlacementCost");


        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "PlacePiece")]
        public static void PlantItemPostFix(Player __instance, ref bool __result, Piece piece)
        {
            placed = __result;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPrefix(bool takeInput, float dt)
        {
            placed = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPostfix(Player __instance, bool takeInput, float dt)
        {
            if (!placed)
            {
                return;
            }
        }

        /// <summary>
        /// Returns the coordinates for where the planting util would place extra plants
        /// </summary>
        /// <returns></returns>
        private static List<Vector3> BuildPlantingPositions(Vector3 originPos, Plant toPlant, Quaternion rotation)
        {
            float growthDiameter = toPlant.m_growRadius * 2f;

            int expectedQuantityOfGhosts = Mathf.FloorToInt(CropUtils.Instance.UtilRange / growthDiameter);

            List<Vector3> positionList = new List<Vector3>(expectedQuantityOfGhosts);

            Vector3 distanceBetween = rotation * Vector3.forward * growthDiameter;
            Vector3 nextPosition = originPos + distanceBetween;

            for (int i = 0; i < expectedQuantityOfGhosts; i++)
            {
                nextPosition.y = ZoneSystem.instance.GetGroundHeight(nextPosition);
                positionList.Add(nextPosition);
                nextPosition += distanceBetween;
            }

            return positionList;
        }

        /// <summary>
        /// Would this be a valid place to plant
        /// </summary>
        /// <param name="newPos"></param>
        /// <param name="go"></param>
        /// <returns></returns>
        private static bool HasGrowSpace(Vector3 newPos, GameObject go)
        {
            Plant component = go.GetComponent<Plant>();
            return component == null || Physics.OverlapSphere(newPos, component.m_growRadius, _plantSpaceMask).Length == 0;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        public static void SetupPlacementGhostPostfix(Player __instance)
        {
            DestroyGhosts();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        public static void UpdatePlacementGhostPostfix(Player __instance, bool flashGuardStone)
        {
            GameObject gameObject = (GameObject)_placementGhostField.GetValue(__instance);
            if (!gameObject || !gameObject.activeSelf)
            {
                SetGhostsActive(false);
                return;
            }
            if (!Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) &&
                !Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey))
            {
                SetGhostsActive(false);
                return;
            }
            Plant plantComponent = gameObject.GetComponent<Plant>();
            if (!plantComponent)
            {
                SetGhostsActive(false);
                return;
            }
            // Allow for distance adjustment while in this mode
            if (Input.GetKey(CropUtils.Instance.IncreaseRangeControllerButton.MainKey) || Input.GetKey(CropUtils.Instance.IncreaseRangeHotKey.MainKey))
            {
                CropUtils.Instance.ChangeRange(1);
            }
            if (Input.GetKey(CropUtils.Instance.DecreaseRangeControllerButton.MainKey) || Input.GetKey(CropUtils.Instance.DecreaseRangeHotKey.MainKey))
            {
                CropUtils.Instance.ChangeRange(-1);
            }
            // Ensure the ghosts list is ready
            if (!DidGhostsBuild(__instance, plantComponent))
            {
                SetGhostsActive(false);
                return;
            }


            // Do the actual ghost creation
            Piece.Requirement requirement = gameObject.GetComponent<Piece>().m_resources.FirstOrDefault((Piece.Requirement r) => r.m_resItem && r.m_amount > 0);

            _fakeResourcePiece.m_resources[0].m_resItem = requirement.m_resItem;
            _fakeResourcePiece.m_resources[0].m_amount = requirement.m_amount;
            float staminaCost = __instance.GetStamina();
            ItemDrop.ItemData itemData = __instance.GetRightItem();
            List<Vector3> list = BuildPlantingPositions(gameObject.transform.position, plantComponent, gameObject.transform.rotation);
            for (int i = 0; i < _placementGhosts.Length; i++)
            {
                Vector3 ghostPosition = list[i];
                if (gameObject.transform.position == ghostPosition)
                {
                    _placementGhosts[i].SetActive(false);
                }
                else
                {
                    _fakeResourcePiece.m_resources[0].m_amount += requirement.m_amount;
                    _placementGhosts[i].transform.position = ghostPosition;
                    _placementGhosts[i].transform.rotation = gameObject.transform.rotation;
                    _placementGhosts[i].SetActive(true);
                    bool invalidPlacementHighlight = false;
                    Heightmap heightmap = Heightmap.FindHeightmap(ghostPosition);
                    if (gameObject.GetComponent<Piece>().m_cultivatedGroundOnly && !heightmap.IsCultivated(ghostPosition))
                    {
                        invalidPlacementHighlight = true;
                    }
                    else if (!HasGrowSpace(ghostPosition, gameObject.gameObject))
                    {
                        invalidPlacementHighlight = true;
                    }
                    else if (staminaCost < itemData.m_shared.m_attack.m_attackStamina)
                    {
                        Hud.instance.StaminaBarNoStaminaFlash();
                        invalidPlacementHighlight = true;
                    }
                    else if (!(bool)_noPlacementCostField.GetValue(__instance) && !__instance.HaveRequirements(_fakeResourcePiece, 0))
                    {
                        invalidPlacementHighlight = true;
                    }
                    staminaCost -= itemData.m_shared.m_attack.m_attackStamina;
                    _placementGhosts[i].GetComponent<Piece>().SetInvalidPlacementHeightlight(invalidPlacementHighlight);
                }
            }
        }

        /// <summary>
        /// Check if we already built the ghosts
        /// </summary>
        /// <param name="player"></param>
        /// <param name="toPlant"></param>
        /// <returns></returns>
        private static bool DidGhostsBuild(Player player, Plant toPlant)
        {
            int expectedQuantityOfGhosts = Mathf.CeilToInt(CropUtils.Instance.UtilRange / (toPlant.m_growRadius * 2f));
            if (!_placementGhosts[0] || _placementGhosts.Length != expectedQuantityOfGhosts)
            {
                DestroyGhosts();
                if (_placementGhosts.Length != expectedQuantityOfGhosts)
                {
                    _placementGhosts = new GameObject[expectedQuantityOfGhosts];
                }
                PieceTable pieceTable = _buildPiecesField.GetValue(player) as PieceTable;
                if (pieceTable != null)
                {
                    GameObject selectedPrefab = pieceTable.GetSelectedPrefab();
                    if (selectedPrefab != null)
                    {
                        if (selectedPrefab.GetComponent<Piece>().m_repairPiece)
                        {
                            return false;
                        }
                        for (int i = 0; i < _placementGhosts.Length; i++)
                        {
                            _placementGhosts[i] = SetupNewGhost(player, selectedPrefab);
                        }
                    }
                }

                return false;
            }
            return true;
        }

        /// <summary>
        /// Clear all the ghosts objects
        /// </summary>
        private static void DestroyGhosts()
        {
            for (int i = 0; i < _placementGhosts.Length; i++)
            {
                if (_placementGhosts[i])
                {
                    Object.Destroy(_placementGhosts[i]);
                    _placementGhosts[i] = null;
                }
            }
        }

        /// <summary>
        /// Enable the ghosts objects
        /// </summary>
        /// <param name="active"></param>
        private static void SetGhostsActive(bool active)
        {
            foreach (GameObject gameObject in _placementGhosts)
            {
                if (gameObject != null)
                {
                    gameObject.SetActive(active);
                }
            }
        }

        /// <summary>
        /// Create a new ghost object
        /// </summary>
        /// <param name="player"></param>
        /// <param name="prefab"></param>
        /// <returns></returns>
        private static GameObject SetupNewGhost(Player player, GameObject prefab)
        {
            ZNetView.m_forceDisableInit = true;
            GameObject gameObject = Object.Instantiate(prefab);
            ZNetView.m_forceDisableInit = false;
            gameObject.name = prefab.name;

            DestroyNonGhostComponents(gameObject);

            Transform transform = gameObject.transform.Find("_GhostOnly");
            if (transform)
            {
                transform.gameObject.SetActive(true);
            }
            foreach (MeshRenderer meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>())
            {
                if (!(meshRenderer.sharedMaterial == null))
                {
                    Material[] sharedMaterials = meshRenderer.sharedMaterials;
                    for (int j = 0; j < sharedMaterials.Length; j++)
                    {
                        Material material = new Material(sharedMaterials[j]);
                        material.SetFloat("_RippleDistance", 0f);
                        material.SetFloat("_ValueNoise", 0f);
                        sharedMaterials[j] = material;
                    }
                    meshRenderer.sharedMaterials = sharedMaterials;
                    meshRenderer.shadowCastingMode = 0;
                }
            }
            return gameObject;
        }

        /// <summary>
        /// Removes all the components that may have some interaction we don't want
        /// </summary>
        /// <param name="gameObject"></param>
        private static void DestroyNonGhostComponents(GameObject gameObject)
        {
            Joint[] jointComponents = gameObject.GetComponentsInChildren<Joint>();
            for (int i = 0; i < jointComponents.Length; i++)
            {
                Object.Destroy(jointComponents[i]);
            }
            Rigidbody[] rigidbodyComponents = gameObject.GetComponentsInChildren<Rigidbody>();
            for (int i = 0; i < rigidbodyComponents.Length; i++)
            {
                Object.Destroy(rigidbodyComponents[i]);
            }
            int layer = LayerMask.NameToLayer("ghost");
            Transform[] transformComponents = gameObject.GetComponentsInChildren<Transform>();
            for (int i = 0; i < transformComponents.Length; i++)
            {
                transformComponents[i].gameObject.layer = layer;
            }
            TerrainModifier[] terrainModifierComponents = gameObject.GetComponentsInChildren<TerrainModifier>();
            for (int i = 0; i < terrainModifierComponents.Length; i++)
            {
                Object.Destroy(terrainModifierComponents[i]);
            }
            GuidePoint[] guidePointComponents = gameObject.GetComponentsInChildren<GuidePoint>();
            for (int i = 0; i < guidePointComponents.Length; i++)
            {
                Object.Destroy(guidePointComponents[i]);
            }
            Light[] lightComponents = gameObject.GetComponentsInChildren<Light>();
            for (int i = 0; i < lightComponents.Length; i++)
            {
                Object.Destroy(lightComponents[i]);
            }
        }
    }
}

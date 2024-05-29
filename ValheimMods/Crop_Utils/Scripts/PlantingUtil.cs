//#define LOGGING

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static ItemDrop;
using Object = UnityEngine.Object;


namespace Crop_Utils
{
    /// <summary>
    /// Class for managing the util for planting multiple crops at once
    /// 1.0.0 has two planting options - Line and Hex
    /// </summary>
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
        private static bool _placed = false;

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

        /// <summary>
        /// Where planting succeeded
        /// </summary>
        private static Transform _placedPosition;
        /// <summary>
        /// How the item was rotated when planted
        /// </summary>
        private static Quaternion _placedRotation;
        /// <summary>
        /// What was placed
        /// </summary>
        private static Piece _placedPiece;

        /// <summary>
        /// Cache of last time the list was generated
        /// </summary>
        private static List<Vector3> _lastPlantedPosition = null;
        /// <summary>
        /// Stored position of the player's main ghost item
        /// </summary>
        private static Vector3 _lastPlayerGhostPosition;
        /// <summary>
        /// Reference to the running routine generating the hex grid
        /// </summary>
        private static Coroutine _hexListCoroutine = null;

        /// <summary>
        /// Immediately aftter a player tried to place a piece
        /// </summary>
        /// <param name="__instance">Reference to this player</param>
        /// <param name="__result">Did the player successfully place the item</param>
        /// <param name="piece">What item got placed</param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "PlacePiece")]
        public static void PlacePiecePostFix(Player __instance, ref bool __result, Piece piece)
        {
            _placed = __result;
            if (__result)
            {
                GameObject gameObject = (GameObject)_placementGhostField.GetValue(__instance);
                _placedPosition = gameObject.transform;
                _placedRotation = gameObject.transform.rotation;
                _placedPiece = piece;
            }
        }
        /// <summary>
        /// Before handling the placement, ensure we know we have not yet placed anything
        /// </summary>
        /// <param name="takeInput"></param>
        /// <param name="dt"></param>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPrefix(bool takeInput, float dt)
        {
            _placed = false;
        }

        /// <summary>
        /// After handling the placement of the original piece, we can then place all extra pieces.
        /// </summary>
        /// <param name="__instance">Reference to this player</param>
        /// <param name="takeInput"></param>
        /// <param name="dt"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPostfix(Player __instance, bool takeInput, float dt)
        {
            if (!_placed)
            {
                return;
            }
            PlaceNextFrame(__instance);
        }

        /// <summary>
        /// Handle all the logic behind needed to place extra crops or not
        /// </summary>
        /// <param name="__instance"></param>
        private static void PlaceNextFrame(Player __instance)
        {
            float plantGrowthRadius = TryFindPlantGrowthRadius(_placedPiece.gameObject);
            if (plantGrowthRadius <= 0) 
            {
                return;
            }
            
            if (!Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) &&
                !Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey))
            {
                return;
            }

            List <Vector3> newPlantPositions = BuildPlantingPositions(_placedPosition, plantGrowthRadius);
            #if LOGGING 
            CropUtils.Log.LogInfo($"Planting {newPlantPositions.Count} new plants..."); 
            #endif
            int plantSuccesses = 0;

            // Get the tool in hand
            ItemData equippedTool = __instance.GetRightItem();
            float staminaCost = equippedTool.m_shared.m_attack.m_attackStamina / CropUtils.Instance.Discount;
            float durabilityCost = equippedTool.m_shared.m_attack.m_attackStamina / CropUtils.Instance.Discount;

            foreach (Vector3 plantPosition in newPlantPositions)
            {
                #if LOGGING
                CropUtils.Log.LogInfo($"Planting at position {plantPosition.x}, {plantPosition.y}, {plantPosition.z} ");
                #endif
                if ((_placedPosition.position == plantPosition))
                {
                    CropUtils.Log.LogError("Did not plant: Position matches origin");
                    continue;
                }
                //CropUtils.Log.LogInfo(1);
                Heightmap heightmap = Heightmap.FindHeightmap(plantPosition);
                if (heightmap == null || (_placedPiece.m_cultivatedGroundOnly && !heightmap.IsCultivated(plantPosition)))
                {
                    #if LOGGING
                    CropUtils.Log.LogInfo($"Did not plant: Not valid plant surface. Needs cultived {_placedPiece.m_cultivatedGroundOnly}, On cultivated {heightmap.IsCultivated(plantPosition)}");
                    #endif
                    continue;
                }
                //CropUtils.Log.LogInfo(2);
                if (!__instance.HaveStamina(staminaCost))
                {
                    Hud.instance.StaminaBarEmptyFlash();
                    #if LOGGING
                    CropUtils.Log.LogInfo("Did not plant: Not Enough Stamina");
                    #endif
                    break;
                }
                //CropUtils.Log.LogInfo(3);
                if (!(bool)_noPlacementCostField.GetValue(__instance) && !__instance.HaveRequirements(_placedPiece, 0))
                {
                    #if LOGGING
                    CropUtils.Log.LogInfo("Did not plant: Missing Required Items");
                    #endif
                    break;
                }
                //CropUtils.Log.LogInfo(4);
                if (!HasGrowSpace(plantPosition, plantGrowthRadius))
                {
                    #if LOGGING
                    CropUtils.Log.LogInfo("Did not plant: Not enough space");
                    #endif
                    continue;
                }
                //CropUtils.Log.LogInfo(5);
                plantSuccesses++;
                GameObject newPlant = Object.Instantiate(_placedPiece.gameObject, plantPosition, _placedRotation);
                Piece newPlantPiece = newPlant.GetComponent<Piece>();
                if (newPlantPiece)
                {
                    newPlantPiece.SetCreator(__instance.GetPlayerID());
                    newPlantPiece.SetInvalidPlacementHeightlight(false);
                }
                //CropUtils.Log.LogInfo(6);
                // Play placement vfx
                _placedPiece.m_placeEffect.Create(plantPosition, _placedRotation, newPlant.transform, 1f, -1);

                Game.instance.GetPlayerProfile().m_playerStats[PlayerStatType.Builds]++;
                    __instance.ConsumeResources(_placedPiece.m_resources, 0);
                    __instance.UseStamina(staminaCost);

                    //CropUtils.Log.LogInfo(7);
                    // Remove tool durability
                    if (equippedTool.m_shared.m_useDurability)
                {
                    equippedTool.m_durability -= durabilityCost;
                    if (equippedTool.m_durability <= 0f)
                    {
                        break;
                    }
                }
                //CropUtils.Log.LogInfo(8);
            }
            ClearAfterPlanting();
            CropUtils.Log.LogInfo($"Finished planting {plantSuccesses} new crops!");
        }

        /// <summary>
        /// Cleanup any information after doing a plant operation
        /// </summary>
        private static void ClearAfterPlanting()
        {
            if (_hexListCoroutine != null)
            {
                CropUtils.Instance.StopCoroutine(_hexListCoroutine);
            }
            _lastPlayerGhostPosition = Vector3.zero;
            _placedRotation = new Quaternion();
            _lastPlantedPosition = null;
            _placedPiece = null;
            _placementGhosts = new GameObject[1];
            _placed = false;
        }

        /// <summary>
        /// Returns the coordinates for where the planting util would place extra plants
        /// </summary>
        /// <returns></returns>
        private static List<Vector3> BuildPlantingPositions(Transform originPos, float plantGrowthRadius)
        {
            if (Input.GetKey(CropUtils.Instance.UtilAltControllerButton.MainKey) ||
                Input.GetKey(CropUtils.Instance.UtilAltHotKey.MainKey))
            {
                if (_lastPlantedPosition != null)
                {
                    originPos.position = _lastPlayerGhostPosition;
                    UpdatePlayerGhostState(originPos, plantGrowthRadius);
                    return _lastPlantedPosition;
                }
                if (_hexListCoroutine != null)
                {
                    CropUtils.Instance.StopCoroutine(_hexListCoroutine);
                }
                _lastPlayerGhostPosition = originPos.position;
                _lastPlantedPosition = HexPlantPositions(originPos, plantGrowthRadius);
                return _lastPlantedPosition;
            }
            else
            {
                _lastPlayerGhostPosition = originPos.position;
                _lastPlantedPosition = LinePlantPositions(originPos, plantGrowthRadius);
                UpdatePlayerGhostState(originPos, plantGrowthRadius);
                return _lastPlantedPosition;
            }
        }

        /// <summary>
        /// Make sure the placment state is correct
        /// </summary>
        /// <param name="originGhost"></param>
        private static void UpdatePlayerGhostState(Transform originGhost, float plantGrowthRadius)
        {
            bool invalidPlacementHighlight = false;
            // This is the grow check that the game does not do preemptively

            /*if (Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) ||
                Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey))
            {*/
            if (!HasGrowSpace(originGhost.position, plantGrowthRadius))
            {
                invalidPlacementHighlight = true;
                originGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(invalidPlacementHighlight);
            }
            //}
            // Early out if failed check
            if (invalidPlacementHighlight) return;

            // Only check this if we are in the "locked" state
            if (!invalidPlacementHighlight &&
            Input.GetKey(CropUtils.Instance.UtilAltControllerButton.MainKey) ||
            Input.GetKey(CropUtils.Instance.UtilAltHotKey.MainKey))
            {
                if (originGhost.GetComponent<Piece>().m_cultivatedGroundOnly && !Heightmap.FindHeightmap(originGhost.position).IsCultivated(originGhost.position))
                {
                    invalidPlacementHighlight = true;
                }
                originGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(invalidPlacementHighlight);
            }
        }

        /// <summary>
        /// Uses hex grid / circle packing to build a set of positions
        /// </summary>
        /// <param name="originPos"></param>
        /// <param name="toPlant"></param>
        /// <param name="rotation"></param>
        /// <returns></returns>
        private static List<Vector3> HexPlantPositions(Transform originPos, float plantGrowthRadius)
        {
            Vector3 arcDegrees = new Vector3(0, 60, 0);
            int maxDistanceFromOrigin = CropUtils.Instance.UtilRange;
            List<Vector3> hexes = new List<Vector3>();
            float distanceBetween = plantGrowthRadius * 2;
            _hexListCoroutine = CropUtils.Instance.StartCoroutine(BuildHexList(originPos, maxDistanceFromOrigin, hexes, distanceBetween));

#region Previous Attempts
            /*float maxDistanceFromOrigin = CropUtils.Instance.UtilRange;
            float distanceSqrd = maxDistanceFromOrigin * maxDistanceFromOrigin;
            float triangleGridHeight = (toPlant.m_growRadius * Mathf.Sqrt(3) / 2);
            float xDist = triangleGridHeight * 2;
            float zDist = toPlant.m_growRadius;
            Vector3 offset = new Vector3(maxDistanceFromOrigin, 0, maxDistanceFromOrigin);
            Vector3 zeroedOrigin = new Vector3(originPos.x, 0, originPos.z);

            List<Vector3> MakeGrid(Vector3 offset)
            {
                List<Vector3> list = new List<Vector3>();
                for (int z = 0; z < maxDistanceFromOrigin * 2 / zDist; ++z)
                {
                    for (int x = 0; x < maxDistanceFromOrigin * 2 / xDist; ++x)
                    {
                        float px = x * xDist * Mathf.Cos(gridAngle);
                        float pz = z * zDist * Mathf.Sin(gridAngle);
                        Vector3 pos = new Vector3(px, 0, pz);
                        if ((pos - zeroedOrigin).sqrMagnitude < distanceSqrd)
                        {
                            pos += offset;
                            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                            list.Add(pos);
                        }
                        else { CropUtils.Log.LogWarning($"{(pos - zeroedOrigin).sqrMagnitude} not less than {distanceSqrd}"); }
                    }
                }
                return list;
            }

            List<Vector3> pointsA = MakeGrid(zeroedOrigin - offset);
            List<Vector3> pointsB = MakeGrid(zeroedOrigin +  new Vector3(triangleGridHeight, 0, zDist * 0.5f) - offset);

            pointsA.AddRange(pointsB);*/

            /*
                            // 1, 6, 12, 18 . i*6
                            for (int i = 1; i <= maxDistanceFromOrigin; i++)
                        {
                            // Move origin to next row
                            Vector3 rotationVector = currentRotation * Vector3.forward;
                            currentPos += rotationVector;
                            currentPos += currentPos * growthDiameter;
                            currentPos -= rotationVector * 2;
                            hexes.Add(currentPos);

                            // the rest of the row
                            int numberOfHexesInRow = i * 6;
                            int numberOfHexesPerSide = i;
                            int sideCount = 1;
                            for (int hexDrawIndex = 0; hexDrawIndex < numberOfHexesInRow; hexDrawIndex++)
                            {
                                Vector3 distanceBetween = Vector3.forward * growthDiameter;

                                if (sideCount < numberOfHexesPerSide)
                                {
                                    // place
                                    currentPos += distanceBetween;
                                    currentPos.y = ZoneSystem.instance.GetGroundHeight(currentPos);
                                    hexes.Add(currentPos);
                                    sideCount++;
                                }
                                if (sideCount >= numberOfHexesPerSide)
                                {
                                    // rotate to the next side
                                    currentRotation *= rotPerArc;
                                    currentPos += currentRotation * Vector3.forward;
                                    sideCount = 0;
                                }
                            }
                        }*/
#endregion

            //CropUtils.Log.LogInfo("Total positions: " + hexes.Count);
            return hexes;
        }

        /// <summary>
        /// Build the list frame by frame so it doesn't clog the main thread
        /// </summary>
        /// <param name="originPos"></param>
        /// <param name="maxDistanceFromOrigin"></param>
        /// <param name="hexes"></param>
        /// <param name="distanceBetween"></param>
        /// <returns></returns>
        private static IEnumerator BuildHexList(Transform originPos, int maxDistanceFromOrigin, List<Vector3> hexes, float distanceBetween)
        {
            for (int i = 1; i <= maxDistanceFromOrigin; i++)
            {
                int rows = i * 6;
                for (int j = 0; j < rows; j++)
                {
                    float circumferenceProgress = (float)j / rows;

                    float currentRadian = circumferenceProgress * 2 * Mathf.PI;

                    float xNormalized = Mathf.Cos(currentRadian);
                    float zNormalized = Mathf.Sin(currentRadian);

                    float x = xNormalized * distanceBetween * i;
                    float z = zNormalized * distanceBetween * i;

                    Vector3 posInRow = new(x, 0, z);
                    //CropUtils.Log.LogInfo($"New coords {x} , {z}");
                    posInRow += originPos.position;
                    posInRow.y = ZoneSystem.instance.GetGroundHeight(posInRow);
                    hexes.Add(posInRow);
                    yield return null;
                }
                yield return null;
            }
        }


        /// <summary>
        /// Creates a list of positions for planting out in a row from the origin
        /// </summary>
        /// <param name="originPos"></param>
        /// <param name="plantGrowthRadius"></param>
        /// <returns></returns>
        private static List<Vector3> LinePlantPositions(Transform originPos, float plantGrowthRadius)
        {
            float growthDiameter = plantGrowthRadius * 2f;

            int expectedQuantityOfGhosts = Mathf.CeilToInt(CropUtils.Instance.UtilRange / growthDiameter);

            List<Vector3> positionList = new List<Vector3>(expectedQuantityOfGhosts);

            Vector3 distanceBetween = originPos.rotation * Vector3.forward * growthDiameter;
            Vector3 nextPosition = distanceBetween;
            nextPosition += originPos.position;

            for (int i = 0; i < expectedQuantityOfGhosts; i++)
            {
                nextPosition.y = ZoneSystem.instance.GetGroundHeight(nextPosition);
                positionList.Add(nextPosition);
                //CropUtils.Log.LogInfo($" {i} coords {nextPosition.x} , {nextPosition.y} , {nextPosition.z}");
                nextPosition += distanceBetween;
            }

            return positionList;
        }

        /// <summary>
        /// Would this be a valid place to plant
        /// </summary>
        /// <param name="newPos"></param>
        /// <param name="plant"></param>
        /// <returns></returns>
        private static bool HasGrowSpace(Vector3 newPos, float plantGrowthRadius)
        {
            return Physics.OverlapSphere(newPos, plantGrowthRadius, _plantSpaceMask).Length == 0;
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
                #if LOGGING
                CropUtils.Log.LogWarning("No game object in placement field.");
                #endif
                return;
            }
            if (!Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) &&
                !Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey))
            {
                _lastPlantedPosition = null;
                SetGhostsActive(false);
                #if LOGGING
                CropUtils.Log.LogWarning("No hotkey pressed.");
                #endif
                return;
            }

            // Allow for spacing adjustment while in this mode
            if (Input.GetKeyDown(CropUtils.Instance.IncreaseSpacingHotKey.MainKey))
            {
                CropUtils.Instance.ChangeSpacing(0.1f);
            }
            if (Input.GetKeyDown(CropUtils.Instance.DecreaseSpacingHotKey.MainKey))
            {
                CropUtils.Instance.ChangeSpacing(-0.1f);
            }

            float plantGrowthRadius = TryFindPlantGrowthRadius(gameObject);
            if (plantGrowthRadius <= 0)
            {
                return;
            }

            // Allow for distance adjustment while in this mode
            if (Input.GetKeyDown(CropUtils.Instance.IncreaseRangeControllerButton.MainKey) || 
                Input.GetKeyDown(CropUtils.Instance.IncreaseRangeHotKey.MainKey))
            {
                CropUtils.Instance.ChangeRange(1);
            }
            if (Input.GetKeyDown(CropUtils.Instance.DecreaseRangeControllerButton.MainKey) || 
                Input.GetKeyDown(CropUtils.Instance.DecreaseRangeHotKey.MainKey))
            {
                CropUtils.Instance.ChangeRange(-1);
            }

            // Ensure the ghosts list is ready
            if (!DidGhostsBuild(__instance, plantGrowthRadius))
            {
                SetGhostsActive(false);
                return;
            }


            // Do the actual ghost creation
            Piece.Requirement requirement = gameObject.GetComponent<Piece>().m_resources.FirstOrDefault((Piece.Requirement r) => r.m_resItem && r.m_amount > 0);

            _fakeResourcePiece.m_resources[0].m_resItem = requirement.m_resItem;
            _fakeResourcePiece.m_resources[0].m_amount = requirement.m_amount;
            float availableStamina = __instance.GetStamina();
            ItemDrop.ItemData equippedTool = __instance.GetRightItem();
            List<Vector3> list = BuildPlantingPositions(gameObject.transform, plantGrowthRadius);

            //CropUtils.Log.LogInfo($"Placing {_placementGhosts.Length} to {list.Count} positions");
            for (int i = 0; i < _placementGhosts.Length && i < list.Count; i++)
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
                    else if (!HasGrowSpace(ghostPosition, plantGrowthRadius))
                    {
                        invalidPlacementHighlight = true;
                    }
                    else if (availableStamina < equippedTool.m_shared.m_attack.m_attackStamina / CropUtils.Instance.Discount)
                    {
                        Hud.instance.StaminaBarEmptyFlash();
                        invalidPlacementHighlight = true;
                    }
                    else if (!(bool)_noPlacementCostField.GetValue(__instance) && !__instance.HaveRequirements(_fakeResourcePiece, 0))
                    {
                        invalidPlacementHighlight = true;
                    }
                    availableStamina -= equippedTool.m_shared.m_attack.m_attackStamina / CropUtils.Instance.Discount;
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
        private static bool DidGhostsBuild(Player player, float plantGrowthRadius)
        {
            int expectedQuantityOfGhosts;
            expectedQuantityOfGhosts = Mathf.CeilToInt(CropUtils.Instance.UtilRange / (plantGrowthRadius * 2f));
            
            if (Input.GetKey(CropUtils.Instance.UtilAltControllerButton.MainKey) ||
                Input.GetKey(CropUtils.Instance.UtilAltHotKey.MainKey))
            {
                expectedQuantityOfGhosts++;
                expectedQuantityOfGhosts = 3 * (int)Mathf.Pow(expectedQuantityOfGhosts, 2) - (3 * expectedQuantityOfGhosts);
            }
            if (expectedQuantityOfGhosts == 0)
            {
                CropUtils.Log.LogError("Math is wrong, no ghosts to build!");
                return false;
            }
            else
            {
                //CropUtils.Log.LogInfo($"Building {expectedQuantityOfGhosts} ghosts");
            }

            if (!_placementGhosts[0] || _placementGhosts.Length != expectedQuantityOfGhosts)
            {
                DestroyGhosts();
                if (_placementGhosts.Length != expectedQuantityOfGhosts)
                {
                    _placementGhosts = new GameObject[expectedQuantityOfGhosts];
                }
                //CropUtils.Log.LogInfo($"Built {_placementGhosts} ghosts");
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

        /// <summary>
        /// A method to fetch the growth radius.
        /// This may need to support many different mod systems and their growth radius variables.
        /// </summary>
        /// <param name="objectToGrow"></param>
        /// <returns></returns>
        private static float TryFindPlantGrowthRadius(GameObject objectToGrow)
        {
            float plantGrowthRadius = CropUtils.Instance.CustomSpacing;
            if (CropUtils.Instance.AlwaysUseCustomSpacing)
            {
                return plantGrowthRadius;
            }

            Plant plantComp = objectToGrow.GetComponent<Plant>();
            if (!plantComp)
            {
                #if LOGGING
                CropUtils.Log.LogWarning("Unsupported - trying to place an item that is not a <Plant>");
                #endif
                if (CropUtils.Instance.AllowPlantAnything)
                {
                    return plantGrowthRadius;
                }
                else
                {
                    return -1f;
                }
            }
            else
            {
                plantGrowthRadius = plantComp.m_growRadius;
            }
            return plantGrowthRadius;
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace Crop_Utils
{   
    /// <summary>
    /// Class for managing range system.
    /// Range is only shown for pickup functionality, because the range value is shown via ghosts when planting
    /// </summary>
    [HarmonyPatch]
    public static class InteractRangeUtil
    {
        public static GameObject m_rangeIndicator = null;

        /// <summary>
        /// When the player hovers over a interactable game objects
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="hover"></param>
        /// <param name="hoverCreature"></param>
        [HarmonyPatch(typeof(Player), "FindHoverObject")]
        public static void Postfix(Player __instance, ref GameObject hover, ref GameObject hoverCreature)
        {
            bool valid = false;
            if (Player.m_localPlayer != null)
            {
                if (Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) ||
                    Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey))
                {
                    if (CropUtils.Instance.ShowVisualRangeIndicator)
                    {
                        if (m_rangeIndicator == null)
                        {
                            //CropUtils.Log.LogInfo("Creating range indicator");
                            CreateDebugRenderer();
                        }
                        if (hover != null)
                        {
                            Interactable componentInParent = hover.GetComponentInParent<Interactable>();
                            if (componentInParent is Pickable || componentInParent is Beehive)
                            {
                                valid = true;
                                m_rangeIndicator.transform.position = hover.transform.position;
                                m_rangeIndicator.transform.localScale = Vector3.one * CropUtils.Instance.UtilRange;
                            }
                        }
                    }
                    // Allow Range to be changed with keys (while active)
                    if (Input.GetKeyDown(CropUtils.Instance.IncreaseRangeControllerButton.MainKey) || Input.GetKey(CropUtils.Instance.IncreaseRangeHotKey.MainKey))
                    {
                        CropUtils.Instance.ChangeRange(1);
                    }
                    if (Input.GetKeyDown(CropUtils.Instance.DecreaseRangeControllerButton.MainKey) || Input.GetKey(CropUtils.Instance.DecreaseRangeHotKey.MainKey))
                    {
                        CropUtils.Instance.ChangeRange(-1);
                    }
                }
            }
            if (m_rangeIndicator != null)
            {
                m_rangeIndicator.SetActive(valid);
            }
        }

        /// <summary>
        /// Creates the renderer used to show the debug range.
        /// </summary>
        private static void CreateDebugRenderer()
        {
            m_rangeIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_rangeIndicator.GetComponent<SphereCollider>().enabled = false; // Don't use this sphere for physics
            var mat = new Material(Shader.Find("UI/Unlit/Transparent"));
            var c = Color.blue;
            c.a = 0.05f;
            mat.color = c;
            m_rangeIndicator.GetComponent<MeshRenderer>().material = mat;
        }
    }
}


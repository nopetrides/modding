using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CropUtils
{
    [HarmonyPatch]
    public static class InteractRangeUtil
    {
        public static GameObject m_rangeIndicator = null;

        [HarmonyPatch(typeof(Player), "FindHoverObject")]
        public static void Postfix(Player __instance, ref GameObject hover, ref GameObject hoverCreature)
        {
            bool valid = false;
            if (ZInput.instance != null && Player.m_localPlayer != null)
            {
                if (CropUtils.Instance.UtilButton != null && 
                    ZInput.GetButton(CropUtils.Instance.UtilButton.Name))
                {
                    if (CropUtils.Instance.ShowVisualRangeIndicator)
                    {
                        if (m_rangeIndicator == null)
                        {
                            Jotunn.Logger.LogInfo("Creating range indicator");
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
                    // Allow Range to be changed with scroll wheel (while active)
                    if (ZInput.GetButtonDown(CropUtils.Instance.IncreaseRangeButton.Name))
                    {
                        CropUtils.Instance.ChangeRange(1.0f);
                    }
                    if (ZInput.GetButtonDown(CropUtils.Instance.DecreaseRangeButton.Name))
                    {
                        CropUtils.Instance.ChangeRange(-1.0f);
                    }
                }
            }
            if (m_rangeIndicator != null)
            {
                m_rangeIndicator.SetActive(valid);
            }
        }


        private static void CreateDebugRenderer()
        {
            m_rangeIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_rangeIndicator.GetComponent<SphereCollider>().enabled = false; // should be safe
            var mat = new Material(Shader.Find("UI/Unlit/Transparent"));
            var c = Color.blue;
            c.a = 0.05f;
            mat.color = c;
            m_rangeIndicator.GetComponent<MeshRenderer>().material = mat;
        }
    }
}


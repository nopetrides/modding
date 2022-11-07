using HarmonyLib;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace Crop_Utils
{
    /// <summary>
    /// Class for interacting with multiple ground objects at once. 
    /// While the original intention was for crops, this works for most interactables such as 
    /// stone, flint, branches, bee hives, dandelions and berries.
    /// </summary>
    [HarmonyPatch]
    public static class PickCropsUtil
    {
        private static FieldInfo m_interactMaskField = AccessTools.Field(typeof(Player), "m_interactMask");

        private static MethodInfo m_extractMethod = AccessTools.Method(typeof(Beehive), "Extract", null, null);

        [HarmonyPatch(typeof(Player),"Interact")]
        public static void Prefix(Player __instance, GameObject go, bool hold, bool alt)
        {
            if (__instance.InAttack() || __instance.InDodge())
            {
                return;
            }
            if (hold)
            {
                return;
            }
            if (!Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) && 
                !Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey))
            {
                return;
            }
            Interactable componentInParent = go.GetComponentInParent<Interactable>();
            
            // Pickable types like crops
            PickableCheck(__instance, go, alt, componentInParent);
            // Other interactable, namely beehives
            BeehiveCheck(__instance, go, alt, componentInParent);
        }

        private static void PickableCheck(Player __instance, GameObject go, bool alt, Interactable ComponentInParent)
        {
            if (ComponentInParent is Pickable initialPickable)
            {
                int num = (int)m_interactMaskField.GetValue(__instance);
                foreach (Collider collider in Physics.OverlapSphere(go.transform.position,
                    CropUtils.Instance.UtilRange, num))
                {
                    Pickable nearbyPickable;
                    if (collider == null)
                    {
                        nearbyPickable = null;
                    }
                    else
                    {
                        GameObject colliderObject = collider.gameObject;
                        nearbyPickable = ((colliderObject != null) ?
                            colliderObject.GetComponentInParent<Pickable>() : null);
                    }
                    if (nearbyPickable != null && nearbyPickable != initialPickable)
                    {
                        if (nearbyPickable.m_itemPrefab.name == initialPickable.m_itemPrefab.name || 
                            (Input.GetKey(CropUtils.Instance.UtilControllerButton.MainKey) ||
                            Input.GetKey(CropUtils.Instance.UtilHotKey.MainKey)))
                        {
                            nearbyPickable.Interact(__instance, false, alt);
                        }
                    }
                }
            }
        }

        private static void BeehiveCheck(Player __instance, GameObject go, bool alt, Interactable ComponentInParent)
        {
            if (ComponentInParent is Beehive initialHive)
            {
                int num = (int)m_interactMaskField.GetValue(__instance);
                foreach(Collider collider in Physics.OverlapSphere(go.transform.position,
                    CropUtils.Instance.UtilRange, num))
                {
                    Beehive nearbyHive;
                    if (collider == null)
                    {
                        nearbyHive = null;
                    }
                    else
                    {
                        GameObject colliderObject = collider.gameObject;
                        nearbyHive = ((colliderObject != null) ?
                            colliderObject.GetComponentInParent<Beehive>() : null);
                    }
                    if (nearbyHive != null && 
                        nearbyHive != initialHive && 
                        PrivateArea.CheckAccess(nearbyHive.transform.position, 0f, true, false))
                    {
                        m_extractMethod.Invoke(nearbyHive, null);
                    }
                }
            }
        }
    }
}

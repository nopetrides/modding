using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Hand_Drawn_Map
{
    internal static class SurveyTool
    {
        internal const string PrefabName = "NPR_SurveyCompass";
        private const string VisualSource = "Catapult_Ammo_BloodGold";
        private static GameObject container;
        private static GameObject prefab;
        private static Recipe recipe;
        private static readonly FieldInfo RightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo NamedPrefabs = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        internal static bool IsEquipped(Player player)
        {
            ItemDrop.ItemData item = (ItemDrop.ItemData)RightItem.GetValue(player);
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName;
        }

        private static GameObject FindItem(ObjectDB db, string name)
            => db.m_items.Find(item => item != null && item.name == name);

        private static void RegisterItem(ObjectDB db)
        {
            if (FindItem(db, PrefabName) == null)
            {
                if (prefab == null)
                {
                    GameObject source = FindItem(db, VisualSource) ?? FindItem(db, "Tankard");
                    if (source == null) return;
                    if (source.name != VisualSource)
                        MapPlugin.Instance?.LogToolWarning("Bloodgold payload is unavailable; using a tankard visual for the Survey Compass.");
                    prefab = CreatePrefab(source);
                }
                db.m_items.Add(prefab);
            }
            if (prefab != null && ZNetScene.instance != null) RegisterNetwork(ZNetScene.instance, true);
            if (db.m_recipes.Exists(entry => entry != null && entry.name == "Recipe_" + PrefabName)) return;
            ItemDrop wood = FindItem(db, "FineWood")?.GetComponent<ItemDrop>();
            ItemDrop nail = FindItem(db, "BronzeNails")?.GetComponent<ItemDrop>();
            ItemDrop coal = FindItem(db, "Coal")?.GetComponent<ItemDrop>();
            CraftingStation station = null;
            foreach (Recipe entry in db.m_recipes)
                if (entry != null && entry.m_craftingStation != null && entry.m_craftingStation.gameObject.name == "piece_workbench")
                { station = entry.m_craftingStation; break; }
            if (wood == null || nail == null || coal == null || station == null) return;
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = "Recipe_" + PrefabName;
            }
            recipe.m_item = FindItem(db, PrefabName).GetComponent<ItemDrop>();
            recipe.m_enabled = true;
            recipe.m_amount = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = station;
            recipe.m_resources = new[]
            {
                Requirement(wood, 4), Requirement(nail, 1), Requirement(coal, 12)
            };
            db.m_recipes.Add(recipe);
        }

        private static Piece.Requirement Requirement(ItemDrop item, int amount)
            => new Piece.Requirement { m_resItem = item, m_amount = amount, m_amountPerLevel = 0, m_recover = true };

        private static GameObject CreatePrefab(GameObject source)
        {
            if (container == null)
            {
                container = new GameObject("HandDrawnMapPrefabs");
                container.SetActive(false);
                Object.DontDestroyOnLoad(container);
            }
            GameObject clone = Object.Instantiate(source, container.transform, false);
            clone.name = PrefabName;
            try
            {
                ItemDrop drop = clone.GetComponent<ItemDrop>();
                ItemDrop sourceDrop = source.GetComponent<ItemDrop>();
                drop.m_itemData = sourceDrop.m_itemData.Clone();
                drop.m_itemData.m_shared = new ItemDrop.ItemData.SharedData
                {
                    m_name = "Survey Compass",
                    m_description = "A simple sun compass for sketching your surroundings. Equip and right-click to draw a temporary guide line across your map.",
                    m_dlc = "",
                    m_itemType = ItemDrop.ItemData.ItemType.Tool,
                    m_animationState = ItemDrop.ItemData.AnimationState.OneHanded,
                    m_icons = sourceDrop.m_itemData.m_shared.m_icons != null ? (Sprite[])sourceDrop.m_itemData.m_shared.m_icons.Clone() : Array.Empty<Sprite>(),
                    m_maxStackSize = 1,
                    m_maxQuality = 1,
                    m_weight = 0.5f,
                    m_teleportable = true,
                    m_equipDuration = 0.3f,
                    m_useDurability = false,
                    m_blockPower = 0f,
                    m_deflectionForce = 0f,
                    m_attack = new Attack(),
                    m_secondaryAttack = new Attack()
                };
                drop.m_itemData.m_stack = 1;
                drop.m_itemData.m_quality = 1;
                drop.m_itemData.m_variant = 0;
                drop.m_itemData.m_dropPrefab = clone;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                { clone.SetActive(true); return clone; }
                Transform originalAttach = clone.transform.Find("attach");
                if (originalAttach != null) originalAttach.name = "drop_visual";
                Transform attach = new GameObject("attach").transform;
                attach.SetParent(clone.transform, false);
                Transform model = new GameObject("Visual").transform;
                model.SetParent(attach, false);
                Transform sourceVisual = source.transform.Find("attach");
                CopyMeshes(sourceVisual != null ? sourceVisual : source.transform, model, true);
                FitInHand(model);
                attach.gameObject.SetActive(false);
                clone.SetActive(true);
                return clone;
            }
            catch
            {
                Object.Destroy(clone);
                throw;
            }
        }

        private static void CopyMeshes(Transform source, Transform parent, bool root)
        {
            Transform copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.localPosition = root ? Vector3.zero : source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && filter.sharedMesh != null && renderer != null)
            {
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            else
            {
                SkinnedMeshRenderer skinned = source.GetComponent<SkinnedMeshRenderer>();
                if (skinned != null && skinned.sharedMesh != null)
                {
                    copy.gameObject.AddComponent<MeshFilter>().sharedMesh = skinned.sharedMesh;
                    copy.gameObject.AddComponent<MeshRenderer>().sharedMaterials = skinned.sharedMaterials;
                }
            }
            for (int i = 0; i < source.childCount; i++) CopyMeshes(source.GetChild(i), copy, false);
        }

        private static void FitInHand(Transform model)
        {
            bool found = false;
            Bounds bounds = new Bounds();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = model.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!found) throw new InvalidOperationException("No mesh found for the survey tool's held visual.");
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = longest > 0.001f ? 0.18f / longest : 1f;
            model.localScale = Vector3.one * scale;
            model.localPosition = -bounds.center * scale + new Vector3(0, 0.03f, 0.06f);
        }

        [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
        private static class DatabaseRegistration
        {
            private static void Prefix(ObjectDB __instance)
            {
                try { RegisterItem(__instance); }
                catch (Exception exception) { MapPlugin.Instance?.LogToolError(exception); }
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class NetworkRegistration
        {
            private static void Prefix(ZNetScene __instance) => RegisterNetwork(__instance, false);
            private static void Postfix(ZNetScene __instance) => RegisterNetwork(__instance, true);
        }

        private static void RegisterNetwork(ZNetScene scene, bool initialized)
        {
            if (prefab == null) return;
            if (!scene.m_prefabs.Exists(item => item != null && item.name == PrefabName)
                && !scene.m_nonNetViewPrefabs.Exists(item => item != null && item.name == PrefabName))
            {
                if (prefab.GetComponent<ZNetView>() != null) scene.m_prefabs.Add(prefab);
                else scene.m_nonNetViewPrefabs.Add(prefab);
            }
            if (initialized)
            {
                var names = (System.Collections.Generic.Dictionary<int, GameObject>)NamedPrefabs.GetValue(scene);
                names[PrefabName.GetStableHashCode()] = prefab;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class ToolControls
        {
            private static void Prefix(Player __instance, ref bool attack, ref bool attackHold,
                ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold)
            {
                if (__instance != Player.m_localPlayer || !IsEquipped(__instance)) return;
                attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hand_Drawn_Map
{
    [BepInPlugin(Guid, "Hand Drawn Map", Version)]
    [DefaultExecutionOrder(-10000)]
    public sealed class MapPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.nopetrides.valheim.hand-drawn-map";
        public const string Version = "0.4.1";
        internal static MapPlugin Instance;
        internal ConfigEntry<KeyboardShortcut> MapKey;
#if DEBUG
        internal ConfigEntry<KeyboardShortcut> SurveyKey;
#endif
        internal ConfigEntry<float> SurveyLifetime;
        internal ConfigEntry<float> PencilPixels;
        internal ConfigEntry<float> EraserPixels;
        internal ConfigEntry<float> AnnotationLifetime;
        internal ConfigEntry<float> BrushOpacity;
        internal ConfigEntry<bool> ShowMyLocation;
        internal ConfigEntry<bool> PersonalMapOnly;
        internal readonly List<SurveyGuide> Guides = new List<SurveyGuide>();
        internal ChartDocument Document;
        internal CanvasWindow Window;
        internal bool Dirty;
        internal MapSession Shared;
        private bool headless;
        internal bool Headless => headless;
        private Harmony harmony;
        private string savePath;
        private bool saveBlocked;
        private float lastSave;
        private int lastPoll = -1;
        private int consumedFrame = -10;
        private int blockThroughFrame = -10;
        private Minimap.MapMode previousMode;

        internal static bool IsOpen => Instance != null && Instance.Window != null && Instance.Window.IsOpen;
        internal static bool BlocksInput => IsOpen || Instance != null && Time.frameCount <= Instance.blockThroughFrame;
        internal static bool ConsumedInput => Instance != null && Instance.consumedFrame == Time.frameCount;

        private void Awake()
        {
            headless = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
            Instance = this;
            MapKey = Config.Bind("Controls", "OpenMap", new KeyboardShortcut(KeyCode.M), "Open or close the drawing canvas.");
#if DEBUG
            SurveyKey = Config.Bind("Controls", "SurveyLine", new KeyboardShortcut(KeyCode.F8), "Capture a temporary full-map bearing while looking into the world. No item required.");
#endif
            SurveyLifetime = Config.Bind("Drawing", "SurveyLifetimeSeconds", 120f,
                new ConfigDescription("Survey guide lifetime.", new AcceptableValueRange<float>(5f, 1800f)));
            PencilPixels = Config.Bind("Drawing", "PencilWidthPixels", 2.5f,
                new ConfigDescription("Pencil width at the start of a stroke. The resulting ink stays in world units.", new AcceptableValueRange<float>(1f, 16f)));
            EraserPixels = Config.Bind("Drawing", "EraserRadiusPixels", 14f,
                new ConfigDescription("Eraser radius at the start of a gesture.", new AcceptableValueRange<float>(3f, 100f)));
            AnnotationLifetime = Config.Bind("Drawing", "AnnotationLifetimeSeconds", 120f,
                new ConfigDescription("Shared annotation lifetime, set by the world owner.", new AcceptableValueRange<float>(5f, 1800f)));
            BrushOpacity = Config.Bind("Drawing", "BrushIndicatorOpacity", 0.2f,
                new ConfigDescription("Opacity of the brush footprint indicator.", new AcceptableValueRange<float>(0f, 1f)));
            ShowMyLocation = Config.Bind("Map", "ShowMyLocation", false,
                "Show your current position and camera heading on the drawing canvas.");
            PersonalMapOnly = Config.Bind("Map", "PersonalMapOnly", false,
                "Use a personal map immediately, without connecting to the host's shared map.");
            Shared = new MapSession(this);
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(MapPlugin).Assembly);
            Logger.LogInfo("Hand Drawn Map " + Version + " loaded.");
        }

        private void Update()
        {
            Shared.Tick();
            if (!headless) Poll();
        }

        internal void Poll()
        {
            if (lastPoll == Time.frameCount) return;
            lastPoll = Time.frameCount;
            Shared.Tick();
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || Minimap.instance == null)
            {
                ReleaseWorld();
                return;
            }
            long world = ZNet.instance.GetWorldUID();
            long character = player.GetPlayerID();
            if (world == 0 || character == 0) return;
            string path = Path.Combine(Paths.ConfigPath, Guid, world + "_" + character + ".view.chart");
            if (path != savePath) LoadWorld(path);

            if (IsOpen && (!Application.isFocused || player.IsDead() || player.IsTeleporting() || player.InCutscene() || OtherUiOpen()))
                CloseMap();
            if (ShortcutDown(MapKey.Value) && !OtherUiOpen())
            {
                consumedFrame = Time.frameCount;
                if (IsOpen) CloseMap();
                else if (CanUseWorldInput(player)) OpenMap();
            }
            bool survey = SurveyTool.IsEquipped(player) && Input.GetMouseButtonDown(1);
#if DEBUG
            survey |= ShortcutDown(SurveyKey.Value);
#endif
            if (!IsOpen && survey && CanUseWorldInput(player)) AddSurvey(player);
            if (IsOpen) Window.Tick();
            if (Guides.RemoveAll(guide => guide.Expires <= Time.unscaledTime) > 0) Window?.Invalidate();
            if (Dirty && Time.unscaledTime - lastSave > 5f && (Window == null || !Window.InGesture)) Save();
        }

        private static bool ShortcutDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey)) return false;
            foreach (KeyCode modifier in shortcut.Modifiers)
                if (!Input.GetKey(modifier)) return false;
            return true;
        }

        private static bool CanUseWorldInput(Player player)
            => Application.isFocused && !OtherUiOpen() && !Minimap.IsOpen()
                && !player.IsDead() && !player.IsTeleporting() && !player.InCutscene()
                && !player.InAttack() && !player.InDodge();

        private static bool OtherUiOpen()
            => InventoryGui.IsVisible() || Menu.IsVisible() || Console.IsVisible() || TextInput.IsVisible()
                || StoreGui.IsVisible() || Chat.instance != null && Chat.instance.HasFocus()
                || ZInput.VirtualKeyboardOpen || UnifiedPopup.IsVisible();

        private void LoadWorld(string path)
        {
            ReleaseWorld();
            savePath = path;
            saveBlocked = false;
            try
            {
                bool exists = File.Exists(path);
                string legacy = path.Replace(".view.chart", ".chart");
                ChartDocument saved = ChartStore.Load(exists ? path : legacy);
                Document = new ChartDocument { Center = saved.Center, Zoom = saved.Zoom };
                if (!exists) Document.Zoom = Minimap.instance.LargeZoom;
            }
            catch (Exception exception)
            {
                saveBlocked = true;
                Document = new ChartDocument();
                Logger.LogError("Chart could not be loaded; saving disabled to preserve the file: " + exception.Message);
            }
            Window = new CanvasWindow(this, false);
            Shared.Bind(Document, () => Window?.Invalidate());
            lastSave = Time.unscaledTime;
        }

        private void OpenMap()
        {
            previousMode = Minimap.instance.m_mode;
            Minimap.instance.SetMapMode(Minimap.MapMode.None);
            Window.SetOpen(true);
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }

        internal void CloseMap()
        {
            if (!IsOpen) return;
            Window.SetOpen(false);
            blockThroughFrame = Time.frameCount + 2;
            consumedFrame = Time.frameCount;
            if (Minimap.instance != null)
                Minimap.instance.SetMapMode(Game.m_noMap ? Minimap.MapMode.None : previousMode);
            Save();
        }

        private void AddSurvey(Player player)
        {
            if (GameCamera.instance == null) return;
            Vector3 direction = GameCamera.instance.transform.forward;
            var bearing = new Vector2(direction.x, direction.z);
            if (bearing.sqrMagnitude < 0.001f) return;
            bearing.Normalize();
            Vector3 position = player.transform.position;
            if (Guides.Count >= 32) Guides.RemoveAt(0);
            Guides.Add(new SurveyGuide
            {
                Origin = new ChartPoint(position.x, position.z),
                Direction = new ChartPoint(bearing.x, bearing.y),
                Created = Time.unscaledTime,
                Expires = Time.unscaledTime + SurveyLifetime.Value
            });
            Window.Invalidate();
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "You sketch a guide line across your map.");
        }

        internal void LogToolError(Exception exception) => Logger.LogError("Survey Compass registration failed: " + exception);
        internal void LogToolWarning(string message) => Logger.LogWarning(message);
        internal void LogMapInfo(string message) => Logger.LogInfo(message);

        internal void Save()
        {
            if (!Dirty || savePath == null || saveBlocked) return;
            lastSave = Time.unscaledTime;
            try { ChartStore.Save(savePath, new ChartDocument { Center = Document.Center, Zoom = Document.Zoom }); Dirty = false; }
            catch (Exception exception) { Logger.LogError("Chart save failed: " + exception.Message); }
        }

        private void ReleaseWorld()
        {
            CloseMap();
            Save();
            Shared?.Unbind();
            Window?.Dispose();
            Window = null;
            Document = null;
            savePath = null;
            Guides.Clear();
            Dirty = false;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) CloseMap(); }
        private void OnApplicationQuit() { Window?.FinishSharedGesture(); Save(); Shared?.Flush(); }
        private void OnDestroy()
        {
            ReleaseWorld();
            Shared?.Dispose();
            harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.IsOpen))]
        private static class MapInput
        {
            private static void Postfix(ref bool __result) { if (BlocksInput) __result = true; }
        }

        [HarmonyPatch(typeof(Minimap), "Update")]
        private static class MapUpdate
        {
            private static bool Prefix()
            {
                Instance?.Poll();
                return !IsOpen && !ConsumedInput;
            }
        }

        [HarmonyPatch(typeof(Menu), "Update")]
        private static class MenuUpdate
        {
            private static bool Prefix() => !IsOpen && !ConsumedInput;
        }

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class PeerConnected
        {
            private static void Prefix(ZNetPeer __0) => Instance?.Shared?.Register(__0);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        private static class WorldShutdown
        {
            private static void Prefix()
            {
                Instance?.Window?.FinishSharedGesture();
                Instance?.Shared?.Flush();
            }
        }
    }

    internal sealed class SurveyGuide
    {
        public ChartPoint Origin;
        public ChartPoint Direction;
        public float Created;
        public float Expires;
    }
}

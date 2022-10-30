using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Linq;
using System;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Events;

namespace NPR_Valheim_ModUtils
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PluginInfo. PLUGIN_VERSION)]
    [BepInProcess(VALHEIM_EXE_NAME)]
    public class ModUtilsManager : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.nopetrides.valheim.mod-utils";
        public const string PLUGIN_NAME = "NPR_ValheimModUtils";
        public const string PLUGIN_VERSION = "1.0.0";
        public const string VALHEIM_EXE_NAME = "valheim.exe";
        internal const string LoggerName = "ModUtilsManagerLog";
        public const string ROOT_GO_NAME = "_ModUtilsRoot";

        public readonly Color ValheimOrange = new Color(1f, 0.631f, 0.235f, 1f);

        // Valheim standard font normal faced.    
        public Font AveriaSerif { get; private set; }

        // Valheims standard font bold faced.
        public Font AveriaSerifBold { get; private set; }

        /// <summary>
        /// Instancing the plugin 
        /// </summary>
        private static ModUtilsManager _instance;
        public static ModUtilsManager Instance => _instance;

        internal static Harmony Harmony;

        internal static ManualLogSource Log;

        #region Jotunn Styled Front and Back CustomGUI
        /// <summary>
        /// GUI container in front of Valheim's GUI elements with automatic scaling for
        /// high res displays and pixel correction. Gets rebuild at every scene change so
        /// make, sure to add your custom GUI prefabs again on each scene change.
        /// </summary>
        public static GameObject CustomGUIFront { get; private set; }

        /// <summary>
        /// GUI container behind Valheim's GUI elements with automatic scaling for high
        /// res displays and pixel correction. Gets rebuild at every scene change so make
        /// sure to add your custom GUI prefabs again on each scene change.
        /// </summary>
        public static GameObject CustomGUIBack { get; private set; }

        /// <summary>
        /// Event that gets fired every time the Unity scene changed and new CustomGUI
        /// objects were created. Subscribe to this event to create your custom GUI 
        /// objects and add them as a child to either CustomGUIFront or CustomGUIBack.
        /// </summary>
        public static event Action OnCustomGUIAvailable;

        /// <summary>
        /// On the main menu page, a "PixelFix" must be applied for the
        /// pixelsPerUnitMultiplier on images.
        /// This bool lets you know if you need to apply the pixel fix or not.
        /// In other words, are you on the startup UI or the in-game UI
        /// </summary>
        private bool GUIInStart;

        /// <summary>
        /// Cache headless state, this means the mod is running on a dedicated server
        /// </summary>
        private static bool Headless;

        /// <summary>
        /// GameObject representing the ModUtilsManager
        /// Set DontDestroyOnLoad
        /// </summary>
        internal static GameObject RootObject;


        /// <summary>
        /// When Valheim launches it's startup scene
        /// Find the GUI root and create a new GUI we can reference it in the main menu
        /// </summary>
        /// <param name="self"></param>
        internal void FejdStartup_SetupGui(FejdStartup self)
        {
            Log.LogInfo("Building GUI in startup");
            Transform transform = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault((GameObject x) => x.name == "GuiRoot")?.transform.Find("GUI");
            if (!transform)
            {
                Log.LogError("GuiRoot GUI not found, not creating custom GUI");
                return;
            }

            GUIInStart = true;
            CreateCustomGUI(transform);
            // ResetInputBlock();
        }

        /// <summary>
        /// When Valheim loads into a world with the chosen character
        /// Find the GUI root and create a new GUI we can reference it in game
        /// </summary>
        /// <param name="self"></param>
        internal void Game_Start(Game self)
        {
            Log.LogInfo("Building GUI in game");
            Transform transform = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault((GameObject x) => x.name == "_GameMain")?.transform.Find("LoadingGUI");
            if (!transform)
            {
                Log.LogError("_GameMain LoadingGUI not found, not creating custom GUI");
                return;
            }

            GUIInStart = false;
            CreateCustomGUI(transform);
            // ResetInputBlock();
        }

        /// <summary>
        /// Create GameObjects for mods to append their custom GUI to
        /// </summary>
        /// <param name="parent">The transform of the root for GUI objects</param>
        private void CreateCustomGUI(Transform parent)
        {
            // The object that sits in front of game UI
            CustomGUIFront = new GameObject("CustomGUIFront", typeof(RectTransform), typeof(GuiPixelFix));
            CustomGUIFront.layer = 5;
            CustomGUIFront.transform.SetParent(parent.transform, worldPositionStays: false);
            CustomGUIFront.transform.SetAsLastSibling();
            CustomGUIFront.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            CustomGUIFront.GetComponent<RectTransform>().anchorMax = Vector2.one;

            // The object that sits behind all game UI (not behind 3d environment)
            CustomGUIBack = new GameObject("CustomGUIBack", typeof(RectTransform), typeof(GuiPixelFix));
            CustomGUIBack.layer = 5;
            CustomGUIBack.transform.SetParent(parent.transform, worldPositionStays: false);
            CustomGUIBack.transform.SetAsFirstSibling();
            CustomGUIBack.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            CustomGUIBack.GetComponent<RectTransform>().anchorMax = Vector2.one;

            InvokeOnCustomGUIAvailable();
        }

        /// <summary>
        /// Fire off the event after the CustomGUI was created
        /// </summary>
        private void InvokeOnCustomGUIAvailable()
        {
            Log.LogInfo("Custom GUI Front & Back built and ready");
            OnCustomGUIAvailable?.SafeInvoke();
        }

        //
        // Summary:
        //     Initialize the manager
        public void Init()
        {
            Headless = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
            if (Headless)
            {
                return;
            }
            Harmony.PatchAll(typeof(Patches));
            SceneManager.sceneLoaded += new UnityAction<Scene, LoadSceneMode>(InitialLoad);
        }

        /// <summary>
        /// Somethings need to happen when the scene loads
        /// </summary>
        /// <exception cref="Exception"></exception>
        private void InitialLoad(Scene scene, LoadSceneMode loadMode)
        {
            Log.LogInfo("Processing Initial Load");
            try
            {
                // Prepare fonts for use
                Font[] fontResources = Resources.FindObjectsOfTypeAll<Font>();
                AveriaSerif = fontResources.FirstOrDefault((Font x) => x.name == "AveriaSerifLibre-Regular");
                AveriaSerifBold = fontResources.FirstOrDefault((Font x) => x.name == "AveriaSerifLibre-Bold");
                if (AveriaSerifBold == null || AveriaSerif == null)
                {
                    throw new Exception("Fonts not found");
                }
                else
                {
                    Log.LogInfo("Fonts loaded");
                }
            }
            catch (Exception data)
            {
                Log.LogError(data);
            }
            finally
            {
                SceneManager.sceneLoaded -= InitialLoad;
            }
        }

        #endregion // Jotunn Styled Front and Back CustomGUI

        private void Awake()
        {
            if(_instance != null)
            {
                Destroy(this);
                return;
            }
            // Set singleton
            _instance = this;
            // Create a static logger we can use
            Log = new ManualLogSource(LoggerName);
            BepInEx.Logging.Logger.Sources.Add(Log);
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loading...");

            // Create harmony patching object
            Harmony = new Harmony(PLUGIN_GUID);

            // Harmony.PatchAll(Assembly.GetExecutingAssembly()); // May not be needed?

            RootObject = new GameObject(ROOT_GO_NAME);
            DontDestroyOnLoad(RootObject);

            Log.LogInfo("Initializing...");
            Init();

            Game.isModded = true;
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} Loaded successfully");
        }

    }
}

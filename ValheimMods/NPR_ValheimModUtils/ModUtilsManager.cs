using BepInEx;
using UnityEngine.SceneManagement;
using UnityEngine;
using System.Linq;
using System;
using System.Diagnostics;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;

namespace NPR_ValheimModUtils
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class ModUtilsManager : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "NPR_ValheimModUtils";
        public const string PLUGIN_NAME = "NPR_ValheimModUtils";
        public const string PLUGIN_VERSION = "1.0.0";

        /// <summary>
        /// Instancing the plugin 
        /// </summary>
        private static ModUtilsManager _instance;
        public static ModUtilsManager Instance => _instance ?? (_instance = new ModUtilsManager());

        internal const string LoggerName = "MyPluginLog";
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
        /// When Valheim launches it's startup scene
        /// Find the GUI root and create a new GUI we can reference it in the main menu
        /// </summary>
        /// <param name="self"></param>
        internal void FejdStartup_SetupGui(FejdStartup self)
        {
            Transform transform = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault((GameObject x) => x.name == "GuiRoot")?.transform.Find("GUI");
            if (!transform)
            {
                Logger.LogError("GuiRoot GUI not found, not creating custom GUI");
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
            Transform transform = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault((GameObject x) => x.name == "_GameMain")?.transform.Find("LoadingGUI");
            if (!transform)
            {
                Logger.LogError("_GameMain LoadingGUI not found, not creating custom GUI");
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
            // The 
            CustomGUIFront = new GameObject("CustomGUIFront", typeof(RectTransform), typeof(GuiPixelFix));
            CustomGUIFront.layer = 5;
            CustomGUIFront.transform.SetParent(parent.transform, worldPositionStays: false);
            CustomGUIFront.transform.SetAsLastSibling();
            CustomGUIFront.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            CustomGUIFront.GetComponent<RectTransform>().anchorMax = Vector2.one;

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
            OnCustomGUIAvailable?.SafeInvoke();
        }

        #endregion // Jotunn Styled Front and Back CustomGUI

        private void Awake()
        {
            // Create a static logger we can use
            Log = new ManualLogSource(LoggerName);
            BepInEx.Logging.Logger.Sources.Add(Log);
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // Apply any patches
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is done what it needs to do.");
        }
    }
}

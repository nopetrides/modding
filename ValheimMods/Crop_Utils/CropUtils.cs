using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Crop_Utils
{
    /// <summary>
    /// This is the core class for the Crop Util mod.
    /// Created by NoPetRides for Valheim
    /// </summary>
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInProcess(VALHEIM_EXE_NAME)]
    [BepInDependency("com.nopetrides.valheim.mod-utils")]
    internal class CropUtils : BaseUnityPlugin
    {
        public static CropUtils Instance;

        public const string PluginGUID = "com.nopetrides.valheim.crop-utils";
        public const string PluginName = "Crop Utils";
        public const string PluginVersion = "1.0.0";
        public const string VALHEIM_EXE_NAME = "valheim.exe";
        internal const string LoggerName = "CropUtilsLog";

        // Configurable range
        private ConfigEntry<int> m_utilRange;
        public int UtilRange => m_utilRange.Value;

        private ConfigEntry<int> m_cropUtilDiscount;
        public int Discount => m_cropUtilDiscount.Value;

        // Render the visual indicator or not
        private ConfigEntry<bool> m_showVisualRangeIndicator;
        public bool ShowVisualRangeIndicator => m_showVisualRangeIndicator.Value;

        // Variable button backed by a KeyCode and a GamepadButton config
        // No idea what good gamepad buttons are
        private ConfigEntry<KeyboardShortcut> m_increaseRangeControllerButton;
        public KeyboardShortcut IncreaseRangeControllerButton => m_increaseRangeControllerButton.Value;
        private ConfigEntry<KeyboardShortcut> m_increaseRangeHotKey;
        public KeyboardShortcut IncreaseRangeHotKey => m_increaseRangeHotKey.Value;

        private ConfigEntry<KeyboardShortcut> m_decreaseRangeControllerButton;
        public KeyboardShortcut DecreaseRangeControllerButton => m_decreaseRangeControllerButton.Value;
        private ConfigEntry<KeyboardShortcut> m_decreaseRangeHotKey;
        public KeyboardShortcut DecreaseRangeHotKey => m_decreaseRangeHotKey.Value;

        private ConfigEntry<KeyboardShortcut> m_utilControllerButton;
        public KeyboardShortcut UtilControllerButton => m_utilControllerButton.Value;
        private ConfigEntry<KeyboardShortcut> m_utilHotKey;
        public KeyboardShortcut UtilHotKey => m_utilHotKey.Value;

        private ConfigEntry<KeyboardShortcut> m_utilAltControllerButton;
        public KeyboardShortcut UtilAltControllerButton => m_utilAltControllerButton.Value;
        private ConfigEntry<KeyboardShortcut> m_utilAltHotKey;
        public KeyboardShortcut UtilAltHotKey => m_utilAltHotKey.Value;

        internal static ManualLogSource Log;

        /// <summary>
        /// Called by unity on all monobehaviours after creation
        /// </summary>
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            // Create a static logger we can use
            Log = new ManualLogSource(LoggerName);
            BepInEx.Logging.Logger.Sources.Add(Log);
            Log.LogInfo("CropUtils startup sequence");

            CreateConfigs();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);

            Log.LogInfo("CropUtils successfully patched in");
        }

        /// <summary>
        /// Generate the config file variables
        /// If not yet made, uses defaults
        /// Else, load any saved config value
        /// </summary>
        private void CreateConfigs()
        {
            Config.SaveOnConfigSet = true;

            // Add a configrable range. This will maybe use the admin only control setting instead?
            // new ConfigurationManagerAttributes { IsAdminOnly = true }
            m_utilRange = Config.Bind("Util Range",
                "UtilRangeConfig",
                20,
                new ConfigDescription(
                    "The distance (in Unity Units) to perform operations out to. Larger numbers may hinder performance.",
                    new AcceptableValueRange<int>(1, 50)));

            m_showVisualRangeIndicator = Config.Bind("Util Range",
                "ShouldShowRangeIndicator",
                true,
                new ConfigDescription("Should the range be shown when holding down the util key"));

            // Add a Gamepad button for the Hot Key
            m_increaseRangeControllerButton = Config.Bind("Util Range",
                "Increase Range Key Gamepad",
                new KeyboardShortcut(KeyCode.JoystickButton7, new KeyCode[0]),
                new ConfigDescription("Gamepad button to increase the range of picking while holding the Utiity Key."));
            // Also add a client side custom input key for the Hot Key
            m_increaseRangeHotKey = Config.Bind("Utils Keys",
                "Increase Range Hot Key",
                new KeyboardShortcut(KeyCode.RightBracket, new KeyCode[0]),
                new ConfigDescription("Key to increase the range of picking while holding the Utiity Key."));

            m_decreaseRangeControllerButton = Config.Bind("Util Range",
                "Decrease Range Key Gamepad",
                new KeyboardShortcut(KeyCode.JoystickButton6, new KeyCode[0]),
                new ConfigDescription("Gamepad button to decrease the range of picking while holding the Utiity Key."));
            m_decreaseRangeHotKey = Config.Bind("Util Keys",
                "Decrease Range Hot Key",
               new KeyboardShortcut(KeyCode.LeftBracket, new KeyCode[0]),
                new ConfigDescription("Key to decrease the range of pickingwhile holding the Utiity Key."));

            m_utilControllerButton = Config.Bind("Util Keys",
                "Utility Key Gamepad",
               new KeyboardShortcut(KeyCode.JoystickButton5, new KeyCode[0]),
                new ConfigDescription("Button to enable farming utility helpers when planting or picking. Default behavior is pickup only this type and place in a line"));
            m_utilHotKey = Config.Bind("Utils Keys",
                "Utility Hot Key",
                new KeyboardShortcut(KeyCode.LeftAlt, new KeyCode[0]),
                new ConfigDescription("Key to enable farming utility helpers when planting or picking. Default behavior is pickup only this type and place in a line"));

            m_utilAltControllerButton = Config.Bind("Util Keys",
                "Ignore Type Key Gamepad",
                new KeyboardShortcut(KeyCode.JoystickButton4, new KeyCode[0]),
                new ConfigDescription("Button to enable farming utility helpers when planting or picking. Should pick any type of crop or use the radius placement for crops."));
            m_utilAltHotKey = Config.Bind("Util Keys",
                "Utlity Alternative Hot Key",
                new KeyboardShortcut(KeyCode.Z, new KeyCode[0]),
                new ConfigDescription("Key to enable farming utility helpers when planting or picking. Should pick any type of crop or use the radius placement for crops."));

            m_cropUtilDiscount = Config.Bind("Stamina & Tool Durability Discount",
               "StaminaDiscountConfig",
               20,
               new ConfigDescription("The divider for how much less stamina planting uses when using the util (stamina cost / 20) default"));
        }

        /// <summary>
        /// Public method to change the range of the utilities
        /// </summary>
        /// <param name="rangeChange"></param>
        public void ChangeRange(int rangeChange)
        {
            m_utilRange.Value += rangeChange;
        }
    }
}

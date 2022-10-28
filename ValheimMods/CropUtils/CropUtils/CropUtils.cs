// CropUtils
// a Valheim mod skeleton using Jötunn
// 
// File:    CropUtils.cs
// Project: CropUtils

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using MonoMod.RuntimeDetour;
using System.Reflection;
using UnityEngine;

namespace CropUtils
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class CropUtils : BaseUnityPlugin
    {
        public static CropUtils Instance;

        public const string PluginGUID = "com.jotunn.CropUtils";
        public const string PluginName = "CropUtils";
        public const string PluginVersion = "0.0.2";

        // Configurable range
        private ConfigEntry<float> m_utilRange;
        public float UtilRange => m_utilRange.Value;
        // Render the visual indicator or not
        private ConfigEntry<bool> m_showVisualRangeIndicator;
        public bool ShowVisualRangeIndicator => m_showVisualRangeIndicator.Value;

        // Variable button backed by a KeyCode and a GamepadButton config
        // No idea what good gamepad buttons are
        private ConfigEntry<InputManager.GamepadButton> m_increaseRangeControllerButton;
        private ConfigEntry<KeyCode> m_increaseRangeHotKey;
        public ButtonConfig IncreaseRangeButton;

        private ConfigEntry<InputManager.GamepadButton> m_decreaseRangeControllerButton;
        private ConfigEntry<KeyCode> m_decreaseRangeHotKey;
        public ButtonConfig DecreaseRangeButton;

        private ConfigEntry<InputManager.GamepadButton> m_utilControllerButton;
        private ConfigEntry<KeyCode> m_utilHotKey;
        public ButtonConfig UtilButton;

        private ConfigEntry<InputManager.GamepadButton> m_ignoreTypeControllerButton;
        private ConfigEntry<KeyCode> m_ignoreTypeHotKey;
        public ButtonConfig IgnoreTypeButton;


        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Jotunn.Logger.LogError("Whoops, we have a copy of this plugin???");
                Destroy(this);
                return;
            }

            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("CropUtils startup sequence");

            CreateConfigs();
            AddInputBindings();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);

            Jotunn.Logger.LogInfo("CropUtils successfully patched in");
        }

        private void CreateConfigs()
        {
            Config.SaveOnConfigSet = true;

            // Add a configrable range. This will maybe use the admin only control setting instead?
            // new ConfigurationManagerAttributes { IsAdminOnly = true }
            m_utilRange = Config.Bind("Util Range",
                "UtilRangeConfig",
                20f,
                new ConfigDescription(
                    "The distance (in Unity Units) to perform operations out to. Larger numbers may hinder performance.",
                    new AcceptableValueRange<float>(0.0f, 50.0f)));

            m_showVisualRangeIndicator = Config.Bind("Util Range",
                "ShouldShowRangeIndicator",
                true,
                new ConfigDescription("Should the range be shown when holding down the util key"));

            // Add a Gamepad button for the Hot Key
            m_increaseRangeControllerButton = Config.Bind("Util Range",
                "Increase Range Key Gamepad",
                InputManager.GamepadButton.LeftShoulder,
                new ConfigDescription("Gamepad button to increase the range of picking while holding the Utiity Key."));
            // Also add a client side custom input key for the Hot Key
            m_increaseRangeHotKey = Config.Bind("Utils Keys",
                "Increase Range Hot Key",
                KeyCode.RightBracket,
                new ConfigDescription("Key to increase the range of picking while holding the Utiity Key."));

            m_decreaseRangeControllerButton = Config.Bind("Util Range",
                "Decrease Range Key Gamepad",
                InputManager.GamepadButton.RightShoulder,
                new ConfigDescription("Gamepad button to decrease the range of picking while holding the Utiity Key."));
            m_decreaseRangeHotKey = Config.Bind("Util Keys",
                "Decrease Range Hot Key",
                KeyCode.LeftBracket,
                new ConfigDescription("Key to decrease the range of pickingwhile holding the Utiity Key."));

            m_utilControllerButton = Config.Bind("Util Keys", 
                "Utility Key Gamepad", 
                InputManager.GamepadButton.ButtonSouth,
                new ConfigDescription("Button to enable farming utility helpers when planting or picking"));
            m_utilHotKey = Config.Bind("Utils Keys", 
                "Utility Hot Key", 
                KeyCode.LeftAlt,
                new ConfigDescription("Key to enable farming utility helpers when planting or picking"));

            m_ignoreTypeControllerButton = Config.Bind("Util Keys", 
                "Ignore Type Key Gamepad", 
                InputManager.GamepadButton.ButtonWest,
                new ConfigDescription("Button to enable farming utility helpers when planting or picking"));
            m_ignoreTypeHotKey = Config.Bind("Util Keys", 
                "Ignore Type Hot Key", 
                KeyCode.LeftControl, 
                new ConfigDescription("Key to enable farming utility helpers when planting or picking"));

            
        }

        private void AddInputBindings()
        {
            // Add key bindings backed by a config value
            // Also adds the alternative Config for the gamepad button
            IncreaseRangeButton = new ButtonConfig
            {
                Name = "IncreaseRangeButton",
                GamepadConfig = m_increaseRangeControllerButton, // Gamepad input
                Config = m_increaseRangeHotKey,        // Keyboard input
                HintToken = "$increase_range_button",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            InputManager.Instance.AddButton(PluginGUID, IncreaseRangeButton);

            DecreaseRangeButton = new ButtonConfig
            {
                Name = "DecreaseRangeButton",
                GamepadConfig = m_decreaseRangeControllerButton, // Gamepad input
                Config = m_decreaseRangeHotKey,        // Keyboard input
                HintToken = "$decrease_range_button",
                BlockOtherInputs = false
            };
            InputManager.Instance.AddButton(PluginGUID, DecreaseRangeButton);

            UtilButton = new ButtonConfig
            {
                Name = "UtilButton",
                GamepadConfig = m_utilControllerButton, // Gamepad input
                Config = m_utilHotKey,        // Keyboard input
                HintToken = "$util_button",
                BlockOtherInputs = false
            };
            InputManager.Instance.AddButton(PluginGUID, UtilButton);

            IgnoreTypeButton = new ButtonConfig
            {
                Name = "IgnoreTypeButton",
                GamepadConfig = m_ignoreTypeControllerButton, // Gamepad input
                Config = m_ignoreTypeHotKey,        // Keyboard input
                HintToken = "$ignore_type_button",
                BlockOtherInputs = true
            };
            InputManager.Instance.AddButton(PluginGUID, IgnoreTypeButton);
        }

        public void ChangeRange(float rangeChange)
        {
            m_utilRange.Value += rangeChange;
        }
    }
}


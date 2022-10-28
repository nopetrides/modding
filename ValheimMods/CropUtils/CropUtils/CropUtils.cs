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
        public const string PluginVersion = "0.0.1";

        private ConfigEntry<float> m_utilRange;
        public float UtilRange => m_utilRange.Value;

        private ConfigEntry<bool> m_visualRangeIndicator;


        // Variable button backed by a KeyCode and a GamepadButton config
        // No idea what good gamepad buttons are
        private ConfigEntry<InputManager.GamepadButton> m_utilControllerButton;
        private ConfigEntry<KeyCode> m_utilHotKey;
        public ButtonConfig UtilButton;

        // No idea what good gamepad buttons are
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

            // Add an alternative Gamepad button for the Utility Hot Key
            m_utilControllerButton = Config.Bind("Util Keys", 
                "Utility Key Gamepad", 
                InputManager.GamepadButton.ButtonSouth,
                new ConfigDescription("Button to enable farming utility helpers when planting or picking"));
            // Also  Add a client side custom input key for the Utility Hot Key
            m_utilHotKey = Config.Bind("Utils Keys", 
                "Utility Hot Key", 
                KeyCode.LeftAlt,
                new ConfigDescription("Key to enable farming utility helpers when planting or picking"));

            // Add an alternative Gamepad button for the Utility Hot Key
            m_ignoreTypeControllerButton = Config.Bind("Util Keys", 
                "Ignore Type Key Gamepad", 
                InputManager.GamepadButton.RightShoulder,
                new ConfigDescription("Button to enable farming utility helpers when planting or picking"));
            // Also  Add a client side custom input key for the Utility Hot Key
            m_ignoreTypeHotKey = Config.Bind("Util Keys", 
                "Ignore Type Hot Key", 
                KeyCode.LeftControl, 
                new ConfigDescription("Key to enable farming utility helpers when planting or picking"));

            // Add a configrable range. This will maybe use the admin only control setting instead?
            // new ConfigurationManagerAttributes { IsAdminOnly = true }
            m_utilRange = Config.Bind("Util Range", 
                "UtilRangeConfig", 
                20f, 
                new ConfigDescription(
                    "The distance (in Unity Units) to perform operations out to. Larger numbers may hinder performance.", 
                    new AcceptableValueRange<float>(0f, 100f)));
        }

        private void AddInputBindings()
        {
            // Add key bindings backed by a config value
            // Also adds the alternative Config for the gamepad button
            // The HintToken is used for the custom KeyHint of the EvilSword
            UtilButton = new ButtonConfig
            {
                Name = "UtilButton",
                GamepadConfig = m_utilControllerButton, // Gamepad input
                Config = m_utilHotKey,        // Keyboard input
                HintToken = "$util_button",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            InputManager.Instance.AddButton(PluginGUID, UtilButton);

            // Add key bindings backed by a config value
            // Also adds the alternative Config for the gamepad button
            // The HintToken is used for the custom KeyHint of the EvilSword
            IgnoreTypeButton = new ButtonConfig
            {
                Name = "IgnoreTypeButton",
                GamepadConfig = m_ignoreTypeControllerButton, // Gamepad input
                Config = m_ignoreTypeHotKey,        // Keyboard input
                HintToken = "$ignore_type_button",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            InputManager.Instance.AddButton(PluginGUID, IgnoreTypeButton);
        }

        private void Update()
        {
            if (ZInput.instance != null)
            {
                if (UtilButton == null || !ZInput.GetButton(UtilButton.Name))
                {
                    if (m_visualRangeIndicator.Value)
                    {
                        
                    }
                    

                    // Allow Range to be changed with scroll wheel (while active)
                }
            }
        }
    }
}


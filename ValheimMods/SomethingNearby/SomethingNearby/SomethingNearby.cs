// SomethingNearby
// a Valheim mod using Jötunn
// 
// File:    SomethingNearby.cs
// Project: SomethingNearby

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Jotunn.Entities;
using Jotunn.Managers;
using System.Text.RegularExpressions;
using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;

namespace SomethingNearby
{
    /// <summary>
    /// The core of the plugin
    /// </summary>
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    internal class SomethingNearby : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.SomethingNearby";
        public const string PluginName = "SomethingNearby";
        public const string PluginVersion = "0.5.0";

        // TODO Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static SomethingNearby Instance;

        // Configurables
        private ConfigEntry<bool> _showLoadDoneMessage;
        private ConfigEntry<string> _loadDoneMessage;

        private ConfigEntry<bool> _onScreenMessaging;
        public bool ShowOnScreenMessages => _onScreenMessaging.Value;
        private ConfigEntry<Color> _messageColor;
        public Color MessageColor => _messageColor.Value;
        private ConfigEntry<int> _fontSize;
        private ConfigEntry<float> _fadeInTime;
        public float FadeInTime => _fadeInTime.Value;
        private ConfigEntry<float> _messageDuration;
        public float MessageDuration => _messageDuration.Value;
        private ConfigEntry<float> _fadeOutTime;
        public float FadeOutTime => _fadeOutTime.Value;
        private ConfigEntry<Vector2> _anchorMin;
        private ConfigEntry<Vector2> _anchorMax;

        // Other classes we need references to
        internal const string LoggerName = "MyPluginLog";
        internal static ManualLogSource Log;
        internal static MyLogListener LogEcho;
        private static MyMessageText GUIInstance;
        public bool IsScreenMessageShowing => GUIInstance.DisplayingMessage;
        private MyMessagesManager _messagingManager;
        public MyMessagesManager MessageManager => _messagingManager;

        private void Awake()
        {
            Instance = this;
            Log = new MyManualLogger(LoggerName);
            BepInEx.Logging.Logger.Sources.Add(Log);
            LogEcho = new MyLogListener();
            BepInEx.Logging.Logger.Listeners.Add(LogEcho);
            // Plugin startup logic
            Log.LogInfo($"Plugin {PluginGUID} is loaded!");
            Log.LogInfo("This C# script was coded by Noah Petrides and is built with BepInEx and Jotunn");

            BindConfigs();

            _messagingManager = new MyMessagesManager();
            GUIManager.OnCustomGUIAvailable += BuildGUIObject;

        }

        private void BindConfigs()
        {
            _showLoadDoneMessage = Config.Bind("On Screen Messaging.Load Complete",             // The section under which the option is shown
                                              "ShouldShowLoadDoneMessage", // The key of the configuration option in the configuration file
                                              true,                  // The default value
                                              "Should the plugin show the load complete message"); // Description of the option to show in the config file);
            Log.LogInfo("ShouldShowLoadDoneMessage configured");

            _loadDoneMessage = Config.Bind("On Screen Messaging.Load Complete",
                                              "LoadDoneMessage",
                                              "Plugin loaded and ready",
                                              "Display a message when the plugin is ready");
            Log.LogInfo("LoadDoneMessage configured");

            _onScreenMessaging = Config.Bind("On Screen Messaging.Text",
                                              "ShouldShowOnScreenMessaging",
                                              true,
                                              "Should the plugin show the on-screen \"something is nearby\" messages");
            Log.LogInfo("ShouldShowLoadDoneMessage configured");

            _messageColor = Config.Bind("On Screen Messaging.Text",
                                             "MessageColor",
                                             GUIManager.Instance.ValheimOrange,
                                             "The color of the messages");
            Log.LogInfo("MessageColor configured");

            _fontSize = Config.Bind("On Screen Messaging.Text",
                                             "FontSize",
                                             20,
                                             "The size of the messages");
            Log.LogInfo("FontSize configured");

            _fadeInTime = Config.Bind("On Screen Messaging.Text.Animation",
                                             "FadeInTime",
                                             .2f,
                                             "How quickly the message fades in");
            Log.LogInfo("FadeInTime configured");

            _messageDuration = Config.Bind("On Screen Messaging.Text.Animation",
                                             "MessageDuration",
                                             5f,
                                             "How long the message shows for before fading away");
            Log.LogInfo("MessageDuration configured");

            _fadeOutTime = Config.Bind("On Screen Messagin.Text.Animationg",
                                             "FadeOutTime",
                                             .5f,
                                             "How quickly the message fades away after its duration");
            Log.LogInfo("FadeOutTime configured");

            _anchorMin = Config.Bind("On Screen Messaging.Text.Position",
                                             "PositionalAnchorsMin",
                                             new Vector2(0.7f, 0.65f),
                                             "Minimum Unity Anchor for text position (0-1 as a % of the screen area)");
            Log.LogInfo("AnchorMin configured");

            _anchorMax = Config.Bind("On Screen Messaging.Text.Position",
                                            "PositionalAnchorsMax",
                                            new Vector2(0.97f, 0.65f),
                                            "Maximum Unity Anchor for text position (0-1 as a % of the screen area)");
            Log.LogInfo("AnchorMax configured");
        }

        private void BuildGUIObject()
        {
            if (GUIInstance)
            {
                Log.LogError("The GUI object already exists!");
                return;
            }

            var go = new GameObject("Something Nearby");
            go = Instantiate(go);
            GUIInstance = go.AddComponent<MyMessageText>();
            GUIInstance.Initialize(
                GUIManager.CustomGUIFront.transform,
                _anchorMin.Value, _anchorMax.Value,
                GUIManager.Instance.AveriaSerifBold, GUIManager.Instance.ValheimOrange,
                _fontSize.Value);

            if (_showLoadDoneMessage.Value)
            {
                _messagingManager.QueueMessage(_loadDoneMessage.Value);
            }
        }

        public void ShowNextQueuedMessage()
        {
            string message = MessageManager.GetNextQueuedMessage();
            if (!string.IsNullOrEmpty(message))
            {
                GUIInstance.ShowNextMessage(message);
            }
        }
    }
}


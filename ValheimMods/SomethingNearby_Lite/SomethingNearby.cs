using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using NPR_ValheimModUtils;
using UnityEngine;

namespace SomethingNearby
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency("com.nopetrides.valheim.npr-valheim-mod-utils")]
    public class SomethingNearby : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.nopetrides.valheim.something-nearby";
        public const string PLUGIN_NAME = "SomethingNearby";
        public const string PLUGIN_VERSION = "1.0.0";
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
            if (Instance != null)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            Log = new MyManualLogger(LoggerName);
            BepInEx.Logging.Logger.Sources.Add(Log);
            LogEcho = new MyLogListener();
            BepInEx.Logging.Logger.Listeners.Add(LogEcho);
            // Plugin startup logic
            Log.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");
            Log.LogInfo("This C# script was coded by Noah Petrides and is built with BepInEx and Jotunn");

            BindConfigs();

            _messagingManager = new MyMessagesManager();
            NPR_ValheimModUtils.ModUtilsManager.OnCustomGUIAvailable += BuildGUIObject;
        }
    }
}

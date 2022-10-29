using BepInEx.Logging;
using System;
using System.Text.RegularExpressions;

namespace SomethingNearby
{
    /// <summary>
    /// Script that scrapes the console logs
    /// </summary>
    internal class MyLogListener : ILogListener, IDisposable
    {
        internal bool WriteUnityLogs { get; set; } = true;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if ((sender is MyManualLogger))
            {
                return;
            }

            if (ContainsSoughtInfo(eventArgs.ToString(), out string message))
            {
                SomethingNearby.Log.LogInfo(message);
                SomethingNearby.Instance.MessageManager.QueueMessage(message);
            }
        }

        public void Dispose()
        {
        }

        // TODO localize
        private bool EchoLogs = true;
        private const string LogQueryDungeon = "Dungeon loaded *";
        private const string LogQuerySpawned = "Spawned ";
        private const string DungeonMessaage = "A Dungeon is nearby";
        private const string SpawnMessage_En = "{0} {1} appeared nearby";
        /// <summary>
        /// Check a log, and see if we want that info
        /// TODO move this somewhere else
        /// </summary>
        private bool ContainsSoughtInfo(string log, out string message)
        {
            message = "";
            if (!EchoLogs)
            {
                return false;
            }
            if (Regex.IsMatch(log, LogQueryDungeon))
            {
                message = DungeonMessaage;
                return true;
            }
            if (Regex.IsMatch(log, LogQuerySpawned))
            {
                int mobNameStart = log.IndexOf(LogQuerySpawned) + LogQuerySpawned.Length;
                int mobNameEnd = log.IndexOf(" x ", mobNameStart);
                string mobName = log.Substring(mobNameStart, mobNameEnd - mobNameStart);
                if (mobName.Contains("FireFlies")) // who cares about the bugs
                {
                    return false;
                }
                int numberStart = log.LastIndexOf(" ") + 1;
                string mobCount = log.Substring(numberStart).Trim('\r', '\n');
                string formattingString;
                formattingString = SpawnMessage_En;                
                message = string.Format(formattingString, mobCount, mobName);
                return true;
            }
            return false;
        }
    }
}

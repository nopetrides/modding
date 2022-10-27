using BepInEx.Logging;
namespace SomethingNearby
{
    /// <summary>
    /// Script that allows us to log messages
    /// </summary>
    internal class MyManualLogger : ManualLogSource
    {
        public MyManualLogger(string sourceName) : base(sourceName)
        {
            // no custom constructor logic yet
        }
    }
}

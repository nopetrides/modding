using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NPR_ValheimModUtils.Scripts
{
    public class Patching
    {
        public static class Patches
        {
            [HarmonyPatch(typeof(FejdStartup), "SetupGui")]
            [HarmonyPostfix]
            private static void FejdStartup_SetupGui(FejdStartup __instance)
            {
                ModUtilsManager.Instance.FejdStartup_SetupGui(__instance);
            }

            [HarmonyPatch(typeof(Game), "Start")]
            [HarmonyPostfix]
            private static void Game_Start(Game __instance)
            {
                ModUtilsManager.Instance.Game_Start(__instance);
            }
        }
    }
}

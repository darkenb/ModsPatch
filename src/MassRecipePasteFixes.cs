using HarmonyLib;
using Patches = MassRecipePaste.Patches;

namespace ModsPatch
{
    // MassRecipePaste 1.1.3 (Thunderstore): null guard.
    public static class MassRecipePasteFixes
    {
        public const string Target = "starfi5h.plugin.MassRecipePaste";

        public static bool AlreadyFixed() => typeof(Patches).GetMethod("CountSuccessfulPaste", AccessTools.all) != null;

        // Its UIRealtimeTip.Popup prefix reads the paste tool, which exists only in a loaded game: every popup outside one
        // (for example ErrorAnalyzer's "copied" tip on the start-up error screen) threw a NullReferenceException.
        [HarmonyPrefix, HarmonyPatch(typeof(Patches), nameof(Patches.Blocker))]
        static bool Blocker(ref bool __result)
        {
            if (Patches.tool != null) return true;
            __result = true;
            return false;
        }
    }
}

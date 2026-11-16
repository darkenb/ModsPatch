using System;
using DSPAllPlanetInfo;
using HarmonyLib;

namespace ModsPatch
{
    // DSPAllPlanetInfo 1.1.14 (Thunderstore) (observe level fix). Its window fixes are in BUG_UIFix.
    // The mod raises GameMain.history.universeObserveLevel to 4 while a star/planet panel fills, and lowers it again in a
    // postfix. An exception in the panel skipped the postfix and left every star fully revealed; with no history (menu) the
    // prefix itself threw.
    public static class AllPlanetInfoFixes
    {
        public const string Target = "Appun.DSP.plugin.AllPlanetInfo";

        public static bool AlreadyFixed() => typeof(InfoCreater).GetField("startSlotNo", AccessTools.all) != null;

        [HarmonyPrefix, HarmonyPatch(typeof(Hook), nameof(Hook.UIStarDetail_Prefix))]
        static bool NoHistoryPrefix(ref int __state, ref bool __result)
        {
            if (GameMain.history != null) return true;
            __state = -1;
            __result = true;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Hook), nameof(Hook.UIStarDetail_Postfix))]
        static bool NoHistoryPostfix(int __state) => __state >= 0 && GameMain.history != null;

        // Runs before the mod's prefix (Priority.First), so it sees the real level; restores it even after an exception.
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(UIStarDetail), nameof(UIStarDetail.OnStarDataSet))]
        [HarmonyPatch(typeof(UIStarDetail), nameof(UIStarDetail.RefreshDynamicProperties))]
        [HarmonyPatch(typeof(UIPlanetDetail), nameof(UIPlanetDetail.OnPlanetDataSet))]
        [HarmonyPatch(typeof(UIPlanetDetail), nameof(UIPlanetDetail.RefreshDynamicProperties))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.OnCursorFunction2Click))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.OnStarClick))]
        static void RememberLevel(out int __state) => __state = GameMain.history?.universeObserveLevel ?? -1;

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(UIStarDetail), nameof(UIStarDetail.OnStarDataSet))]
        [HarmonyPatch(typeof(UIStarDetail), nameof(UIStarDetail.RefreshDynamicProperties))]
        [HarmonyPatch(typeof(UIPlanetDetail), nameof(UIPlanetDetail.OnPlanetDataSet))]
        [HarmonyPatch(typeof(UIPlanetDetail), nameof(UIPlanetDetail.RefreshDynamicProperties))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.OnCursorFunction2Click))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.OnStarClick))]
        static Exception RestoreLevel(int __state, Exception __exception)
        {
            if (__state >= 0 && GameMain.history != null) GameMain.history.universeObserveLevel = __state;
            return __exception;
        }
    }
}

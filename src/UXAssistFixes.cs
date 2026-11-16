using System;
using BepInEx.Configuration;
using HarmonyLib;
using UXAssist.Common;
using UXAssist.Common.ModCompat;
using UXAssist.ModsCompat;
using UXAssist.Patches.Logistics;

namespace ModsPatch
{
    // UXAssist 1.6.3 (Thunderstore).
    // The starmap filter fix of the same branch is in BUG_UIFix.
    public static class UXAssistFixes
    {
        public const string Target = "org.soardev.uxassist";

        public static bool AlreadyFixed() => typeof(LogisticsPatch).GetMethod("MissingWorkers", AccessTools.all) != null;

        // Auxilaryfunction 3.1 keeps its station overlay switch in AuxConfig; UXAssist looked only in the plugin class
        // ("Failed to get ShowStationInfo from Auxilaryfunction") and lost the logistics info panel toggle.
        [HarmonyPrefix, HarmonyPatch(typeof(AuxilaryfunctionWrapper), nameof(AuxilaryfunctionWrapper.Start))]
        static bool AuxStart(Harmony harmony)
        {
            const string guid = "cn.blacksnipe.dsp.Auxilaryfunction";
            if (!ModCompatHelper.TryGetLoadedPluginInfo(guid, out var pluginInfo)) return false;
            if (!ModCompatHelper.TryGetPluginType(pluginInfo, "Auxilaryfunction.Auxilaryfunction", out var classType))
            {
                UXAssist.UXAssist.Logger.LogWarning("Failed to locate Auxilaryfunction main type");
                return false;
            }
            var configType = pluginInfo.Instance.GetType().Assembly.GetType("Auxilaryfunction.AuxConfig") ?? classType;
            if (!ModCompatHelper.TryGetFieldValue<ConfigEntry<bool>>(configType, "ShowStationInfo", pluginInfo.Instance, out AuxilaryfunctionWrapper.ShowStationInfo))
                UXAssist.UXAssist.Logger.LogWarning("Failed to get ShowStationInfo from Auxilaryfunction");
            if (!ModCompatHelper.TryGetPluginType(pluginInfo, "Auxilaryfunction.Patch.SpeedUpPatch", out var speedUpPatchType))
            {
                UXAssist.UXAssist.Logger.LogWarning("Failed to locate Auxilaryfunction SpeedUpPatch");
                return false;
            }
            if (!ModCompatHelper.TryGetPropertySetter(speedUpPatchType, "Enable", out var setter))
            {
                UXAssist.UXAssist.Logger.LogWarning("Failed to resolve SpeedUpPatch.set_Enable() from Auxilaryfunction");
                return false;
            }
            harmony.Patch(setter, new HarmonyMethod(AccessTools.Method(typeof(AuxilaryfunctionWrapper), nameof(AuxilaryfunctionWrapper.PatchSpeedUpPatchEnable))));
            return false;
        }

        // Auto-fill of new stations and dispensers: only for the player's own factory (a build event on another planet
        // took items from the local inventory), never above the worker arrays (LogisticsBotsTweaks may enlarge them,
        // ships are capped at 64 by the game), and without overflow.
        public static int MissingWorkers(int target, int capacity, int idle, int working) =>
            idle < 0 || working < 0 ? 0 : (int)Math.Max(0L, (long)Math.Max(0, Math.Min(target, capacity)) - idle - working);

        static bool Local(PlanetFactory factory) => factory != null && GameMain.mainPlayer != null && GameMain.mainPlayer.factory == factory;

        [HarmonyPrefix, HarmonyPatch(typeof(LogisticsPatch), nameof(LogisticsPatch.StationFillDrones))]
        static bool StationFillDrones(PlanetFactory factory, StationComponent station)
        {
            if (!Local(factory) || station?.workDroneDatas == null) return false;
            int target = station.isStellar ? LogisticsPatch.AutoConfigILSDroneCount.Value : LogisticsPatch.AutoConfigPLSDroneCount.Value;
            int toFill = MissingWorkers(target, station.workDroneDatas.Length, station.idleDroneCount, station.workDroneCount);
            if (toFill > 0) station.idleDroneCount += GameMain.data.mainPlayer.package.TakeItem((int)KnownItemId.Drone, toFill, out _);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LogisticsPatch), nameof(LogisticsPatch.StationFillShips))]
        static bool StationFillShips(PlanetFactory factory, StationComponent station)
        {
            if (!Local(factory) || station?.workShipDatas == null) return false;
            int toFill = MissingWorkers(LogisticsPatch.AutoConfigILSShipCount.Value, Math.Min(64, station.workShipDatas.Length), station.idleShipCount, station.workShipCount);
            if (toFill > 0) station.idleShipCount += GameMain.data.mainPlayer.package.TakeItem((int)KnownItemId.Ship, toFill, out _);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LogisticsPatch), nameof(LogisticsPatch.DispenserFillCouriers))]
        static bool DispenserFillCouriers(PlanetFactory factory, DispenserComponent dispenser)
        {
            if (!Local(factory) || dispenser?.workCourierDatas == null) return false;
            int toFill = MissingWorkers(LogisticsPatch.AutoConfigDispenserCourierCount.Value, dispenser.workCourierDatas.Length, dispenser.idleCourierCount, dispenser.workCourierCount);
            if (toFill > 0) dispenser.idleCourierCount += GameMain.data.mainPlayer.package.TakeItem((int)KnownItemId.Bot, toFill, out _);
            return false;
        }
    }
}

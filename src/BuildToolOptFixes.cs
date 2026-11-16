using System;
using BuildToolOpt;
using HarmonyLib;

namespace ModsPatch
{
    // BuildToolOpt 1.2.0 (Thunderstore).
    public static class BuildToolOptFixes
    {
        public const string Target = "starfi5h.plugin.BuildToolOpt";

        public static bool AlreadyFixed() => typeof(ReplaceStationLogic).GetField("replacingTransport", AccessTools.all) != null;

        static PlanetTransport replacingTransport;
        static int replacingStationId;

        // The refund is suppressed only for the station being replaced (any other dismantle during the replace,
        // or another mod's earlier skip, is left alone).
        [HarmonyPrefix, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.TakeBackItems_Station))]
        static bool TakeBack(PlanetTransport __0, int __1, ref bool __2)
        {
            if (!__2 || !ReplaceStationLogic.IsReplacing || ReplaceStationLogic.ReplacingGhost || !ReferenceEquals(__0, replacingTransport) || __1 != replacingStationId) return false;
            __2 = false;
            StationComponent station = __1 == 0 ? null : __0.GetStationComponent(__1);
            if (station == null || station.id != __1) return false;
            if (station.storage != null)
                for (int i = 0; i < station.storage.Length; i++)
                    if (station.storage[i].count > 0 && station.storage[i].itemId > 0) { station.storage[i].count = 0; station.storage[i].inc = 0; }
            station.idleDroneCount = 0;
            station.idleShipCount = 0;
            station.warperCount = 0;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.ReplaceStation))]
        static void ReplaceStationStart(BuildTool_Click tool, int stationId)
        {
            replacingTransport = tool.factory.transport;
            replacingStationId = stationId;
        }

        // An exception during the replace left the flags set, and every later dismantle lost its refund.
        [HarmonyFinalizer, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.ReplaceStation))]
        static void ReplaceStationEnd()
        {
            ReplaceStationLogic.ReplacingGhost = false;
            ReplaceStationLogic.IsReplacing = false;
            replacingTransport = null;
            replacingStationId = 0;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.ReplaceStationPrebuild))]
        static void ReplacePrebuildEnd()
        {
            ReplaceStationLogic.ReplacingGhost = false;
            ReplaceStationLogic.IsReplacing = false;
        }

        // _tmp_cols is read up to its length (a full array ran past the end).
        [HarmonyPrefix, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.GetOverlapStationEntityId))]
        static bool OverlapEntity(BuildTool_Click tool, ref int __result)
        {
            __result = 0;
            PlanetPhysics physics = tool.player.planetData.physics;
            var cols = BuildTool._tmp_cols;
            for (int i = 0; i < cols.Length && cols[i] != null; i++)
                if (physics.GetColliderData(cols[i], out ColliderData c) && c.objType == EObjectType.Entity && tool.factory.entityPool[c.objId].stationId > 0)
                {
                    __result = c.objId;
                    break;
                }
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.GetOverlapStationPrebuildId))]
        static bool OverlapPrebuild(BuildTool_Click tool, ref int __result)
        {
            __result = 0;
            PlanetPhysics physics = tool.player.planetData.physics;
            var cols = BuildTool._tmp_cols;
            for (int i = 0; i < cols.Length && cols[i] != null; i++)
            {
                if (!physics.GetColliderData(cols[i], out ColliderData c) || c.objType != EObjectType.Prebuild) continue;
                PrebuildData prebuild = tool.factory.prebuildPool[c.objId];
                if (prebuild.id != c.objId) continue;
                ItemProto proto = LDB.items.Select(prebuild.protoId);
                if (proto != null && proto.prefabDesc.isStation && !proto.prefabDesc.isVeinCollector && !proto.prefabDesc.isCollectStation)
                {
                    __result = c.objId;
                    break;
                }
            }
            return false;
        }

        // Drones, ships and warpers already put into the new station (by another mod) are kept; only the real excess
        // is refunded, ships capped at 64 like the game.
        [HarmonyPrefix, HarmonyPatch(typeof(ReplaceStationLogic), nameof(ReplaceStationLogic.LoadState))]
        static bool LoadState(StationComponent station, ref ReplaceStationLogic.StationState state)
        {
            station.energy = state.energy;
            station.warperCount = MergeStock(state.warperCount, station.warperCount, station.warperMaxCount, out int excess);
            ReplaceStationLogic.RefundItem(1210, excess, 0, station.entityId);
            station.idleDroneCount = MergeStock(state.droneCount, station.idleDroneCount,
                Math.Max(0, (station.workDroneDatas?.Length ?? 0) - station.workDroneCount), out excess);
            ReplaceStationLogic.RefundItem(5001, excess, 0, station.entityId);
            station.idleShipCount = MergeStock(state.shipCount, station.idleShipCount,
                Math.Max(0, Math.Min(64, station.workShipDatas?.Length ?? 0) - station.workShipCount), out excess);
            ReplaceStationLogic.RefundItem(5002, excess, 0, station.entityId);

            int length = Math.Min(station.storage.Length, state.storage.Length);
            for (int i = 0; i < length; i++)
            {
                station.storage[i].count = state.storage[i].count;
                station.storage[i].inc = state.storage[i].inc;
            }
            for (int i = length; i < state.storage.Length; i++)
                ReplaceStationLogic.RefundItem(state.storage[i].itemId, state.storage[i].count, state.storage[i].inc, station.entityId);
            return false;
        }

        public static int MergeStock(int saved, int added, int capacity, out int excess)
        {
            long total = (long)Math.Max(0, saved) + Math.Max(0, added);
            int kept = (int)Math.Min(total, Math.Max(0, capacity));
            excess = (int)Math.Min(int.MaxValue, total - kept);
            return kept;
        }
    }
}

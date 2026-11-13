using System;
using System.Collections.Generic;
using System.Reflection;
using Auxilaryfunction;
using Auxilaryfunction.Models;
using Auxilaryfunction.Services;
using HarmonyLib;
using Aux = Auxilaryfunction.Auxilaryfunction;

namespace ModsPatch
{
    // Auxilaryfunction 3.1.1 (Thunderstore).
    // Auto-fill only for the player's own factory, never above the station / dispenser capacity, taking from all player
    // packages (inventory, hand, logistics slots); star fuel of any fuel item the generator accepts.
    public static class AuxilaryfunctionFixes
    {
        public const string Target = "cn.blacksnipe.dsp.Auxilaryfunction";

        public static bool AlreadyFixed() => typeof(Aux).Assembly.GetType("Auxilaryfunction.NativeInventory") != null;

        public static int Missing(int capacity, int idle, int working, int target)
            => (int)Math.Max(0L, Math.Min((long)Math.Max(0, capacity), Math.Max(0, target)) - Math.Max(0L, idle) - Math.Max(0L, working));

        static bool HasItem(Player player, int itemId)
        {
            if (player == null || itemId <= 0) return false;
            if (player.package.GetItemCount(itemId) > 0 || (player.inhandItemId == itemId && player.inhandItemCount > 0)) return true;
            var delivery = player.deliveryPackage;
            if (delivery == null || !delivery.unlocked) return false;
            for (int i = 0; i < delivery.grids.Length; i++)
                if (delivery.IsGridActive(i) && delivery.grids[i].itemId == itemId && delivery.grids[i].count > 0) return true;
            return false;
        }

        static int Take(Player player, int itemId, int requested)
        {
            if (player?.packageUtility == null || requested <= 0) return 0;
            int count = requested;
            player.packageUtility.TryTakeItemFromAllPackages(ref itemId, ref count, out _, true);
            return count;
        }

        static int Take(Player player, int itemId, int requested, out int inc)
        {
            inc = 0;
            if (player?.packageUtility == null || requested <= 0) return 0;
            int count = requested;
            player.packageUtility.TryTakeItemFromAllPackages(ref itemId, ref count, out inc, true);
            return count;
        }

        static void FillFuel(Player player, PlanetFactory factory, int generatorId, int itemId, int target)
        {
            if (player == null || player.factory != factory || factory?.powerSystem == null || generatorId <= 0 || generatorId >= factory.powerSystem.genCursor) return;
            ref var generator = ref factory.powerSystem.genPool[generatorId];
            var fuel = LDB.items.Select(itemId);
            if (target <= generator.fuelCount) return;
            if (generator.id != generatorId || generator.fuelMask != 4 || fuel == null || itemId > short.MaxValue ||
                fuel.HeatValue <= 0 || (fuel.FuelType & generator.fuelMask) == 0 || (generator.fuelCount > 0 && generator.fuelId != itemId)) return;
            int amount = Math.Min(Math.Max(0, target - generator.fuelCount), Math.Min(short.MaxValue - generator.fuelCount, (short.MaxValue - generator.fuelInc) / 10));
            int added = Take(player, itemId, amount, out int inc);
            if (added <= 0) return;
            if (generator.fuelCount == 0) generator.SetNewFuel(itemId, (short)added, (short)inc);
            else { generator.fuelCount += (short)added; generator.fuelInc += (short)inc; }
        }

        static bool Own(PlanetFactory factory) => Aux.player != null && factory != null && Aux.player.factory == factory;

        [HarmonyPrefix, HarmonyPatch(typeof(AuxilaryfunctionPatch), nameof(AuxilaryfunctionPatch.NewGeneratorComponentPatch))]
        static bool NewGenerator(ref int __0, PowerSystem __1)
        {
            if (Own(__1.factory) && AuxConfig.autosetSomevalue_bool.Value)
                FillFuel(Aux.player, __1.factory, __0, AuxConfig.auto_supply_starfuelID.Value, AuxConfig.auto_supply_starfuel.Value);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(AuxilaryfunctionPatch), nameof(AuxilaryfunctionPatch.NewStationComponentPatch))]
        static bool NewStation(ref StationComponent __0, PlanetTransport __1)
        {
            StationComponent s = __0;
            if (s == null || !Own(__1.factory)) return false;
            Player player = Aux.player;
            if (AuxConfig.auto_supply_station.Value && !s.isCollector && !s.isVeinCollector)
            {
                if (BuildTargetValues.IsConfigEnabled("填充飞机数量"))
                    s.idleDroneCount += Take(player, 5001, Missing(s.workDroneDatas?.Length ?? 0, s.idleDroneCount, s.workDroneCount, AuxConfig.auto_supply_drone.Value));
                if (BuildTargetValues.IsConfigEnabled("运输机最远路程"))
                    s.tripRangeDrones = Math.Cos(AuxConfig.stationdronedist.Value * Math.PI / 180.0);
                if (BuildTargetValues.IsConfigEnabled("最大充电功率"))
                {
                    long perTick = (long)AuxConfig.stationmaxpowerpertick.Value * 16667;
                    if (AuxConfig.stationmaxpowerpertick.Value > 60f && !s.isStellar) perTick = 60L * 16667;
                    __1.planet.factory.powerSystem.consumerPool[s.pcId].workEnergyPerTick = perTick;
                }
                if (BuildTargetValues.IsConfigEnabled("运输机起送量"))
                    s.deliveryDrones = AuxConfig.DroneStartCarry.Value == 0f ? 1 : (int)(AuxConfig.DroneStartCarry.Value * 10f) * 10;
                if (s.isStellar)
                {
                    if (BuildTargetValues.IsConfigEnabled("翘曲填充数量"))
                        s.warperCount += Take(player, 1210, Missing(int.MaxValue, s.warperCount, 0, AuxConfig.auto_supply_warp.Value));
                    if (BuildTargetValues.IsConfigEnabled("曲速启用路程"))
                        s.warpEnableDist = AuxConfig.stationwarpdist.Value * GalaxyData.AU;
                    if (BuildTargetValues.IsConfigEnabled("运输船起送量"))
                        s.deliveryShips = AuxConfig.ShipStartCarry.Value == 0f ? 1 : (int)(AuxConfig.ShipStartCarry.Value * 10f) * 10;
                    if (BuildTargetValues.IsConfigEnabled("填充飞船数量"))
                        s.idleShipCount += Take(player, 5002, Missing(Math.Min(64, s.workShipDatas?.Length ?? 0), s.idleShipCount, s.workShipCount, AuxConfig.auto_supply_ship.Value));
                    if (BuildTargetValues.IsConfigEnabled("运输船最远路程"))
                        s.tripRangeShips = AuxConfig.stationshipdist.Value > 60 ? 24000000000L : AuxConfig.stationshipdist.Value * 2400000L;
                }
            }
            if (AuxConfig.auto_supply_station.Value && s.isVeinCollector && BuildTargetValues.IsConfigEnabled("大型采矿机采矿速率"))
                __1.factory.factorySystem.minerPool[s.minerId].speed = AuxConfig.veincollectorspeed.Value * 1000;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(AuxilaryfunctionPatch), nameof(AuxilaryfunctionPatch.NewDispenserComponentPatch))]
        static bool NewDispenser(int __0, PlanetTransport __1)
        {
            if (__0 <= 0 || !Own(__1.factory) || !AuxConfig.autosetCourier_bool.Value || !BuildTargetValues.IsConfigEnabled("填充配送机数量")) return false;
            var d = __1.dispenserPool[__0];
            if (d != null)
                d.idleCourierCount += Take(Aux.player, 5003, Missing(Math.Min(d.workCourierDatas?.Length ?? 0, d.orders?.Length ?? 0), d.idleCourierCount, d.workCourierCount, AuxConfig.auto_supply_Courier.Value));
            return false;
        }

        static PlanetFactory OwnLocalFactory()
        {
            PlanetFactory f = AuxConfig.LocalPlanet?.factory;
            return Own(f) ? f : null;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LogisticsService), nameof(LogisticsService.AddCourierToAllStar))]
        static bool AddCourierToAllStar()
        {
            PlanetFactory f = OwnLocalFactory();
            if (f == null || !BuildTargetValues.IsConfigEnabled("填充配送机数量")) return false;
            foreach (var d in f.transport.dispenserPool)
            {
                if (d == null || d.id <= 0) continue;
                int need = Missing(Math.Min(d.workCourierDatas?.Length ?? 0, d.orders?.Length ?? 0), d.idleCourierCount, d.workCourierCount, AuxConfig.auto_supply_Courier.Value);
                if (need > 0) d.idleCourierCount += Take(Aux.player, 5003, need);
            }
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LogisticsService), nameof(LogisticsService.AddDroneShipToStation))]
        static bool AddDroneShipToStation()
        {
            PlanetFactory f = OwnLocalFactory();
            if (f == null || AuxConfig.LocalPlanet.type == EPlanetType.Gas) return false;
            bool drones = BuildTargetValues.IsConfigEnabled("填充飞机数量");
            bool ships = BuildTargetValues.IsConfigEnabled("填充飞船数量");
            bool warpers = BuildTargetValues.IsConfigEnabled("翘曲填充数量");
            foreach (StationComponent s in f.transport.stationPool)
            {
                if (s == null || s.isVeinCollector) continue;
                if (drones)
                {
                    int need = Missing(s.workDroneDatas?.Length ?? 0, s.idleDroneCount, s.workDroneCount, AuxConfig.auto_supply_drone.Value);
                    if (need > 0) s.idleDroneCount += Take(Aux.player, 5001, need);
                }
                if (ships && s.isStellar)
                {
                    int need = Missing(Math.Min(64, s.workShipDatas?.Length ?? 0), s.idleShipCount, s.workShipCount, AuxConfig.auto_supply_ship.Value);
                    if (need > 0) s.idleShipCount += Take(Aux.player, 5002, need);
                }
                if (warpers)
                {
                    int need = Missing(int.MaxValue, s.warperCount, 0, AuxConfig.auto_supply_warp.Value);
                    if (need > 0) s.warperCount += Take(Aux.player, 1210, need);
                }
            }
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LogisticsService), nameof(LogisticsService.AddFuelToAllStar))]
        static bool AddFuelToAllStar()
        {
            PlanetFactory f = OwnLocalFactory();
            if (f?.powerSystem == null) return false;
            for (int i = 1; i < f.powerSystem.genCursor; i++)
                FillFuel(Aux.player, f, i, AuxConfig.auto_supply_starfuelID.Value, AuxConfig.auto_supply_starfuel.Value);
            return false;
        }

        // "Move to unbuilt" also sees items in the hand and in the logistics slots.
        [HarmonyTranspiler, HarmonyPatch(typeof(Aux), nameof(Aux.AutoMovetounbuilt))]
        static IEnumerable<CodeInstruction> AutoMove(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getCount = AccessTools.Method(typeof(StorageComponent), nameof(StorageComponent.GetItemCount), new[] { typeof(int) });
            int n = 0;
            foreach (var ins in instructions)
            {
                if (ins.Calls(getCount)) { ins.opcode = System.Reflection.Emit.OpCodes.Call; ins.operand = AccessTools.Method(typeof(AuxilaryfunctionFixes), nameof(PlayerItemCount)); n++; }
                yield return ins;
            }
            Il.Require(n == 2, "Auxilaryfunction AutoMovetounbuilt GetItemCount x2");
        }

        static int PlayerItemCount(StorageComponent package, int itemId)
        {
            Player player = GameMain.mainPlayer;
            if (player != null && ReferenceEquals(package, player.package)) return HasItem(player, itemId) ? 1 : 0;
            return package.GetItemCount(itemId);
        }
    }
}

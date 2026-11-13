using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PlanetFinderMod;
using UnityEngine;
using UnityEngine.UI;

namespace ModsPatch
{
    // PlanetFinder 1.3.2 (Thunderstore).
    // The resource filter buttons (list and layout) are in BUG_UIFix.
    public static class PlanetFinderFixes
    {
        public const string Target = PLFN.__GUID__;

        public static bool AlreadyFixed() => typeof(PLFN).Assembly.GetType("PlanetFinderMod.ResourceList") != null;

        const int FirstItemId = 1000;  // vein types are below, item ids (liquids, gases) from here

        static bool IsVeinType(int id) => id < FirstItemId;

        static bool PlanetHasFluid(PlanetData planet, int itemId) =>
            itemId >= FirstItemId && (planet.waterItemId == itemId || (planet.gasItems != null && Array.IndexOf(planet.gasItems, itemId) >= 0));

        // Ocean liquids and atmosphere gases match on any planet (GenesisBook has atmospheres on rocky planets).
        [HarmonyPrefix, HarmonyPatch(typeof(UIPlanetFinderWindow), nameof(UIPlanetFinderWindow.IsTargetPlanet), typeof(PlanetListData), typeof(int))]
        static bool IsTargetPlanet(PlanetListData planetListData, int itemId, ref bool __result)
        {
            PlanetData planet = planetListData.planetData;
            __result = planet != null && (PlanetHasFluid(planet, itemId)
                || (planet.type != EPlanetType.Gas && IsVeinType(itemId) && planetListData.IsContainItem(itemId)));
            return false;
        }

        // Star rows have no planetData; they used to throw here.
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetListData), nameof(PlanetListData.IsCalculatedOrLoaded))]
        static bool IsCalculatedOrLoaded(PlanetListData __instance, ref bool __result)
        {
            __result = (__instance.planetData != null && __instance.planetData.scanned) || __instance.cachedVeinGroups != null;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetListData), nameof(PlanetListData.VeinGroups))]
        static bool VeinGroups(PlanetListData __instance, ref VeinGroup[] __result)
        {
            PlanetData p = __instance.planetData;
            __result = p == null || !p.scanned ? __instance.cachedVeinGroups : p.runtimeVeinGroups;
            return false;
        }

        // Large galaxies overflowed the sums (negative amounts).
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetListData), nameof(PlanetListData.ItemCount))]
        static bool ItemCount(PlanetListData __instance, int itemId, ref long __result)
        {
            long amount = 0;
            VeinGroup[] groups = __instance.VeinGroups();
            if (groups != null)
                for (int i = 1; i < groups.Length; i++)
                    if ((int)groups[i].type == itemId)
                    {
                        long add = Math.Max(0L, groups[i].amount);
                        amount = add > long.MaxValue - amount ? long.MaxValue : amount + add;
                    }
            __result = amount;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetListData), nameof(PlanetListData.ItemCountSketch))]
        static bool ItemCountSketch(PlanetListData __instance, int itemId, ref int __result)
        {
            long amount = 0;
            VeinGroup[] groups = __instance.VeinGroups();
            if (groups != null)
                for (int i = 1; i < groups.Length; i++)
                    if ((int)groups[i].type == itemId) amount = Math.Min(int.MaxValue, amount + Math.Max(0, groups[i].count));
            __result = (int)amount;
            return false;
        }

        // Counts every collector station, also mod collectors like GenesisBook's atmospheric collector.
        [HarmonyPrefix, HarmonyPatch(typeof(UIPlanetFinderListItem), nameof(UIPlanetFinderListItem.OrbitalCollectorCount))]
        static bool OrbitalCollectorCount(PlanetListData ld, ref int __result)
        {
            int count = 0;
            foreach (PlanetData pd in UIPlanetFinderListItem.CollectPlanets(ld))
            {
                PlanetTransport transport = pd?.factory?.transport;
                StationComponent[] pool = transport?.stationPool;
                if (pool == null) continue;
                int cursor = Math.Min(transport.stationCursor, pool.Length);
                for (int i = 1; i < cursor; i++)
                    if (pool[i] != null && pool[i].id == i && pool[i].isCollector) count++;
            }
            __result = count;
            return false;
        }

        // A star without planets is ranked by the star itself (it threw on planets[0]).
        [HarmonyPrefix, HarmonyPatch(typeof(UIPlanetFinderWindow), nameof(UIPlanetFinderWindow.SetupDistanceForSort))]
        static bool SetupDistanceForSort(PlanetListData d, int nearStar)
        {
            PlanetData planet = d.planetData;
            if (planet == null && d.starData?.planets != null && d.starData.planets.Length > 0) planet = d.starData.planets[0];
            int starId, planetIndex, planetId;
            if (planet != null) { starId = planet.star.id; planetIndex = planet.index; planetId = planet.id; }
            else if (d.starData != null) { starId = d.starData.id; planetIndex = 0; planetId = d.starData.id * 100; }
            else return false;

            int localPlanetId = GameMain.localPlanet != null ? GameMain.localPlanet.id : 0;
            string distanceStr = "";
            int distanceForSort;
            if (localPlanetId == planetId) distanceForSort = 0;
            else if (localPlanetId / 100 == planetId / 100) distanceForSort = planetIndex + 40;
            else
            {
                float distancef = StarDistance.DistanceFromHere(starId, nearStar);
                distanceForSort = (int)(distancef * 10000f) + starId * 100 + planetIndex;
                distanceStr = string.Format("  ({0:F1}ly)", distancef);
            }
            if (d.distanceForSort >= 0) distanceForSort = d.distanceForSort;
            d.distanceForSort = distanceForSort;
            d.distanceStr = distanceStr;
            return false;
        }
    }
}

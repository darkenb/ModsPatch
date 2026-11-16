using HarmonyLib;
using LSTMMod;
using UnityEngine;

namespace ModsPatch
{
    // LSTM 0.9.6 (Thunderstore). Sent upstream as hetima/DSP_LSTM#47; not in 0.9.6.
    // The game nulls a removed station's stationPool slot and recycles its gid, so traffic log entries can point at
    // nothing or at a newer station on another planet.
    public static class LSTMFixes
    {
        public const string Target = "com.hetima.dsp.LSTM";

        public static bool AlreadyFixed() => AccessTools.Method(typeof(Util), "FindLoggedStation") != null;

        public static StationComponent FindStation(GalacticTransport transport, int gid)
        {
            if (transport?.stationPool == null || gid <= 0 || gid >= transport.stationPool.Length) return null;
            StationComponent station = transport.stationPool[gid];
            return station != null && station.gid == gid ? station : null;
        }

        // The station on the demand (or supply) side of a log entry, only while it is still on the logged planet.
        public static StationComponent FindLoggedStation(GalacticTransport transport, TrafficLogData log, bool demand)
        {
            if (log == null) return null;
            bool from = demand == log.isFromDemand;
            StationComponent station = FindStation(transport, from ? log.fromStationGid : log.toStationGid);
            return station != null && station.planetId == (from ? log.fromPlanet : log.toPlanet) ? station : null;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UILogListItem), nameof(UILogListItem.DemandStation))]
        static bool DemandStation(UILogListItem __instance, ref StationComponent __result)
        {
            __result = FindLoggedStation(GameMain.data?.galacticTransport, __instance.logData, true);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UILogListItem), nameof(UILogListItem.SupplyStation))]
        static bool SupplyStation(UILogListItem __instance, ref StationComponent __result)
        {
            __result = FindLoggedStation(GameMain.data?.galacticTransport, __instance.logData, false);
            return false;
        }

        // "Filter demand/supply station" always took fromStationGid/toStationGid; the demand side is "from" only when the
        // trip started at the demand station. Swapping the command picks the right side.
        const int FilterDemandStation = 4, FilterSupplyStation = 5;

        [HarmonyPrefix, HarmonyPatch(typeof(UILogWindow), nameof(UILogWindow.OnMenuSelect))]
        static void OnMenuSelect(ref int obj, UILogListItem ___menuTarget)
        {
            if ((obj == FilterDemandStation || obj == FilterSupplyStation) && ___menuTarget?.logData != null && !___menuTarget.logData.isFromDemand)
                obj = FilterDemandStation + FilterSupplyStation - obj;
        }

        // Window header: a removed station (null slot) threw at station.gid, and a missing star, planet or item threw at
        // displayName/name. Same text and buttons as the original otherwise.
        [HarmonyPrefix, HarmonyPatch(typeof(UILogWindow), nameof(UILogWindow.SetUpItemUI))]
        static bool SetUpItemUI(UILogWindow __instance)
        {
            var w = __instance;
            if (w.targetItemId <= 0)
            {
                w.itemCircle.fillAmount = 0f;
                w.itemResetButton.gameObject.SetActive(false);
                w.itemImage.sprite = UILogWindow.defaultItemSprite;
            }
            else
            {
                w.itemCircle.fillAmount = 1f;
                w.itemResetButton.gameObject.SetActive(true);
                ItemProto item = LDB.items.Select(w.targetItemId);
                if (item != null) w.itemImage.sprite = item.iconSprite;
            }

            string planetName, stationName = "";
            bool planetButton = true;
            if (w.targetStarId != 0) planetName = (GameMain.galaxy?.StarById(w.targetStarId)?.displayName ?? "") + "空格行星系".Translate();
            else if (w.targetPlanetId > 0) planetName = GameMain.galaxy?.PlanetById(w.targetPlanetId)?.displayName ?? "";
            else
            {
                planetName = w.targetItemId != 0 ? LDB.items.Select(w.targetItemId)?.name ?? "" : "All Traffic Log";
                planetButton = false;
            }
            w.planetResetButton.gameObject.SetActive(planetButton);

            StationComponent station = w.targetStationGid != 0 ? FindStation(UIRoot.instance?.uiGame?.gameData?.galacticTransport, w.targetStationGid) : null;
            if (station != null)
            {
                stationName = station.isStellar ? "星际站点号".Translate() + station.gid : "本地站点号".Translate() + station.id;
                planetName = "(" + (GameMain.galaxy?.PlanetById(station.planetId)?.displayName ?? "") + ")";
            }
            w.stationResetButton.gameObject.SetActive(station != null);

            Place(w.planetText, w.planetResetButton, planetName);
            Place(w.stationText, w.stationResetButton, stationName);
            return false;
        }

        static void Place(UnityEngine.UI.Text text, Component button, string value)
        {
            if (text == null) return;
            text.text = value;
            var rect = (RectTransform)button.gameObject.transform;
            rect.anchoredPosition = new Vector2(text.preferredWidth / 2 + 18f, rect.anchoredPosition.y);
        }
    }
}

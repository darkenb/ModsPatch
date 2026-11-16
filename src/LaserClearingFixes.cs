using System;
using System.Collections.Generic;
using HarmonyLib;
using LaserClearing;
using UnityEngine;

namespace ModsPatch
{
    // LaserClearing 1.0.8 (Thunderstore).
    // The laser loop keeps its own state here: which laser hits which plant, on which factory and skill system.
    public static class LaserClearingFixes
    {
        public const string Target = "starfi5h.plugin.LaserClearing";

        public static bool AlreadyFixed() => typeof(LocalLaser_Patch).GetField("laserTargets", AccessTools.all) != null;

        static readonly List<int> laserIds = new();
        static readonly Dictionary<int, int> laserTargets = new();  // laser id -> plant id
        static PlanetFactory ownerFactory;
        static SkillSystem ownerSkillSystem;
        [ThreadStatic] static bool enemyKill;

        // Lasers stop when the inventory is full (not only new ones), damage follows the energy the mecha has,
        // plants are re-checked on every pick (no stale cache of reused plant slots), and a plant is never targeted twice.
        [HarmonyPrefix, HarmonyPatch(typeof(LocalLaser_Patch), "GameTick")]
        // LaserClearing's methods are static with a parameter named __instance: Harmony would fill that name with null, so __0.
        static bool GameTick(PlayerAction_Mine __0)
        {
            PlayerAction_Mine __instance = __0;
            if (!LocalLaser_Patch.Enable) return false;
            PlanetFactory factory = __instance.player.factory;
            if (factory == null) { ClearAll(); return false; }
            if (!ReferenceEquals(ownerFactory, factory)) ClearAll();
            if (!LocalLaser_Patch.CheckPlayerInventorySpace())
            {
                ClearAll();
                UI_Patch.UpdateButtonStatus(UI_Patch.ButtonStatus.NotEnoughSpace);
                return false;
            }
            UI_Patch.UpdateButtonStatus(UI_Patch.ButtonStatus.Normal);
            ownerFactory = factory;
            Mecha mecha = __instance.player.mecha;
            Vector3 beginPos = mecha.skillCastRightL;

            if (laserIds.Count < LocalLaser_Patch.maxLaserCount && GameMain.gameTick % LocalLaser_Patch.checkIntervalTick == 0)
            {
                int vegeId = ClosestPlant(factory, beginPos, LocalLaser_Patch.Range);
                if (vegeId != 0) laserIds.Add(StartLaser(factory, vegeId, beginPos));
            }
            for (int i = laserIds.Count - 1; i >= 0; i--)
                if (!ContinueLaser(factory, laserIds[i], beginPos, mecha))
                {
                    laserTargets.Remove(laserIds[i]);
                    laserIds.RemoveAt(i);
                }
            return false;
        }

        // Only this mod's own lasers are stopped (other lasers and combat are left alone).
        [HarmonyPrefix, HarmonyPatch(typeof(LocalLaser_Patch), nameof(LocalLaser_Patch.ClearAll))]
        static bool ClearAll()
        {
            if (ownerSkillSystem != null && ownerFactory != null)
            {
                ref var pool = ref ownerSkillSystem.localLaserContinuous;
                if (pool?.buffer != null)
                    foreach (var id in laserIds)
                    {
                        if (id <= 0 || id >= pool.cursor || id >= pool.buffer.Length) continue;
                        ref var laser = ref pool.buffer[id];
                        if (laser.id == id && laser.astroId == ownerFactory.planetId && laser.target.type == ETargetType.Vegetable
                            && laserTargets.TryGetValue(id, out var vege) && laser.target.id == vege) laser.Stop(ownerSkillSystem);
                    }
            }
            laserIds.Clear();
            laserTargets.Clear();
            ownerFactory = null;
            ownerSkillSystem = null;
            return false;
        }

        // Runs before LaserClearing's prefix: plants killed by enemies keep the game's handling.
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.KillVegeFinally))]
        static void MarkCaster(ref CombatStat combatStat) => enemyKill = combatStat.lastCaster.type == ETargetType.Enemy;

        // Only plants hit by this mod's lasers get the mod's loot; the plant also counts for the vegetation collection.
        [HarmonyPrefix, HarmonyPatch(typeof(LocalLaser_Patch), nameof(LocalLaser_Patch.KillVegeFinally_Prefix))]
        static bool KillVege(PlanetFactory __0, int id, ref bool __result)
        {
            PlanetFactory __instance = __0;
            __result = true;
            if (enemyKill || !LocalLaser_Patch.Enable || !ReferenceEquals(__instance, ownerFactory) || !laserTargets.ContainsValue(id)) return false;
            if (id <= 0 || id >= __instance.vegePool.Length || __instance.vegePool[id].id != id) return false;

            ref var vegeData = ref __instance.vegePool[id];
            VegeProto vegeProto = LDB.veges.Select(vegeData.protoId);
            if (vegeProto != null)
            {
                VFEffectEmitter.Emit(vegeProto.MiningEffect, vegeData.pos, vegeData.rot);
                if (LocalLaser_Patch.EnableDestructionSFX) VFAudio.Create(vegeProto.MiningAudio, null, vegeData.pos, true, 1, -1, -1L);
                if (LocalLaser_Patch.EnableLoot) LocalLaser_Patch.GetVegLoot(__instance, vegeData, vegeProto);
            }
            __instance.gameData.mainPlayer.vegetableCollection.AddVegeToPlayer(vegeData.protoId);
            __instance.RemoveVegeWithComponents(id);
            __result = false;
            return false;
        }

        static int StartLaser(PlanetFactory factory, int vegeId, Vector3 beginPos)
        {
            ownerSkillSystem = GameMain.data.spaceSector.skillSystem;
            ref LocalLaserContinuous ptr = ref ownerSkillSystem.localLaserContinuous.Add();
            ref VegeData vegeData = ref factory.vegePool[vegeId];
            ptr.Start();
            ptr.astroId = factory.planetId;
            ptr.hitIndex = 4;
            ptr.beginPos = vegeData.pos + vegeData.pos.normalized * SkillSystem.RoughHeightByModelIndex[vegeData.modelIndex] * 0.5f;
            ptr.endPos = beginPos;
            ptr.target.type = ETargetType.Vegetable;
            ptr.target.id = vegeId;
            int maxHp = SkillSystem.HpMaxByModelIndex[vegeData.modelIndex];
            int recoverHp = SkillSystem.HpRecoverByModelIndex[vegeData.modelIndex];
            ptr.damage = (int)((maxHp / LocalLaser_Patch.tickToClear + recoverHp) / 1.0f + 1);
            ptr.damageScale = 1.0f;
            ptr.mask = ETargetTypeMask.NotPlayer;
            ref SkillSFXHolder sfx = ref ownerSkillSystem.audio.AddPlanetAudio(123, 0f, ptr.astroId, ptr.beginPos);  // mining sound
            ptr.sfxId = sfx.id;
            laserTargets[ptr.id] = vegeId;
            return ptr.id;
        }

        static bool ContinueLaser(PlanetFactory factory, int laserId, Vector3 beginPos, Mecha mecha)
        {
            if (ownerSkillSystem == null || !ReferenceEquals(factory, ownerFactory)) return false;
            ref var pool = ref ownerSkillSystem.localLaserContinuous;
            if (pool?.buffer == null || laserId <= 0 || laserId >= pool.cursor || laserId >= pool.buffer.Length) return false;
            ref LocalLaserContinuous ptr = ref pool.buffer[laserId];
            if (!laserTargets.TryGetValue(laserId, out int vegeId) || ptr.id != laserId || ptr.target.type != ETargetType.Vegetable
                || ptr.target.id != vegeId || ptr.astroId != factory.planetId) return false;
            ptr.endPos = beginPos;
            if (vegeId <= 0 || vegeId >= factory.vegePool.Length) { ptr.Stop(ownerSkillSystem); return false; }
            ref VegeData vegeData = ref factory.vegePool[vegeId];
            if (vegeData.id == 0) { ptr.Stop(ownerSkillSystem); return false; }
            ptr.beginPos = vegeData.pos + vegeData.pos.normalized * SkillSystem.RoughHeightByModelIndex[vegeData.modelIndex] * 0.5f;
            // Range from the plant itself (the old check compared the laser end with the mecha, always in range).
            if (Vector3.SqrMagnitude(vegeData.pos - beginPos) > LocalLaser_Patch.Range * LocalLaser_Patch.Range) { ptr.Stop(ownerSkillSystem); return false; }
            mecha.QueryEnergy(LocalLaser_Patch.MiningPower, out double energyConsumed, out float energyRatio);
            mecha.coreEnergy -= energyConsumed;
            mecha.MarkEnergyChange(5, -energyConsumed);
            ptr.damageScale = energyRatio;
            return true;
        }

        static int ClosestPlant(PlanetFactory factory, Vector3 centerPos, float range)
        {
            int result = 0;
            VegeData[] vegePool = factory.vegePool;
            float minDist = range * range;
            HashSystem hash = factory.hashSystemStatic;
            hash.ClearActiveBuckets();
            int[] hashPool = hash.hashPool, bucketOffsets = hash.bucketOffsets, bucketCursors = hash.bucketCursors;
            hash.GetBucketIdxesInArea(centerPos, range);
            int[] activeBuckets = hash.activeBuckets;
            for (int k = 0; k < hash.activeBucketsCount; k++)
            {
                int bucket = activeBuckets[k];
                int end = bucketOffsets[bucket] + bucketCursors[bucket];
                for (int l = bucketOffsets[bucket]; l < end; l++)
                {
                    int address = hashPool[l];
                    if (address == 0 || address >> 28 != 1) continue;  // ETargetType.Vegetable
                    int vegeId = address & 268435455;
                    if (vegeId <= 0 || vegeId >= vegePool.Length || laserTargets.ContainsValue(vegeId)) continue;
                    ref VegeData vege = ref vegePool[vegeId];
                    if (vege.id != vegeId || (vege.protoId == 9999 && !LocalLaser_Patch.SpaceCapsule)) continue;
                    VegeProto proto = LDB.veges.Select(vege.protoId);
                    if (proto == null || (LocalLaser_Patch.DropOnly && (proto.MiningItem == null || proto.MiningItem.Length == 0))) continue;
                    float dist = Vector3.SqrMagnitude(vege.pos - centerPos);
                    if (dist < minDist) { result = vegeId; minDist = dist; }
                }
            }
            hash.ClearActiveBuckets();
            return result;
        }

        // Status updates before the toggle button exists are skipped (GameTick above calls them every tick).
        [HarmonyPrefix, HarmonyPatch(typeof(UI_Patch), nameof(UI_Patch.UpdateButtonStatus))]
        static bool NoButton() => UI_Patch.enableButton != null;
    }
}

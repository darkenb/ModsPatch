using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MoreMegaStructure;

namespace ModsPatch
{
    // MoreMegaStructure 2.0.3 (Thunderstore; the patched methods are unchanged since 2.0.2).
    // Interstellar assembly stats use virtual factory indices past GameData.factories; they are routed to MMS inside the
    // game's own calculator loop instead of a prefix that skipped the loop (and its begin/end callbacks) for that tick.
    public static class MoreMegaStructureFixes
    {
        public const string Target = "Gnimaerd.DSP.plugin.MoreMegaStructure";

        public static bool AlreadyFixed() => typeof(UIStatisticsPatcher).GetMethod("CalculatorFactoryPrefix", AccessTools.all) != null;

        // MMS's old prefix ran one virtual factory and returned false, skipping the native calculator tick.
        [HarmonyPrefix, HarmonyPatch(typeof(UIStatisticsPatcher), nameof(UIStatisticsPatcher.ProductionExtraInfoCalculator_GameTick_Prefix))]
        static bool DisableOldGameTickPrefix(ref bool __result)
        {
            __result = true;
            return false;
        }

        // Virtual indices get a proxy factory carrying only the index; GameData.factories is never written.
        public static PlanetFactory Lookup(GameData gameData, int factoryIndex)
        {
            if (factoryIndex >= 0 && factoryIndex < gameData.factories.Length) return gameData.factories[factoryIndex];
            if (factoryIndex >= gameData.factories.Length + gameData.galaxy.starCount) throw new ArgumentOutOfRangeException(nameof(factoryIndex));
            var proxy = new PlanetFactory();
            AccessTools.Property(typeof(PlanetFactory), "index").SetValue(proxy, factoryIndex, null);
            return proxy;
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(ProductionExtraInfoCalculator), nameof(ProductionExtraInfoCalculator.GameTick))]
        static IEnumerable<CodeInstruction> GameTick(IEnumerable<CodeInstruction> instructions) => ReplaceFactoryLookup(instructions);

        [HarmonyTranspiler, HarmonyPatch(typeof(ProductionExtraInfoCalculator), nameof(ProductionExtraInfoCalculator.CalculateImmediately))]
        static IEnumerable<CodeInstruction> CalculateImmediately(IEnumerable<CodeInstruction> instructions) => ReplaceFactoryLookup(instructions);

        static IEnumerable<CodeInstruction> ReplaceFactoryLookup(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo factories = AccessTools.Field(typeof(GameData), nameof(GameData.factories));
            int replacements = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (!code[i].LoadsField(factories) || !code[i + 1].IsLdloc() || code[i + 2].opcode != OpCodes.Ldelem_Ref) continue;
                // Opcodes are rewritten in place so branch labels and exception blocks stay where they are.
                code[i].opcode = OpCodes.Nop; code[i].operand = null;
                code[i + 2].opcode = OpCodes.Call; code[i + 2].operand = AccessTools.Method(typeof(MoreMegaStructureFixes), nameof(Lookup));
                replacements++;
            }
            Il.Require(replacements == 1, "ProductionExtraInfoCalculator factory lookup (" + replacements + ")");
            return code;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ProductionExtraInfoCalculator), nameof(ProductionExtraInfoCalculator.CalculateFactory))]
        static bool CalculateFactory(ProductionExtraInfoCalculator __instance, GameData gameData, PlanetFactory factory)
        {
            if (factory == null || factory.index < gameData.factories.Length) return true;
            UIStatisticsPatcher.CalculateFactory_IA(ref __instance, gameData, factory.index);
            return false;
        }

        // A star whose megastructure is no longer an interstellar assembly (or has no sphere) keeps only the cleared stats,
        // instead of its old reference speeds. The target's own parameter is named __instance, hence __0.
        [HarmonyPrefix, HarmonyPatch(typeof(UIStatisticsPatcher), nameof(UIStatisticsPatcher.CalculateFactory_IA))]
        static bool CalculateFactoryIA(ref ProductionExtraInfoCalculator __0, GameData gameData, int factoryIndex)
        {
            int star = UIStatisticsPatcher.FactoryIndex2StarIndex(factoryIndex);
            int[] types = MoreMegaStructure.MoreMegaStructure.StarMegaStructureType;
            if (star >= 0 && star < types.Length && types[star] == 4 && gameData.dysonSpheres != null
                && star < gameData.dysonSpheres.Length && gameData.dysonSpheres[star] != null) return true;
            FactoryProductionStat stat = __0.productionStatistics.factoryStatPool[factoryIndex];
            stat.ResetRefSpeed();
            stat.ResetStorageCount();
            stat.ResetDetailedStorageCount();
            stat.ResetTransportStorageCount();
            return false;
        }
    }
}

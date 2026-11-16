using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyInformationalVersion("1.0.2")]

namespace ModsPatch
{
    // Fixes for other mods, applied to their original Thunderstore releases at runtime. No mod file is changed.
    // Each target mod is one module: it is patched completely or not at all, and only when that mod is installed.
    [BepInPlugin(Guid, "BUG_ModsPatch", "1.0.2")]
    [BepInDependency(UXAssistFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(LaserClearingFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(PlanetFinderFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(BuildToolOptFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AuxilaryfunctionFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MultifunctionFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MoreMegaStructureFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AllPlanetInfoFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(BigInventoryFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MassRecipePasteFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(LSTMFixes.Target, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bug.dsp.modspatch";
        internal static ManualLogSource Log;

        internal static readonly Module[] Modules =
        {
            new Module("UXAssist", UXAssistFixes.Target, "1.6.3", UXAssistFixes.AlreadyFixed, typeof(UXAssistFixes)),
            new Module("LaserClearing", LaserClearingFixes.Target, "1.0.8", LaserClearingFixes.AlreadyFixed, typeof(LaserClearingFixes)),
            new Module("PlanetFinder", PlanetFinderFixes.Target, "1.3.2", PlanetFinderFixes.AlreadyFixed, typeof(PlanetFinderFixes)),
            new Module("BuildToolOpt", BuildToolOptFixes.Target, "1.2.0", BuildToolOptFixes.AlreadyFixed, typeof(BuildToolOptFixes)),
            new Module("Auxilaryfunction", AuxilaryfunctionFixes.Target, "3.1.1", AuxilaryfunctionFixes.AlreadyFixed, typeof(AuxilaryfunctionFixes)),
            new Module("Multifunction", MultifunctionFixes.Target, "4.0.8", MultifunctionFixes.AlreadyFixed, typeof(MultifunctionFixes)),
            new Module("MoreMegaStructure", MoreMegaStructureFixes.Target, "2.0.3", MoreMegaStructureFixes.AlreadyFixed, typeof(MoreMegaStructureFixes)),
            new Module("AllPlanetInfo", AllPlanetInfoFixes.Target, "1.1.14", AllPlanetInfoFixes.AlreadyFixed, typeof(AllPlanetInfoFixes)),
            new Module("BigInventory", BigInventoryFixes.Target, "1.0.8", BigInventoryFixes.AlreadyFixed, typeof(BigInventoryFixes)),
            new Module("MassRecipePaste", MassRecipePasteFixes.Target, "1.1.3", MassRecipePasteFixes.AlreadyFixed, typeof(MassRecipePasteFixes)),
            new Module("LSTM", LSTMFixes.Target, "0.9.6", LSTMFixes.AlreadyFixed, typeof(LSTMFixes)),
        };

        void Awake()
        {
            Log = Logger;
            foreach (var module in Modules) module.Apply();
        }
    }

    internal sealed class Module
    {
        readonly string name, guid, version;
        readonly Func<bool> alreadyFixed;
        readonly Type patches;
        internal bool Active;

        internal Module(string name, string guid, string version, Func<bool> alreadyFixed, Type patches)
        {
            this.name = name; this.guid = guid; this.version = version; this.alreadyFixed = alreadyFixed; this.patches = patches;
        }

        internal void Apply()
        {
            if (!Chainloader.PluginInfos.TryGetValue(guid, out var info)) return;
            string installed = info.Metadata.Version.ToString();
            if (alreadyFixed())
            {
                Plugin.Log.LogInfo(name + " " + installed + " already contains these fixes, nothing patched");
                return;
            }
            var harmony = new Harmony(Plugin.Guid + "." + name);
            try
            {
                harmony.PatchAll(patches);
                Active = true;
                Plugin.Log.LogInfo(name + " " + installed + " patched");
            }
            catch (Exception e)
            {
                // All or nothing: half of a mod's fixes could leave it inconsistent.
                Plugin.Log.LogError(name + " " + installed + " could not be patched (fixes written for " + version + "); it is left as it is. " + e);
                // Removing patches rebuilds the methods, which can fail as well; that must not stop the other modules.
                try { harmony.UnpatchSelf(); } catch (Exception undo) { Plugin.Log.LogError(name + ": removing the partial patches failed: " + undo.Message); }
            }
        }
    }

    internal static class Il
    {
        // Transpiler helper: fails the whole module when a pattern is missing, instead of patching half.
        internal static void Require(bool found, string what)
        {
            if (!found) throw new InvalidOperationException("pattern not found: " + what);
        }

        internal static int Count(List<CodeInstruction> code, Func<CodeInstruction, bool> match)
        {
            int n = 0;
            foreach (var c in code) if (match(c)) n++;
            return n;
        }
    }
}

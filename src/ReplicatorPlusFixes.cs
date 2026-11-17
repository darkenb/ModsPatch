using System.Collections.Generic;
using DSPReplicatorPlus;
using HarmonyLib;

namespace ModsPatch
{
    // DSPReplicatorPlus 0.0.3 (Thunderstore).
    public static class ReplicatorPlusFixes
    {
        public const string Target = "Appun.DSP.plugin.DSPReplicatorPlus";

        public static bool AlreadyFixed() => typeof(Main).Assembly.GetType("DSPReplicatorPlus.QueueEdit") != null;

        // "To head" empties the queue and adds the new recipe, but the forge's surplus cache still holds what the old
        // tasks will make later. The new head was planned on those items, which only reach tasks after their maker:
        // the head waited for them forever and the queue stopped. The cache is cleared first, so only the inventory counts.
        [HarmonyPrefix, HarmonyPatch(typeof(UI), nameof(UI.mechaForgeAddTaskHead))]
        static bool AddTaskHead(int count) => AddAtHead(UIRoot.instance.uiGame.replicator.mechaForge, UIRoot.instance.uiGame.replicator.selectedRecipe.ID, count);

        // MAX added the game's ">999" as 1000 batches, a number never shown; it now adds the real most (up to MostBatches),
        // the number BUG_UIFix shows above the craft button.
        public const int MostBatches = 99999;  // same as BUG_UIFix ReplicatorCountFix.MostBatches
        [HarmonyPrefix, HarmonyPatch(typeof(UI), nameof(UI.onClickEndMax))]
        static bool EndMax() => AddMost(UI.mechaForgeAddTask);
        [HarmonyPrefix, HarmonyPatch(typeof(UI), nameof(UI.onClickHeadMax))]
        static bool HeadMax() => AddMost(UI.mechaForgeAddTaskHead);

        static bool AddMost(System.Action<int> add)
        {
            var window = UIRoot.instance.uiGame.replicator;
            if (window.isInstantItem || window.selectedRecipe == null) return true;
            add(System.Math.Min(window.mechaForge.PredictTaskCount(window.selectedRecipe.ID, MostBatches - 1), MostBatches));
            return false;
        }

        public static bool AddAtHead(MechaForge forge, int recipeId, int count)
        {
            var previous = new List<ForgeTask>(forge.tasks);
            forge.tasks.Clear();
            forge.CalculateExtra();
            forge.AddTask(recipeId, count);
            int shift = forge.tasks.Count;
            foreach (ForgeTask task in previous)
                if (task.parentTaskIndex >= 0) task.parentTaskIndex += shift;
            forge.tasks.AddRange(previous);
            forge.CalculateExtra();  // the cache again covers the whole queue, for the next addition
            return false;
        }
    }
}

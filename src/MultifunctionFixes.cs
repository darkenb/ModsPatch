using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multifunction_mod;
using Multifunction_mod.GameModify;

namespace ModsPatch
{
    // Multfuntion_mod 4.0.8 (Thunderstore).
    public static class MultifunctionFixes
    {
        public const string Target = "cn.blacksnipe.dsp.Multfuntion_mod";

        public static bool AlreadyFixed() => typeof(Multifunction).Assembly.GetType("Multifunction_mod.Patchs.PlanetReformCollectPatch") != null;

        // ---- Inventory size: shrinking never drops a cell with items or a filter; cells keep their row and column.
        // With DSPBigInventory installed, growing stays inside its on-screen limit.

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerModify), nameof(PlayerModify.OperatePackageSize))]
        static bool OperatePackageSize(Player player, int rownum, int colnum, bool add)
        {
            ResizePackage(player, rownum, colnum, add);
            return false;
        }

        public static bool ResizePackage(Player target, int rowDelta, int columnDelta, bool add)
        {
            if (target?.package == null || rowDelta < 0 || columnDelta < 0 || target.packageColCount <= 0) return false;
            var package = target.package;
            int oldColumns = target.packageColCount;
            long oldRows = ((long)package.size + oldColumns - 1) / oldColumns;
            long columns = Math.Max(10L, Math.Min(30L, oldColumns + (add ? (long)columnDelta : -(long)columnDelta)));
            long rows = Math.Max(1L, oldRows + (add ? (long)rowDelta : -(long)rowDelta));
            if (add)
            {
                if (ScreenLimit("MaxColumnsNow", 0, out int maxColumns)) columns = Math.Min(columns, Math.Max(oldColumns, maxColumns));
                if (ScreenLimit("MaxRowsNow", (int)columns, out int maxRows))
                {
                    rows = Math.Min(rows, Math.Max(oldRows, maxRows));
                    if (columns * rows > package.size && rows > maxRows) return false;
                }
                if (columns == oldColumns && rows == oldRows) return false;
            }
            long size = rows * columns;
            if (size > int.MaxValue) return false;
            for (int i = 0; i < package.size; i++)
                if ((i / oldColumns >= rows || i % oldColumns >= columns) && (package.grids[i].count != 0 || package.grids[i].filter != 0)) return false;
            var grids = new StorageComponent.GRID[(int)size];
            for (int i = 0; i < package.size; i++)
                if (i / oldColumns < rows && i % oldColumns < columns) grids[(i / oldColumns) * (int)columns + i % oldColumns] = package.grids[i];
            target.packageColCount = (int)columns;
            package.grids = grids; package.size = (int)size;
            package.ResetOptimizationFlags(); package.NotifyStorageChange(); package.NotifyStorageSizeChange();
            return true;
        }

        // DSPBigInventory's screen limit (false without that mod: no limit): the Thunderstore release gets it from this
        // plugin's BigInventory module, our old local builds have it themselves (looked up by name).
        static bool ScreenLimit(string name, int argument, out int limit)
        {
            if (BigInventory.ScreenLimits.Active)
            {
                limit = name == "MaxRowsNow" ? BigInventory.ScreenLimits.MaxRowsNow(argument) : BigInventory.ScreenLimits.MaxColumnsNow();
                return true;
            }
            limit = int.MaxValue;
            try
            {
                Type type = AccessTools.TypeByName("DSPBigInventory.ScreenLimits");
                bool withColumns = name == "MaxRowsNow";
                var method = type?.GetMethod(name, withColumns ? new[] { typeof(int) } : Type.EmptyTypes);
                if (method == null) return false;
                limit = (int)method.Invoke(null, withColumns ? new object[] { argument } : null);
                return true;
            }
            catch (Exception) { return false; }
        }

        // ---- Recipes: originals are kept per RecipeProto (late-added, reordered or replaced recipes restore correctly),
        // and "all handcraft" also updates the item handcraft links the replicator uses for intermediate products.

        sealed class RecipeOrigin { public bool Handcraft; public int TimeSpend, CacheTimeSpend, CacheExtraTimeSpend; }
        static readonly Dictionary<RecipeProto, RecipeOrigin> recipeOrigins = new();

        static RecipeOrigin Origin(RecipeProto recipe)
        {
            if (!recipeOrigins.TryGetValue(recipe, out var origin))
            {
                RecipeExecuteData data = RecipeModify.GetRecipeExecuteData(recipe);
                origin = new RecipeOrigin
                {
                    Handcraft = recipe.Handcraft,
                    TimeSpend = recipe.TimeSpend,
                    CacheTimeSpend = data != null ? data.timeSpend : recipe.TimeSpend * RecipeModify.RecipeExecuteTimeScale,
                    CacheExtraTimeSpend = data != null ? data.extraTimeSpend : 0
                };
                recipeOrigins.Add(recipe, origin);
            }
            return origin;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(RecipeModify), nameof(RecipeModify.CaptureRecipeOriginals))]
        static bool CaptureRecipeOriginals()
        {
            if (LDB.recipes?.dataArray != null) foreach (RecipeProto recipe in LDB.recipes.dataArray) Origin(recipe);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(RecipeModify), nameof(RecipeModify.ApplyRecipeModifications))]
        static bool ApplyRecipeModifications(bool allHandcraft, bool quickProduce)
        {
            if (LDB.recipes?.dataArray == null || recipeOrigins.Count == 0) return false;
            if (!allHandcraft && !quickProduce && !RecipeModify.recipeModificationsApplied) return false;
            RecipeModify.recipeModificationsApplied = allHandcraft || quickProduce;
            foreach (RecipeProto recipe in LDB.recipes.dataArray)
            {
                RecipeOrigin origin = Origin(recipe);
                recipe.Handcraft = allHandcraft || origin.Handcraft;
                recipe.TimeSpend = quickProduce ? RecipeModify.QuickProduceTimeSpend : origin.TimeSpend;
                RecipeExecuteData data = RecipeModify.GetRecipeExecuteData(recipe);
                if (data == null || origin.CacheTimeSpend <= 0) continue;
                int time = quickProduce ? RecipeModify.QuickProduceTimeSpend * RecipeModify.RecipeExecuteTimeScale : origin.CacheTimeSpend;
                if (origin.CacheExtraTimeSpend > 0)
                    RecipeModify.SetRecipeExecuteCycleTime(data, time, quickProduce ? RecipeModify.ScaleCycleTime(origin.CacheExtraTimeSpend, origin.CacheTimeSpend, time) : origin.CacheExtraTimeSpend);
                else
                    RecipeModify.SetRecipeExecuteCycleTime(data, time, null);
            }
            RebuildHandcraftCache();
            return false;
        }

        // The game links handcraft recipes into ItemProto.handcraft* at load; an existing preferred path is kept
        // (a new main-product path could make loops like salt -> chlorine -> salt).
        static void RebuildHandcraftCache()
        {
            foreach (ItemProto item in LDB.items.dataArray)
            {
                RecipeProto previous = item.handcraft;
                if (item.handcrafts == null) item.handcrafts = new List<RecipeProto>();
                else item.handcrafts.Clear();
                item.handcraft = null;
                item.handcraftProductCount = 0;
                int best = int.MaxValue;
                foreach (RecipeProto recipe in LDB.recipes.dataArray)
                {
                    if (!recipe.Handcraft) continue;
                    int output = Array.IndexOf(recipe.Results, item.ID);
                    if (output < 0) continue;
                    item.handcrafts.Add(recipe);
                    if (output < best) { item.handcraft = recipe; item.handcraftProductCount = recipe.ResultCounts[output]; best = output; }
                }
                if (previous != null && item.handcrafts.Contains(previous))
                {
                    item.handcraft = previous;
                    item.handcraftProductCount = previous.ResultCounts[Array.IndexOf(previous.Results, item.ID)];
                }
            }
            UIReplicatorWindow window = UIRoot.instance?.uiGame?.replicator;
            if (window != null && window.active)
            {
                window.OnTechUnlocked(0, 0, false);
                window.OnSelectedRecipeChange(false);
            }
        }

        // Recipe changes are applied again when the replicator opens (upstream hooks this only with "instant item" on).
        [HarmonyPostfix, HarmonyPatch(typeof(UIReplicatorWindow), nameof(UIReplicatorWindow._OnOpen))]
        static void ReplicatorOpen() => Multifunction.refreshLDB = true;

        // ---- Bulk foundation (PlanetReformAll) adds the removed plants to the vegetation collection, once.
        [HarmonyTranspiler, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.PlanetReformAll))]
        static IEnumerable<CodeInstruction> BulkReform(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var remove = AccessTools.Method(typeof(PlanetFactory), nameof(PlanetFactory.RemoveVegeWithComponents));
            int n = 0;
            foreach (var c in code)
                if (c.Calls(remove)) { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(MultifunctionFixes), nameof(RemoveVegeAndCollect)); n++; }
            Il.Require(n == 1, "PlanetReformAll RemoveVegeWithComponents");
            return code;
        }

        static void RemoveVegeAndCollect(PlanetFactory factory, int id)
        {
            int proto = id > 0 && id < factory.vegePool.Length && factory.vegePool[id].id == id ? factory.vegePool[id].protoId : 0;
            var collection = factory.gameData.mainPlayer?.vegetableCollection;
            int before = 0, after = 0;
            collection?.playerVegeDict.TryGetValue(proto, out before);
            factory.RemoveVegeWithComponents(id);
            collection?.playerVegeDict.TryGetValue(proto, out after);
            if (proto > 0 && collection != null && factory.vegePool[id].id == 0 && before == after) collection.AddVegeToPlayer(proto);
        }
    }
}

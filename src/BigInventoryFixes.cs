using System;
using System.Reflection;
using HarmonyLib;
using ModsPatch.BigInventory;
using Mod = DSPBigInventory.DSPBigInventory;

namespace ModsPatch
{
    // DSPBigInventory 1.0.8 (Thunderstore). Its window fit is in BUG_UIFix.
    // 1.0.8 set the inventory to exactly colCount * rowCount on every load: a save larger than the config lost the items in the
    // cut cells, and a column change moved items between cells. It also blocked the inventory/delivery research.
    // Now the config values are minimums, a save is never made smaller than its content, cells keep their row and column,
    // research works, and the whole window has to fit the screen (columns are widened before empty rows are dropped).
    public static class BigInventoryFixes
    {
        public const string Target = "Appun.DSP.plugin.BigInventory";

        public static bool AlreadyFixed() => typeof(Mod).Assembly.GetType("DSPBigInventory.ScreenLimits") != null;

        [HarmonyPrepare]
        static void Prepare(MethodBase original)
        {
            if (original == null) ScreenLimits.Active = true;
        }

        // The mod's own sizing patches (it applies them in Start, after this plugin's Awake) are switched off.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mod), nameof(Mod.Mecha_Import_Postfix))]
        [HarmonyPatch(typeof(Mod), nameof(Mod.Mecha_SetForNewGame_Postfix))]
        [HarmonyPatch(typeof(Mod), nameof(Mod.UIInventoryWindow_OnPackageSizeChange_Postfix))]
        static bool Skip() => false;

        [HarmonyPrefix, HarmonyPatch(typeof(Mod), nameof(Mod.GameHistoryData_UnlockTechFunction_Prefix))]
        static bool AllowResearch(ref bool __result)
        {
            __result = true;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.Import))]
        [HarmonyPatch(typeof(Player), nameof(Player.SetForNewGame))]
        static void Loaded(Player __instance) => Apply(__instance);

        // Inventory (5), delivery columns (32) and delivery rows (38) research: the config minimum still applies on top.
        [HarmonyPostfix, HarmonyPatch(typeof(GameHistoryData), nameof(GameHistoryData.UnlockTechFunction))]
        static void Researched(int func)
        {
            if (func == 5 || func == 32 || func == 38) Apply(GameMain.mainPlayer);
        }

        static void Apply(Player player) => Apply(player, Mod.colCount?.Value ?? 16, Mod.rowCount?.Value ?? 14, Mod.DeliveryColCount?.Value ?? 5, Mod.defaultSetting?.Value ?? false);

        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        // Last occupied/filtered cell: neither a row nor a column holding one may be removed.
        public static void UsedBounds(StorageComponent package, int columns, out int usedColumns, out int usedRows)
        {
            usedColumns = 0; usedRows = 0;
            for (int i = 0; i < package.size; i++)
            {
                if (package.grids[i].itemId == 0 && package.grids[i].count == 0 && package.grids[i].filter == 0) continue;
                usedRows = Math.Max(usedRows, i / columns + 1);
                usedColumns = Math.Max(usedColumns, i % columns + 1);
            }
        }

        // The save is never made smaller than its content, the config values are minimums, and the whole window frame has
        // to fit the screen (and the window's 400 slots). When the current layout does not fit, columns are widened first so
        // the capacity survives (cells keep their row/column); only if that fails are empty trailing rows removed.
        public static void Plan(int configColumns, int configRows, int oldColumns, int oldSize, int usedColumns, int usedRows, int deliveryColumns, out int columns, out int rows)
        {
            configColumns = Clamp(configColumns, 10, 30); configRows = Clamp(configRows, 1, 100);
            int oldRows = (oldSize - 1) / oldColumns + 1;
            int minColumns = Math.Max(10, usedColumns);
            int maxColumns = Math.Max(ScreenLimits.MaxColumns(deliveryColumns), minColumns);
            int currentColumns = Clamp(oldColumns, minColumns, maxColumns);
            int goalColumns = Clamp(Math.Max(configColumns, currentColumns), currentColumns, maxColumns);
            int goalRows = Math.Max(configRows, oldRows);
            int[] tryColumns = { goalColumns, currentColumns, goalColumns, currentColumns };
            int[] tryRows = { goalRows, goalRows, oldRows, oldRows };
            for (int i = 0; i < tryColumns.Length; i++)
                if (Acceptable(tryColumns[i], tryRows[i], oldSize, usedColumns, usedRows, deliveryColumns)) { columns = tryColumns[i]; rows = tryRows[i]; return; }
            long need = Math.Max(oldSize, (long)currentColumns * configRows);
            for (int c = currentColumns; c <= maxColumns; c++)
            {
                int r = (int)Math.Max(usedRows, (need + c - 1) / c);
                if (Acceptable(c, r, oldSize, usedColumns, usedRows, deliveryColumns)) { columns = c; rows = r; return; }
            }
            columns = currentColumns;
            rows = Math.Max(ScreenLimits.MaxRows(currentColumns, deliveryColumns), usedRows);
        }

        static bool Acceptable(int columns, int rows, int oldSize, int usedColumns, int usedRows, int deliveryColumns)
            => columns >= usedColumns && rows >= usedRows
               && rows <= Math.Max(ScreenLimits.MaxRows(columns, deliveryColumns), usedRows)
               && (long)columns * rows <= Math.Max(ScreenLimits.MaxCells, oldSize);   // an already oversized save is not made worse

        // Re-lays the package out keeping every cell at its row/column; refuses when a used or filtered cell would be lost.
        public static bool Relayout(Player player, int newColumns, int newRows)
        {
            var package = player.package;
            int oldColumns = player.GetPackageColumnCount();
            long size = (long)newColumns * newRows;
            if (newColumns < 1 || newRows < 1 || size > int.MaxValue) return false;
            for (int i = 0; i < package.size; i++)
            {
                bool inside = i / oldColumns < newRows && i % oldColumns < newColumns;
                if (!inside && (package.grids[i].itemId != 0 || package.grids[i].count != 0 || package.grids[i].filter != 0)) return false;
            }
            var grids = new StorageComponent.GRID[(int)size];
            for (int i = 0; i < package.size; i++)
                if (i / oldColumns < newRows && i % oldColumns < newColumns) grids[(i / oldColumns) * newColumns + i % oldColumns] = package.grids[i];
            player.packageColCount = newColumns;
            package.grids = grids; package.size = (int)size;
            package.ResetOptimizationFlags(); package.NotifyStorageChange(); package.NotifyStorageSizeChange();
            return true;
        }

        public static void Apply(Player player, int columns, int rows, int deliveryColumns, bool native)
        {
            if (native || player?.package == null) return;
            deliveryColumns = Clamp(deliveryColumns, 0, 5);
            int oldColumns = player.GetPackageColumnCount();
            int oldSize = player.package.size;
            int oldRows = (oldSize - 1) / oldColumns + 1;
            // The frame width/height depend on the delivery panel that will be shown afterwards.
            int delivery = player.deliveryPackage != null ? Math.Max(player.deliveryPackage.colCount, deliveryColumns) : deliveryColumns;
            UsedBounds(player.package, oldColumns, out int usedColumns, out int usedRows);
            Plan(columns, rows, oldColumns, oldSize, usedColumns, usedRows, delivery, out int newColumns, out int newRows);
            int target = checked(newColumns * newRows);
            if (newColumns >= oldColumns && newRows >= oldRows)
            {
                player.packageColCount = newColumns;   // pure growth: native remapping
                if (newColumns != oldColumns) player.package.SetSize(target, oldColumns, newColumns);
                else if (player.package.size < target) player.package.SetSize(target);
            }
            else if (!Relayout(player, newColumns, newRows))
                Plugin.Log?.LogWarning("BigInventory: inventory " + oldColumns + "x" + oldRows + " kept, it holds items outside " + newColumns + "x" + newRows);
            if (newColumns != oldColumns || newRows != oldRows)
                Plugin.Log?.LogInfo("BigInventory: inventory " + oldColumns + "x" + oldRows + " -> " + player.GetPackageColumnCount() + "x" + ((player.package.size - 1) / player.GetPackageColumnCount() + 1) + " (" + ScreenLimits.Describe(delivery, newColumns) + ")");
            if (player.deliveryPackage != null)
            {
                player.deliveryPackage.colCount = Math.Max(player.deliveryPackage.colCount, deliveryColumns);
                player.OnPackageSizeChange();  // native 20-row/5-column clamp, maps and UI notification
            }
        }
    }
}

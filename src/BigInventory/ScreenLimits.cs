using System;
using UnityEngine;

namespace ModsPatch.BigInventory
{
    // How many inventory columns/rows the full native window frame can have on this screen.
    // Native UIInventoryWindow frame in UI layout units (OnPlayerDeliveryPackageSizeChange):
    //   width  = 88 + 50 * columns (+ 50 * deliveryColumns + 6 while the delivery panel is shown)
    //   height = 126 + 50 * rows   (+ 26 while the delivery panel is shown)
    // The game maps layout units to pixels with UICanvasScalerHandler.uiLayoutHeight / Screen.height.
    // From DSPBigInventory 1.0.11 (local). Other mods may look this class up by name (ModsPatch.BigInventory.ScreenLimits.MaxColumnsNow /
    // MaxRowsNow, only while Active: DSPBigInventory is installed and patched).
    public static class ScreenLimits
    {
        public const int NativeMinColumns = 10, NativeMaxColumns = 30, HardMaxRows = 100;
        public const int MaxCells = 400;   // UIStorageGrid.kMaxGrid: its slot arrays hold 400 entries, more slots throw in the window
        public const int EdgePixels = 8;   // same margin the window fit (BUG_UIFix) keeps to the safe area
        public static bool Active;
        // Test hook: the available layout area in units (0 = read the real screen).
        public static float OverrideUnitsWidth, OverrideUnitsHeight;

        public static bool Available(out float unitsWidth, out float unitsHeight)
        {
            unitsWidth = OverrideUnitsWidth; unitsHeight = OverrideUnitsHeight;
            if (unitsWidth > 0 && unitsHeight > 0) return true;
            return ReadScreen(out unitsWidth, out unitsHeight);
        }

        // Unity calls live in their own method so the headless checks (override set) never JIT them.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool ReadScreen(out float unitsWidth, out float unitsHeight)
        {
            unitsWidth = unitsHeight = 0;
            int pixelsHigh = Screen.height;
            if (pixelsHigh < 64) return false;
            float unitsPerPixel = UICanvasScalerHandler.uiLayoutHeight / (float)pixelsHigh;
            Rect safe = Screen.safeArea;
            unitsWidth = (safe.width - 2 * EdgePixels) * unitsPerPixel;
            unitsHeight = (safe.height - 2 * EdgePixels) * unitsPerPixel;
            return unitsWidth > 0 && unitsHeight > 0;
        }

        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        static int Fit(float units, float fixedUnits) => (int)Math.Floor((units - fixedUnits) / 50f);

        // Largest column count whose full frame fits the width (native range 10-30). deliveryColumns 0 = no delivery panel.
        public static int MaxColumns(float unitsWidth, int deliveryColumns)
            => Clamp(Fit(unitsWidth, 88 + (deliveryColumns > 0 ? deliveryColumns * 50 + 6 : 0)), NativeMinColumns, NativeMaxColumns);

        // Largest row count whose full frame fits the height, also keeping columns * rows within the window's 400-slot limit.
        public static int MaxRows(float unitsHeight, int columns, int deliveryColumns)
            => Clamp(Math.Min(Fit(unitsHeight, 126 + (deliveryColumns > 0 ? 26 : 0)), MaxCells / Math.Max(1, columns)), 1, HardMaxRows);

        public static int MaxColumns(int deliveryColumns)
        {
            float w, h;
            return Available(out w, out h) ? MaxColumns(w, deliveryColumns) : NativeMaxColumns;
        }

        public static int MaxRows(int columns, int deliveryColumns)
        {
            float w, h;
            return Available(out w, out h) ? MaxRows(h, columns, deliveryColumns) : Clamp(MaxCells / Math.Max(1, columns), 1, HardMaxRows);
        }

        // For other mods: limits with the running game's delivery panel (no panel before the player exists).
        public static int MaxColumnsNow() => MaxColumns(DeliveryColumnsNow());
        public static int MaxRowsNow(int columns) => MaxRows(columns, DeliveryColumnsNow());
        static int DeliveryColumnsNow()
        {
            var delivery = GameMain.mainPlayer?.deliveryPackage;
            return delivery == null ? 0 : Clamp(delivery.colCount, 0, 5);
        }

        public static string Describe(int deliveryColumns, int columns)
        {
            float w, h;
            if (!Available(out w, out h)) return "screen size unknown: native limits (columns 10-30, 400 slots)";
            return Limits(w, h, deliveryColumns, columns) + (OverrideUnitsWidth > 0 ? "" : Resolution());
        }

        static string Limits(float w, float h, int deliveryColumns, int columns)
            => string.Format("columns 10-{0}, rows 1-{1} at {2} columns (400 slots max)", MaxColumns(w, deliveryColumns), MaxRows(h, columns, deliveryColumns), columns);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string Resolution() => string.Format(" on {0}x{1} px, UI layout {2}", Screen.width, Screen.height, UICanvasScalerHandler.uiLayoutHeight);
    }
}

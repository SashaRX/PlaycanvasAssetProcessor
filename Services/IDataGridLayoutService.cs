using System.Windows.Controls;

namespace AssetProcessor.Services {
    /// <summary>
    /// Service for managing DataGrid column layout persistence (widths, order, visibility).
    /// </summary>
    public interface IDataGridLayoutService {
        /// <summary>
        /// Saves column widths to settings.
        /// </summary>
        void SaveColumnWidths(DataGrid grid, string settingName);

        /// <summary>
        /// Loads column widths from settings.
        /// Returns true if widths were loaded (grid has saved settings).
        /// </summary>
        bool LoadColumnWidths(DataGrid grid, string settingName);

        /// <summary>
        /// Saves column order (DisplayIndex) to settings.
        /// </summary>
        void SaveColumnOrder(DataGrid grid, string settingName);

        /// <summary>
        /// Loads column order from settings.
        /// </summary>
        void LoadColumnOrder(DataGrid grid, string settingName);

        /// <summary>
        /// Saves column visibility to settings.
        /// </summary>
        void SaveColumnVisibility(DataGrid grid, string settingName);

        /// <summary>
        /// Loads column visibility from settings.
        /// </summary>
        void LoadColumnVisibility(DataGrid grid, string settingName);

        /// <summary>
        /// Adjusts the last visible column to fill remaining space.
        /// </summary>
        /// <param name="grid">The DataGrid to adjust</param>
        /// <param name="hasSavedWidths">If true, only shrink last column; otherwise cascade shrink</param>
        void FillRemainingSpace(DataGrid grid, bool hasSavedWidths);

        /// <summary>
        /// Schedules a debounced save of column widths (500ms delay).
        /// </summary>
        void SaveColumnWidthsDebounced(DataGrid grid, string settingName);

        /// <summary>
        /// Marks grid as loaded (prevents saving during initial load).
        /// </summary>
        void MarkAsLoaded(DataGrid grid);

        /// <summary>
        /// Checks if a grid has been loaded.
        /// </summary>
        bool IsLoaded(DataGrid grid);

        /// <summary>
        /// Checks if a grid has saved widths from settings.
        /// </summary>
        bool HasSavedWidths(DataGrid grid);

        /// <summary>
        /// Cleans up timers and state for a grid.
        /// </summary>
        void Cleanup(DataGrid grid);

        /// <summary>
        /// Cleans up all timers and state.
        /// </summary>
        void CleanupAll();
    }
}

using AssetProcessor.Settings;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AssetProcessor.Services {
    /// <summary>
    /// Service for managing DataGrid column layout persistence (widths, order, visibility).
    /// </summary>
    public class DataGridLayoutService : IDataGridLayoutService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly AppSettings appSettings;
        private readonly Dictionary<DataGrid, DispatcherTimer> _saveTimers = new();
        private readonly Dictionary<DataGrid, (string settingName, EventHandler handler)> _saveHandlers = new();
        private readonly HashSet<DataGrid> _loadedGrids = new();
        private readonly Dictionary<DataGrid, DateTime> _loadedTime = new();
        private readonly HashSet<DataGrid> _hasSavedWidths = new();
        private readonly Dictionary<DataGrid, double> _lastGridWidth = new();

        private bool _isAdjusting = false;

        public DataGridLayoutService(AppSettings appSettings) {
            this.appSettings = appSettings;
        }

        public void SaveColumnWidths(DataGrid grid, string settingName) {
            if (grid == null || grid.Columns.Count == 0) return;

            var widths = new List<string>();
            foreach (var column in grid.Columns) {
                widths.Add(column.ActualWidth.ToString("F1", CultureInfo.InvariantCulture));
            }

            string widthsStr = string.Join(",", widths);
            string? currentValue = GetSettingValue(settingName);

            if (currentValue != widthsStr) {
                SetSettingValue(settingName, widthsStr);
                appSettings.Save();
                logger.Debug($"Saved column widths for {settingName}");
            }
        }

        public bool LoadColumnWidths(DataGrid grid, string settingName) {
            string? widthsStr = GetSettingValue(settingName);

            if (!string.IsNullOrEmpty(widthsStr)) {
                string[] parts = widthsStr.Split(',');
                for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++) {
                    if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) && width > 0) {
                        grid.Columns[i].Width = new DataGridLength(width);
                    }
                }
                _hasSavedWidths.Add(grid);
                logger.Debug($"Loaded column widths for {settingName}");
                MarkAsLoaded(grid);
                return true;
            }

            MarkAsLoaded(grid);
            return false;
        }

        public void SaveColumnOrder(DataGrid grid, string settingName) {
            if (grid == null || grid.Columns.Count == 0) return;

            var order = string.Join(",", grid.Columns.Select(c => c.DisplayIndex));
            SetSettingValue(settingName, order);
            appSettings.Save();
            logger.Debug($"Saved column order for {settingName}");
        }

        public void LoadColumnOrder(DataGrid grid, string settingName) {
            string? orderStr = GetSettingValue(settingName);
            if (string.IsNullOrEmpty(orderStr)) return;

            string[] parts = orderStr.Split(',');
            for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++) {
                if (int.TryParse(parts[i], out int displayIndex) && displayIndex >= 0 && displayIndex < grid.Columns.Count) {
                    grid.Columns[i].DisplayIndex = displayIndex;
                }
            }
            logger.Debug($"Loaded column order for {settingName}");
        }

        public void SaveColumnVisibility(DataGrid grid, string settingName) {
            if (grid == null || grid.Columns.Count == 0) return;

            var visibility = string.Join(",", grid.Columns.Select(c => c.Visibility == Visibility.Visible ? "1" : "0"));
            SetSettingValue(settingName, visibility);
            appSettings.Save();
            logger.Debug($"Saved column visibility for {settingName}");
        }

        public void LoadColumnVisibility(DataGrid grid, string settingName) {
            string? visibility = GetSettingValue(settingName);
            if (string.IsNullOrEmpty(visibility)) return;

            var parts = visibility.Split(',');
            for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++) {
                grid.Columns[i].Visibility = parts[i] == "1" ? Visibility.Visible : Visibility.Collapsed;
            }
            logger.Debug($"Loaded column visibility for {settingName}");
        }

        public void FillRemainingSpace(DataGrid grid, bool hasSavedWidths) {
            if (grid == null || grid.Columns.Count == 0 || _isAdjusting) return;

            double currentWidth = grid.ActualWidth;
            if (_lastGridWidth.TryGetValue(grid, out double lastWidth) && Math.Abs(currentWidth - lastWidth) < 1) {
                return;
            }
            _lastGridWidth[grid] = currentWidth;

            _isAdjusting = true;
            try {
                double availableWidth = currentWidth - SystemParameters.VerticalScrollBarWidth - 2;
                if (availableWidth <= 0) return;

                var visibleColumns = grid.Columns
                    .Where(c => c.Visibility == Visibility.Visible)
                    .OrderByDescending(c => c.DisplayIndex)
                    .ToList();

                if (visibleColumns.Count == 0) return;

                double totalWidth = visibleColumns.Sum(c => c.ActualWidth);
                double delta = availableWidth - totalWidth;

                if (Math.Abs(delta) < 1) return;

                var lastVisible = visibleColumns[0];

                if (delta > 0) {
                    lastVisible.Width = new DataGridLength(lastVisible.ActualWidth + delta);
                } else {
                    if (hasSavedWidths) {
                        double colMin = lastVisible.MinWidth > 0 ? lastVisible.MinWidth : 30;
                        double available = lastVisible.ActualWidth - colMin;
                        if (available > 0) {
                            double shrink = Math.Min(-delta, available);
                            lastVisible.Width = new DataGridLength(lastVisible.ActualWidth - shrink);
                        }
                    } else {
                        double remainingShrink = -delta;
                        for (int i = 0; i < visibleColumns.Count && remainingShrink > 0; i++) {
                            var col = visibleColumns[i];
                            double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                            double available = col.ActualWidth - colMin;
                            if (available > 0) {
                                double shrink = Math.Min(remainingShrink, available);
                                col.Width = new DataGridLength(col.ActualWidth - shrink);
                                remainingShrink -= shrink;
                            }
                        }
                    }
                }
            } finally {
                _isAdjusting = false;
            }
        }

        public void SaveColumnWidthsDebounced(DataGrid grid, string settingName) {
            if (!_loadedGrids.Contains(grid)) return;

            // Don't save within 2 seconds after loading
            if (_loadedTime.TryGetValue(grid, out var loadedTime) &&
                (DateTime.Now - loadedTime).TotalSeconds < 2) {
                return;
            }

            if (!_saveTimers.TryGetValue(grid, out var timer)) {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveTimers[grid] = timer;

                EventHandler handler = (s, e) => {
                    timer.Stop();
                    SaveColumnWidths(grid, settingName);
                };
                _saveHandlers[grid] = (settingName, handler);
                timer.Tick += handler;
            } else {
                // Update setting name if changed
                if (_saveHandlers.TryGetValue(grid, out var existing) && existing.settingName != settingName) {
                    timer.Tick -= existing.handler;
                    EventHandler handler = (s, e) => {
                        timer.Stop();
                        SaveColumnWidths(grid, settingName);
                    };
                    _saveHandlers[grid] = (settingName, handler);
                    timer.Tick += handler;
                }
            }

            timer.Stop();
            timer.Start();
        }

        public void MarkAsLoaded(DataGrid grid) {
            _loadedGrids.Add(grid);
            _loadedTime[grid] = DateTime.Now;
        }

        public bool IsLoaded(DataGrid grid) => _loadedGrids.Contains(grid);

        public bool HasSavedWidths(DataGrid grid) => _hasSavedWidths.Contains(grid);

        public void Cleanup(DataGrid grid) {
            if (_saveTimers.TryGetValue(grid, out var timer)) {
                timer.Stop();
                if (_saveHandlers.TryGetValue(grid, out var handlerInfo)) {
                    timer.Tick -= handlerInfo.handler;
                }
                _saveTimers.Remove(grid);
            }
            _saveHandlers.Remove(grid);
            _loadedGrids.Remove(grid);
            _loadedTime.Remove(grid);
            _hasSavedWidths.Remove(grid);
            _lastGridWidth.Remove(grid);
        }

        public void CleanupAll() {
            foreach (var kvp in _saveTimers) {
                kvp.Value.Stop();
                if (_saveHandlers.TryGetValue(kvp.Key, out var handlerInfo)) {
                    kvp.Value.Tick -= handlerInfo.handler;
                }
            }
            _saveTimers.Clear();
            _saveHandlers.Clear();
            _loadedGrids.Clear();
            _loadedTime.Clear();
            _hasSavedWidths.Clear();
            _lastGridWidth.Clear();
        }

        private string? GetSettingValue(string settingName) {
            return (string?)typeof(AppSettings).GetProperty(settingName)?.GetValue(appSettings);
        }

        private void SetSettingValue(string settingName, string value) {
            typeof(AppSettings).GetProperty(settingName)?.SetValue(appSettings, value);
        }
    }
}

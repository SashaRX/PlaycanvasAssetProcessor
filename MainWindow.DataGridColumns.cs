using AssetProcessor.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AssetProcessor {
    /// <summary>
    /// DataGrid column management: header text adjustment, width persistence,
    /// visibility management, initialization, and scale slider.
    /// Left gripper resize logic is in DataGridColumns.Resize.cs.
    /// </summary>
    public partial class MainWindow {
        // Column header definitions: (Full name, Short name, MinWidthForFull)
        private static readonly (string Full, string Short, double MinWidthForFull)[] TextureColumnHeaders = [
            ("№", "№", 30),                      // 0 - Index
            ("ID", "ID", 30),                    // 1 - ID
            ("Texture Name", "Name", 100),       // 2 - Name
            ("Extension", "Ext", 65),            // 3 - Extension
            ("Size", "Size", 40),                // 4 - Size
            ("Compressed", "Comp", 85),          // 5 - Compressed Size
            ("Resolution", "Res", 80),           // 6 - Resolution
            ("Resize", "Rsz", 55),               // 7 - Resize Resolution
            ("Format", "Fmt", 55),               // 8 - Compression Format
            ("Mipmaps", "Mip", 60),              // 9 - Mipmaps
            ("Preset", "Prs", 55),               // 10 - Preset
            ("Status", "St", 55)                 // 11 - Status/Progress
        ];

        private readonly bool[] _columnUsingShortHeader = new bool[12];
        private readonly Dictionary<DataGrid, double[]> _previousColumnWidths = new();
        private bool _isAdjustingColumns = false;
        private DispatcherTimer? _resizeDebounceTimer;

        // Left gripper dragging state
        private bool _isLeftGripperDragging;
        private Point _leftGripperStartPoint;
        private DataGridColumn? _leftGripperColumn;
        private List<DataGridColumn>? _leftGripperLeftColumns;
        private List<DataGridColumn>? _leftGripperRightColumns;
        private Dictionary<DataGridColumn, double>? _leftGripperColumnWidths;
        private double _leftGripperColumnWidth;
        private DataGrid? _currentResizingDataGrid;
        private const double LeftGripperZoneWidth = 10;
        private readonly HashSet<DataGrid> _leftGripperSubscribedGrids = new();

        #region DataGrid Events

        private void TexturesDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdate(grid);
        }

        private void DebouncedResizeUpdate(DataGrid grid) {
            _resizeDebounceTimer?.Stop();
            _resizeDebounceTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _resizeDebounceTimer.Tick += (s, e) => {
                _resizeDebounceTimer?.Stop();
                UpdateColumnHeadersBasedOnWidth(grid);
                dataGridLayoutService.FillRemainingSpace(grid, dataGridLayoutService.HasSavedWidths(grid));
            };
            _resizeDebounceTimer.Start();
        }

        private void TexturesDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(TexturesDataGrid);
                SubscribeDataGridColumnResizing(TexturesDataGrid);
                dataGridLayoutService.FillRemainingSpace(TexturesDataGrid, dataGridLayoutService.HasSavedWidths(TexturesDataGrid));
                dataGridLayoutService.SaveColumnOrder(TexturesDataGrid, GetColumnOrderSettingName(TexturesDataGrid));
            }), DispatcherPriority.Background);
        }

        private void ModelsDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdateForGrid(grid);
        }

        private void DebouncedResizeUpdateForGrid(DataGrid grid) {
            _resizeDebounceTimer?.Stop();
            _resizeDebounceTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _resizeDebounceTimer.Tick += (s, e) => {
                _resizeDebounceTimer?.Stop();
                dataGridLayoutService.FillRemainingSpace(grid, dataGridLayoutService.HasSavedWidths(grid));
            };
            _resizeDebounceTimer.Start();
        }

        private void ModelsDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(ModelsDataGrid);
                SubscribeDataGridColumnResizing(ModelsDataGrid);
                dataGridLayoutService.FillRemainingSpace(ModelsDataGrid, dataGridLayoutService.HasSavedWidths(ModelsDataGrid));
                dataGridLayoutService.SaveColumnOrder(ModelsDataGrid, GetColumnOrderSettingName(ModelsDataGrid));
            }), DispatcherPriority.Background);
        }

        private void MaterialsDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdateForGrid(grid);
        }

        private void MaterialsDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(MaterialsDataGrid);
                SubscribeDataGridColumnResizing(MaterialsDataGrid);
                dataGridLayoutService.FillRemainingSpace(MaterialsDataGrid, dataGridLayoutService.HasSavedWidths(MaterialsDataGrid));
                dataGridLayoutService.SaveColumnOrder(MaterialsDataGrid, GetColumnOrderSettingName(MaterialsDataGrid));
            }), DispatcherPriority.Background);
        }

        #endregion

        #region Column Width Subscription

        private void SubscribeToColumnWidthChanges() => SubscribeToColumnWidthChanges(TexturesDataGrid);

        private void SubscribeToColumnWidthChanges(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;

            var widths = new double[grid.Columns.Count];
            for (int i = 0; i < grid.Columns.Count; i++) {
                widths[i] = grid.Columns[i].ActualWidth;

                var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
                descriptor?.RemoveValueChanged(grid.Columns[i], OnColumnWidthChanged);
                descriptor?.AddValueChanged(grid.Columns[i], OnColumnWidthChanged);
            }
            _previousColumnWidths[grid] = widths;

            Dispatcher.BeginInvoke(() => SubscribeToLeftGripperThumbs(), DispatcherPriority.Loaded);
        }

        private void SubscribeToLeftGripperThumbs() {
            SubscribeDataGridColumnResizing(TexturesDataGrid);
            SubscribeDataGridColumnResizing(ModelsDataGrid);
            SubscribeDataGridColumnResizing(MaterialsDataGrid);
        }

        private void SubscribeDataGridColumnResizing(DataGrid grid) {
            if (grid == null) return;
            if (_leftGripperSubscribedGrids.Contains(grid)) return;

            var columnHeaders = FindVisualChildren<DataGridColumnHeader>(grid).ToList();
            if (columnHeaders.Count == 0) return;

            foreach (var header in columnHeaders) {
                if (header.Column == null) continue;
                header.PreviewMouseLeftButtonDown += OnHeaderMouseDown;
                header.PreviewMouseMove += OnHeaderMouseMoveForCursor;
            }

            grid.PreviewMouseMove += OnDataGridMouseMove;
            grid.PreviewMouseLeftButtonUp += OnDataGridMouseUp;
            grid.LostMouseCapture += OnDataGridLostCapture;
            _leftGripperSubscribedGrids.Add(grid);
        }

        #endregion

        #region Column Width Change Handling

        private void OnColumnWidthChanged(object? sender, EventArgs e) {
            if (_isAdjustingColumns || sender is not DataGridColumn changedColumn) return;
            if (changedColumn.Visibility != Visibility.Visible) return;

            DataGrid? grid = null;
            foreach (var g in new[] { TexturesDataGrid, ModelsDataGrid, MaterialsDataGrid }) {
                if (g.Columns.Contains(changedColumn)) {
                    grid = g;
                    break;
                }
            }
            if (grid == null) return;

            if (!_previousColumnWidths.TryGetValue(grid, out var prevWidths) || prevWidths.Length == 0) return;

            _isAdjustingColumns = true;
            try {
                int changedIndex = grid.Columns.IndexOf(changedColumn);
                if (changedIndex < 0 || changedIndex >= prevWidths.Length) return;

                double oldWidth = prevWidths[changedIndex];
                double newWidth = changedColumn.ActualWidth;
                double delta = newWidth - oldWidth;

                if (Math.Abs(delta) < 1) return;

                double remainingDelta = delta;

                if (delta > 0) {
                    var columnsToRight = GetVisibleColumnsAfter(grid, changedIndex);
                    remainingDelta = ShrinkColumns(columnsToRight, remainingDelta);

                    if (Math.Abs(remainingDelta) >= 1) {
                        var columnsToLeft = GetVisibleColumnsBefore(grid, changedIndex);
                        remainingDelta = ShrinkColumns(columnsToLeft, remainingDelta);
                    }
                } else {
                    var columnsToRight = GetVisibleColumnsAfter(grid, changedIndex);
                    if (columnsToRight.Count > 0) {
                        var rightNeighbor = columnsToRight[0];
                        rightNeighbor.Width = new DataGridLength(rightNeighbor.ActualWidth + (-delta));
                        remainingDelta = 0;
                    } else {
                        var columnsToLeft = GetVisibleColumnsBefore(grid, changedIndex);
                        if (columnsToLeft.Count > 0) {
                            var leftNeighbor = columnsToLeft[0];
                            leftNeighbor.Width = new DataGridLength(leftNeighbor.ActualWidth + (-delta));
                            remainingDelta = 0;
                        }
                    }
                }

                if (Math.Abs(remainingDelta) >= 1 && delta > 0) {
                    changedColumn.Width = new DataGridLength(oldWidth + (delta - remainingDelta));
                }

                UpdateStoredWidths(grid);
                if (grid == TexturesDataGrid) {
                    UpdateColumnHeadersBasedOnWidth(grid);
                }
                dataGridLayoutService.SaveColumnWidthsDebounced(grid, GetColumnWidthsSettingName(grid));
            } finally {
                _isAdjustingColumns = false;
            }
        }

        private double ShrinkColumns(List<DataGridColumn> columns, double deltaToDistribute) {
            double remaining = deltaToDistribute;
            foreach (var col in columns) {
                if (remaining < 1) break;

                double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                double colCurrent = col.ActualWidth;
                double available = colCurrent - colMin;

                if (available > 0) {
                    double shrinkBy = Math.Min(remaining, available);
                    col.Width = new DataGridLength(colCurrent - shrinkBy);
                    remaining -= shrinkBy;
                }
            }
            return remaining;
        }

        #endregion

        #region Column Helpers

        private List<DataGridColumn> GetVisibleColumnsBefore(int index) => GetVisibleColumnsBefore(TexturesDataGrid, index);

        private List<DataGridColumn> GetVisibleColumnsBefore(DataGrid grid, int index) {
            var changedColumn = grid.Columns[index];
            int changedDisplayIndex = changedColumn.DisplayIndex;

            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < changedDisplayIndex)
                .OrderByDescending(c => c.DisplayIndex)
                .ToList();
        }

        private List<DataGridColumn> GetVisibleColumnsAfter(int index) => GetVisibleColumnsAfter(TexturesDataGrid, index);

        private List<DataGridColumn> GetVisibleColumnsAfter(DataGrid grid, int index) {
            var changedColumn = grid.Columns[index];
            int changedDisplayIndex = changedColumn.DisplayIndex;

            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex > changedDisplayIndex)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
        }

        private DataGridColumn? GetLastVisibleColumn(DataGrid grid) {
            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderByDescending(c => c.DisplayIndex)
                .FirstOrDefault();
        }

        private void FillRemainingSpace(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;

            bool hasSavedWidths = dataGridLayoutService.HasSavedWidths(grid);
            dataGridLayoutService.FillRemainingSpace(grid, hasSavedWidths);
            UpdateStoredWidths(grid);

            if (!hasSavedWidths) {
                dataGridLayoutService.SaveColumnWidthsDebounced(grid, GetColumnWidthsSettingName(grid));
            }
        }

        private void UpdateStoredWidths() => UpdateStoredWidths(TexturesDataGrid);

        private void UpdateStoredWidths(DataGrid grid) {
            if (grid == null || !_previousColumnWidths.TryGetValue(grid, out var prevWidths)) return;
            for (int i = 0; i < grid.Columns.Count && i < prevWidths.Length; i++) {
                prevWidths[i] = grid.Columns[i].ActualWidth;
            }
        }

        private void AdjustLastColumnToFill(DataGrid grid) => FillRemainingSpace(grid);

        private void UpdateColumnHeadersBasedOnWidth(DataGrid grid) {
            if (grid == null || grid.Columns.Count <= 1) return;

            for (int i = 0; i < TextureColumnHeaders.Length && i + 1 < grid.Columns.Count; i++) {
                var column = grid.Columns[i + 1];
                double actualWidth = column.ActualWidth;

                if (actualWidth <= 0) continue;

                bool needShort = actualWidth < TextureColumnHeaders[i].MinWidthForFull;

                if (needShort != _columnUsingShortHeader[i]) {
                    _columnUsingShortHeader[i] = needShort;
                    column.Header = needShort ? TextureColumnHeaders[i].Short : TextureColumnHeaders[i].Full;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) {
                    yield return typedChild;
                }
                foreach (var descendant in FindVisualChildren<T>(child)) {
                    yield return descendant;
                }
            }
        }

        #endregion

        #region Setting Names

        private string GetColumnWidthsSettingName(DataGrid grid) {
            if (grid == TexturesDataGrid) return nameof(AppSettings.TexturesColumnWidths);
            if (grid == ModelsDataGrid) return nameof(AppSettings.ModelsColumnWidths);
            if (grid == MaterialsDataGrid) return nameof(AppSettings.MaterialsColumnWidths);
            return nameof(AppSettings.TexturesColumnWidths);
        }

        private string GetColumnOrderSettingName(DataGrid grid) {
            if (grid == TexturesDataGrid) return nameof(AppSettings.TexturesColumnOrder);
            if (grid == ModelsDataGrid) return nameof(AppSettings.ModelsColumnOrder);
            if (grid == MaterialsDataGrid) return nameof(AppSettings.MaterialsColumnOrder);
            return nameof(AppSettings.TexturesColumnOrder);
        }

        private string GetColumnVisibilitySettingName(DataGrid grid) {
            if (grid == TexturesDataGrid) return nameof(AppSettings.TexturesColumnVisibility);
            if (grid == ModelsDataGrid) return nameof(AppSettings.ModelsColumnVisibility);
            if (grid == MaterialsDataGrid) return nameof(AppSettings.MaterialsColumnVisibility);
            return nameof(AppSettings.TexturesColumnVisibility);
        }

        #endregion

        #region Grid Initialization

        private void InitializeGridColumnsIfNeeded(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;
            if (dataGridLayoutService.IsLoaded(grid)) return;

            string visibilitySetting = GetColumnVisibilitySettingName(grid);
            string widthsSetting = GetColumnWidthsSettingName(grid);
            string orderSetting = GetColumnOrderSettingName(grid);

            dataGridLayoutService.LoadColumnVisibility(grid, visibilitySetting);
            dataGridLayoutService.LoadColumnWidths(grid, widthsSetting);
            dataGridLayoutService.LoadColumnOrder(grid, orderSetting);
            SubscribeToColumnWidthChanges(grid);
        }

        private void RestoreGridLayout(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;

            string visibilitySetting = GetColumnVisibilitySettingName(grid);
            string widthsSetting = GetColumnWidthsSettingName(grid);
            string orderSetting = GetColumnOrderSettingName(grid);

            dataGridLayoutService.LoadColumnVisibility(grid, visibilitySetting);
            dataGridLayoutService.LoadColumnWidths(grid, widthsSetting);
            dataGridLayoutService.LoadColumnOrder(grid, orderSetting);
            SubscribeToColumnWidthChanges(grid);
            dataGridLayoutService.FillRemainingSpace(grid, dataGridLayoutService.HasSavedWidths(grid));
            UpdateColumnHeadersBasedOnWidth(grid);
        }

        #endregion

        #region Column Visibility Menu

        private void TextureColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                int columnIndex = GetColumnIndexByTag(TexturesDataGrid, columnTag);

                if (columnIndex >= 0 && columnIndex < TexturesDataGrid.Columns.Count) {
                    TexturesDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    SubscribeToColumnWidthChanges();
                    AdjustLastColumnToFill(TexturesDataGrid);
                    dataGridLayoutService.SaveColumnVisibility(TexturesDataGrid, nameof(AppSettings.TexturesColumnVisibility));
                }
            }
        }

        private void MaterialColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                int columnIndex = GetColumnIndexByTag(MaterialsDataGrid, columnTag);

                if (columnIndex >= 0 && columnIndex < MaterialsDataGrid.Columns.Count) {
                    MaterialsDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    dataGridLayoutService.FillRemainingSpace(MaterialsDataGrid, dataGridLayoutService.HasSavedWidths(MaterialsDataGrid));
                    dataGridLayoutService.SaveColumnVisibility(MaterialsDataGrid, nameof(AppSettings.MaterialsColumnVisibility));
                }
            }
        }

        private void ModelColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                int columnIndex = GetColumnIndexByTag(ModelsDataGrid, columnTag);

                if (columnIndex >= 0 && columnIndex < ModelsDataGrid.Columns.Count) {
                    ModelsDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    dataGridLayoutService.FillRemainingSpace(ModelsDataGrid, dataGridLayoutService.HasSavedWidths(ModelsDataGrid));
                    dataGridLayoutService.SaveColumnVisibility(ModelsDataGrid, nameof(AppSettings.ModelsColumnVisibility));
                }
            }
        }

        private void LoadColumnVisibilityWithMenu(DataGrid grid, string settingName, ContextMenu? headerContextMenu) {
            dataGridLayoutService.LoadColumnVisibility(grid, settingName);

            if (headerContextMenu != null) {
                UpdateColumnVisibilityMenuItems(grid, headerContextMenu);
            }
        }

        private void UpdateColumnVisibilityMenuItems(DataGrid grid, ContextMenu contextMenu) {
            if (contextMenu.Items[0] is MenuItem columnsMenu && columnsMenu.Header?.ToString() == "Columns Visibility") {
                foreach (MenuItem item in columnsMenu.Items) {
                    if (item.Tag is string tag) {
                        int colIndex = GetColumnIndexByTag(grid, tag);
                        if (colIndex >= 0 && colIndex < grid.Columns.Count) {
                            item.IsChecked = grid.Columns[colIndex].Visibility == Visibility.Visible;
                        }
                    }
                }
            }
        }

        private int GetColumnIndexByTag(DataGrid grid, string tag) {
            if (grid == TexturesDataGrid) {
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "TextureName" => 3, "Extension" => 4,
                    "Size" => 5, "Compressed" => 6, "Resolution" => 7, "ResizeResolution" => 8,
                    "Compression" => 9, "Mipmaps" => 10, "Preset" => 11, "Status" => 12, "Upload" => 13,
                    _ => -1
                };
            } else if (grid == ModelsDataGrid) {
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "Name" => 3, "Size" => 4,
                    "UVChannels" => 5, "Extension" => 6, "Status" => 7,
                    _ => -1
                };
            } else if (grid == MaterialsDataGrid) {
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "Name" => 3, "Master" => 4, "Status" => 5,
                    _ => -1
                };
            }
            return -1;
        }

        #endregion

        #region Scale Slider

        private void TableScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            RefreshStarColumns(TexturesDataGrid);
            RefreshStarColumns(ModelsDataGrid);
            RefreshStarColumns(MaterialsDataGrid);
        }

        private static void RefreshStarColumns(DataGrid? dataGrid) {
            if (dataGrid == null || !dataGrid.IsLoaded) return;

            foreach (var col in dataGrid.Columns) {
                if (col.Width.IsStar) {
                    var starValue = col.Width.Value;
                    col.Width = new DataGridLength(0, DataGridLengthUnitType.Auto);
                    col.Width = new DataGridLength(starValue, DataGridLengthUnitType.Star);
                }
            }
        }

        #endregion
    }
}

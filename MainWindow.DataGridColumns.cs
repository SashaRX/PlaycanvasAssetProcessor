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
    /// Partial class containing DataGrid column management logic:
    /// - Column header text adjustment based on width
    /// - Column resizing with left/right gripper handling
    /// - Column visibility management
    /// - Column width persistence
    /// </summary>
    public partial class MainWindow {
        // Column header definitions: (Full name, Short name, MinWidthForFull)
        // MinWidthForFull = minimum column width needed to show full name
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

        // Track current header state per column to avoid unnecessary updates
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

        #region TexturesDataGrid Events

        private void TexturesDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdate(grid);
        }

        private void DebouncedResizeUpdate(DataGrid grid) {
            _resizeDebounceTimer?.Stop();
            _resizeDebounceTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(16) // ~1 frame
            };
            _resizeDebounceTimer.Tick += (s, e) => {
                _resizeDebounceTimer?.Stop();
                UpdateColumnHeadersBasedOnWidth(grid);
                dataGridLayoutService.FillRemainingSpace(grid, dataGridLayoutService.HasSavedWidths(grid));
            };
            _resizeDebounceTimer.Start();
        }

        private void TexturesDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            // After column reorder - recalculate, fill space, and save order
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(TexturesDataGrid);
                SubscribeDataGridColumnResizing(TexturesDataGrid);
                dataGridLayoutService.FillRemainingSpace(TexturesDataGrid, dataGridLayoutService.HasSavedWidths(TexturesDataGrid));
                dataGridLayoutService.SaveColumnOrder(TexturesDataGrid, GetColumnOrderSettingName(TexturesDataGrid));
            }), DispatcherPriority.Background);
        }

        #endregion

        #region Models/Materials DataGrid Events

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

        #region Column Width Change Subscription

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

            // Subscribe to left gripper thumb events after visual tree is ready
            Dispatcher.BeginInvoke(() => SubscribeToLeftGripperThumbs(), DispatcherPriority.Loaded);
        }

        private void SubscribeToLeftGripperThumbs() {
            // Subscribe to all DataGrids
            SubscribeDataGridColumnResizing(TexturesDataGrid);
            SubscribeDataGridColumnResizing(ModelsDataGrid);
            SubscribeDataGridColumnResizing(MaterialsDataGrid);
        }

        private void SubscribeDataGridColumnResizing(DataGrid grid) {
            if (grid == null) return;

            // Skip if already subscribed - prevents expensive FindVisualChildren on every resize
            if (_leftGripperSubscribedGrids.Contains(grid)) return;

            // Subscribe to column headers for left edge mouse handling
            var columnHeaders = FindVisualChildren<DataGridColumnHeader>(grid).ToList();

            // Skip if no headers found yet (grid not visible)
            if (columnHeaders.Count == 0) return;

            foreach (var header in columnHeaders) {
                if (header.Column == null) continue;
                header.PreviewMouseLeftButtonDown += OnHeaderMouseDown;
                header.PreviewMouseMove += OnHeaderMouseMoveForCursor;
            }

            // Subscribe to move/up events on DataGrid level for dragging
            grid.PreviewMouseMove += OnDataGridMouseMove;
            grid.PreviewMouseLeftButtonUp += OnDataGridMouseUp;
            grid.LostMouseCapture += OnDataGridLostCapture;
            _leftGripperSubscribedGrids.Add(grid);
        }

        #endregion

        #region Left Gripper Dragging

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e) {
            if (sender is not DataGridColumnHeader header || header.Column == null) {
                return;
            }

            var dataGrid = FindParentDataGrid(header);
            if (dataGrid == null) return;

            Point pos = e.GetPosition(header);
            double headerWidth = header.ActualWidth;
            int currentIndex = dataGrid.Columns.IndexOf(header.Column);

            // Check if click is on LEFT edge (resize with left neighbor)
            if (pos.X <= LeftGripperZoneWidth) {
                var columnsToLeft = GetVisibleColumnsBefore(dataGrid, currentIndex);
                if (columnsToLeft.Count == 0) return;

                StartLeftGripperDrag(dataGrid, header.Column, columnsToLeft, e);
                return;
            }

            // Check if click is on RIGHT edge (resize with right neighbor)
            if (pos.X >= headerWidth - LeftGripperZoneWidth) {
                var columnsToRight = GetVisibleColumnsAfter(dataGrid, currentIndex);
                if (columnsToRight.Count == 0) return;

                var rightNeighbor = columnsToRight[0];
                var leftColumnsForRight = GetVisibleColumnsBefore(dataGrid, dataGrid.Columns.IndexOf(rightNeighbor));

                StartLeftGripperDrag(dataGrid, rightNeighbor, leftColumnsForRight, e);
                return;
            }
        }

        private DataGrid? FindParentDataGrid(DependencyObject element) {
            while (element != null) {
                if (element is DataGrid dg) return dg;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private void StartLeftGripperDrag(DataGrid dataGrid, DataGridColumn currentColumn, List<DataGridColumn> columnsToLeft, MouseButtonEventArgs e) {
            _isLeftGripperDragging = true;
            _currentResizingDataGrid = dataGrid;
            _leftGripperStartPoint = e.GetPosition(dataGrid);
            _leftGripperColumn = currentColumn;
            _leftGripperLeftColumns = columnsToLeft;
            _leftGripperColumnWidth = currentColumn.ActualWidth;

            int currentIndex = dataGrid.Columns.IndexOf(currentColumn);
            _leftGripperRightColumns = GetVisibleColumnsAfter(dataGrid, currentIndex);

            _leftGripperColumnWidths = new Dictionary<DataGridColumn, double>();
            foreach (var col in _leftGripperLeftColumns) {
                _leftGripperColumnWidths[col] = col.ActualWidth;
                if (col.Width.IsStar) {
                    col.Width = new DataGridLength(col.ActualWidth);
                }
            }
            foreach (var col in _leftGripperRightColumns) {
                _leftGripperColumnWidths[col] = col.ActualWidth;
                if (col.Width.IsStar) {
                    col.Width = new DataGridLength(col.ActualWidth);
                }
            }
            if (_leftGripperColumn.Width.IsStar) {
                _leftGripperColumn.Width = new DataGridLength(_leftGripperColumnWidth);
            }

            dataGrid.CaptureMouse();
            dataGrid.Cursor = Cursors.SizeWE;
            e.Handled = true;
        }

        private void OnHeaderMouseMoveForCursor(object sender, MouseEventArgs e) {
            if (sender is not DataGridColumnHeader header || header.Column == null) return;
            if (_isLeftGripperDragging) return;

            var dataGrid = FindParentDataGrid(header);
            if (dataGrid == null) return;

            Point pos = e.GetPosition(header);
            double headerWidth = header.ActualWidth;
            int colIndex = dataGrid.Columns.IndexOf(header.Column);

            if (pos.X <= LeftGripperZoneWidth) {
                var leftCols = GetVisibleColumnsBefore(dataGrid, colIndex);
                if (leftCols.Count > 0) {
                    header.Cursor = Cursors.SizeWE;
                    return;
                }
            }

            if (pos.X >= headerWidth - LeftGripperZoneWidth) {
                var rightCols = GetVisibleColumnsAfter(dataGrid, colIndex);
                if (rightCols.Count > 0) {
                    header.Cursor = Cursors.SizeWE;
                    return;
                }
            }

            header.Cursor = null;
        }

        private void OnDataGridMouseMove(object sender, MouseEventArgs e) {
            if (!_isLeftGripperDragging || _currentResizingDataGrid == null) return;
            if (_leftGripperColumn == null || _leftGripperLeftColumns == null || _leftGripperColumnWidths == null) {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed) {
                StopLeftGripperDrag();
                return;
            }

            Point currentPoint = e.GetPosition(_currentResizingDataGrid);
            double delta = currentPoint.X - _leftGripperStartPoint.X;
            _leftGripperStartPoint = currentPoint;

            if (Math.Abs(delta) < 1) return;

            double currentMin = _leftGripperColumn.MinWidth > 0 ? _leftGripperColumn.MinWidth : 30;

            _isAdjustingColumns = true;
            try {
                if (delta < 0) {
                    // Dragging LEFT - shrink columns to the left, expand current
                    double remainingShrink = -delta;

                    double totalShrinkable = 0;
                    foreach (var col in _leftGripperLeftColumns) {
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        totalShrinkable += _leftGripperColumnWidths[col] - colMin;
                    }
                    remainingShrink = Math.Min(remainingShrink, Math.Max(0, totalShrinkable));

                    for (int i = 0; i < _leftGripperLeftColumns.Count && remainingShrink > 0; i++) {
                        var col = _leftGripperLeftColumns[i];
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        double colWidth = _leftGripperColumnWidths[col];
                        double available = colWidth - colMin;

                        if (available > 0) {
                            double shrink = Math.Min(remainingShrink, available);
                            _leftGripperColumnWidths[col] = colWidth - shrink;
                            col.Width = new DataGridLength(_leftGripperColumnWidths[col]);
                            _leftGripperColumnWidth += shrink;
                            remainingShrink -= shrink;
                        }
                    }
                    _leftGripperColumn.Width = new DataGridLength(_leftGripperColumnWidth);
                } else if (delta > 0 && _leftGripperRightColumns != null) {
                    // Dragging RIGHT - shrink current and columns to the right, expand left
                    double totalShrinkable = Math.Max(0, _leftGripperColumnWidth - currentMin);
                    foreach (var col in _leftGripperRightColumns) {
                        if (!_leftGripperColumnWidths.ContainsKey(col)) continue;
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        totalShrinkable += Math.Max(0, _leftGripperColumnWidths[col] - colMin);
                    }

                    double remainingShrink = Math.Min(delta, totalShrinkable);
                    double originalRemaining = remainingShrink;

                    double currentAvailable = _leftGripperColumnWidth - currentMin;
                    double shrinkFromCurrent = Math.Min(remainingShrink, Math.Max(0, currentAvailable));
                    if (shrinkFromCurrent > 0) {
                        _leftGripperColumnWidth -= shrinkFromCurrent;
                        remainingShrink -= shrinkFromCurrent;
                    }

                    for (int i = 0; i < _leftGripperRightColumns.Count && remainingShrink > 0; i++) {
                        var col = _leftGripperRightColumns[i];
                        if (!_leftGripperColumnWidths.ContainsKey(col)) continue;
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        double colWidth = _leftGripperColumnWidths[col];
                        double available = colWidth - colMin;

                        if (available > 0) {
                            double shrink = Math.Min(remainingShrink, available);
                            _leftGripperColumnWidths[col] = colWidth - shrink;
                            col.Width = new DataGridLength(_leftGripperColumnWidths[col]);
                            remainingShrink -= shrink;
                        }
                    }

                    double totalShrunk = originalRemaining - remainingShrink;
                    if (totalShrunk > 0 && _leftGripperLeftColumns.Count > 0) {
                        _leftGripperColumn.Width = new DataGridLength(_leftGripperColumnWidth);

                        var leftNeighbor = _leftGripperLeftColumns[0];
                        if (_leftGripperColumnWidths.ContainsKey(leftNeighbor)) {
                            _leftGripperColumnWidths[leftNeighbor] += totalShrunk;
                            leftNeighbor.Width = new DataGridLength(_leftGripperColumnWidths[leftNeighbor]);
                        }
                    }
                }

                UpdateStoredWidths(_currentResizingDataGrid);
                if (_currentResizingDataGrid == TexturesDataGrid) {
                    UpdateColumnHeadersBasedOnWidth(TexturesDataGrid);
                }
            } finally {
                _isAdjustingColumns = false;
            }

            e.Handled = true;
        }

        private void StopLeftGripperDrag() {
            if (_isLeftGripperDragging && _currentResizingDataGrid != null) {
                var gridToSave = _currentResizingDataGrid;
                _isLeftGripperDragging = false;
                _leftGripperColumn = null;
                _leftGripperLeftColumns = null;
                _leftGripperRightColumns = null;
                _leftGripperColumnWidths = null;
                // Save reference before ReleaseMouseCapture triggers LostMouseCapture
                var grid = _currentResizingDataGrid;
                _currentResizingDataGrid = null;
                grid.ReleaseMouseCapture();
                grid.Cursor = null;
                dataGridLayoutService.SaveColumnWidthsDebounced(gridToSave, GetColumnWidthsSettingName(gridToSave));
            }
        }

        private void OnDataGridMouseUp(object sender, MouseButtonEventArgs e) {
            if (_isLeftGripperDragging) {
                StopLeftGripperDrag();
                e.Handled = true;
            }
        }

        private void OnDataGridLostCapture(object sender, MouseEventArgs e) {
            _isLeftGripperDragging = false;
            _leftGripperColumn = null;
            _leftGripperLeftColumns = null;
            _leftGripperRightColumns = null;
            _leftGripperColumnWidths = null;
            if (_currentResizingDataGrid != null) {
                _currentResizingDataGrid.Cursor = null;
                _currentResizingDataGrid = null;
            }
        }

        #endregion

        #region Column Width Change Handling

        private void OnColumnWidthChanged(object? sender, EventArgs e) {
            if (_isAdjustingColumns || sender is not DataGridColumn changedColumn) return;
            if (changedColumn.Visibility != Visibility.Visible) return;

            // Find which DataGrid owns this column
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
                    // Column is EXPANDING - shrink columns to the RIGHT first
                    var columnsToRight = GetVisibleColumnsAfter(grid, changedIndex);
                    remainingDelta = ShrinkColumns(columnsToRight, remainingDelta);

                    // If still have remaining, try shrinking columns to the LEFT
                    if (Math.Abs(remainingDelta) >= 1) {
                        var columnsToLeft = GetVisibleColumnsBefore(grid, changedIndex);
                        remainingDelta = ShrinkColumns(columnsToLeft, remainingDelta);
                    }
                } else {
                    // Column is SHRINKING - expand the nearest neighbor to fill space
                    var columnsToRight = GetVisibleColumnsAfter(grid, changedIndex);
                    if (columnsToRight.Count > 0) {
                        var rightNeighbor = columnsToRight[0];
                        double expandBy = -delta;
                        rightNeighbor.Width = new DataGridLength(rightNeighbor.ActualWidth + expandBy);
                        remainingDelta = 0;
                    } else {
                        // No columns to the right - expand nearest column to the LEFT
                        var columnsToLeft = GetVisibleColumnsBefore(grid, changedIndex);
                        if (columnsToLeft.Count > 0) {
                            var leftNeighbor = columnsToLeft[0];
                            double expandBy = -delta;
                            leftNeighbor.Width = new DataGridLength(leftNeighbor.ActualWidth + expandBy);
                            remainingDelta = 0;
                        }
                    }
                }

                // If couldn't distribute all delta, limit the change
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

            // Get visible columns to the left, ordered from nearest to farthest
            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < changedDisplayIndex)
                .OrderByDescending(c => c.DisplayIndex) // Nearest first
                .ToList();
        }

        private List<DataGridColumn> GetVisibleColumnsAfter(int index) => GetVisibleColumnsAfter(TexturesDataGrid, index);

        private List<DataGridColumn> GetVisibleColumnsAfter(DataGrid grid, int index) {
            var result = new List<DataGridColumn>();
            var sortedColumns = grid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            var changedColumn = grid.Columns[index];
            int changedDisplayIndex = changedColumn.DisplayIndex;

            foreach (var col in sortedColumns) {
                if (col.DisplayIndex > changedDisplayIndex) {
                    result.Add(col);
                }
            }
            return result;
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

            // Only save if not auto-adjusting after loading saved widths
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

        // Legacy method name for compatibility
        private void AdjustLastColumnToFill(DataGrid grid) => FillRemainingSpace(grid);

        private void UpdateColumnHeadersBasedOnWidth(DataGrid grid) {
            if (grid == null || grid.Columns.Count <= 1) return;

            // Start from column index 1 to skip Export checkbox column
            // TextureColumnHeaders[i] maps to grid.Columns[i + 1]
            for (int i = 0; i < TextureColumnHeaders.Length && i + 1 < grid.Columns.Count; i++) {
                var column = grid.Columns[i + 1];  // +1 to skip Export column
                double actualWidth = column.ActualWidth;

                // Skip if width not yet calculated
                if (actualWidth <= 0) continue;

                // Check if column width is less than minimum needed for full name
                bool needShort = actualWidth < TextureColumnHeaders[i].MinWidthForFull;

                // Only update if state changed
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

            // Load saved visibility, widths, and order
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

        #region Column Visibility Menu Handlers

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

            // Update context menu checkboxes
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
                // 0=Export, 1=№, 2=ID, 3=TextureName, 4=Extension, 5=Size, 6=Compressed,
                // 7=Resolution, 8=ResizeResolution, 9=Compression, 10=Mipmaps, 11=Preset, 12=Status, 13=Upload
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "TextureName" => 3, "Extension" => 4,
                    "Size" => 5, "Compressed" => 6, "Resolution" => 7, "ResizeResolution" => 8,
                    "Compression" => 9, "Mipmaps" => 10, "Preset" => 11, "Status" => 12, "Upload" => 13,
                    _ => -1
                };
            } else if (grid == ModelsDataGrid) {
                // 0=Export, 1=№, 2=ID, 3=Name, 4=Size, 5=UVChannels, 6=Extension, 7=Status
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "Name" => 3, "Size" => 4,
                    "UVChannels" => 5, "Extension" => 6, "Status" => 7,
                    _ => -1
                };
            } else if (grid == MaterialsDataGrid) {
                // 0=Export, 1=№, 2=ID, 3=Name, 4=Master, 5=Status
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "Name" => 3, "Master" => 4, "Status" => 5,
                    _ => -1
                };
            }
            return -1;
        }

        #endregion

        #region Scale Slider

        /// <summary>
        /// Scale slider changed - force star columns to recalculate
        /// </summary>
        private void TableScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            RefreshStarColumns(TexturesDataGrid);
            RefreshStarColumns(ModelsDataGrid);
            RefreshStarColumns(MaterialsDataGrid);
        }

        /// <summary>
        /// Force star-width columns to recalculate by toggling their width
        /// </summary>
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

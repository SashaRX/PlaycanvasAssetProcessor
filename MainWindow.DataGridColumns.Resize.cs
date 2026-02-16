using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AssetProcessor {
    /// <summary>
    /// Left gripper column resizing logic for DataGrids.
    /// Allows resizing columns by dragging their left edge.
    /// </summary>
    public partial class MainWindow {

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
    }
}

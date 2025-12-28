using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Manages DataGrid sorting with proper state tracking and direction toggling.
    /// Solves the issue where SortDirection doesn't toggle correctly on repeated clicks.
    /// </summary>
    public class DataGridSortManager {
        private readonly DataGrid _dataGrid;
        private string? _currentSortColumn;
        private ListSortDirection _currentDirection = ListSortDirection.Ascending;
        private bool _isSorting;

        /// <summary>
        /// Gets whether a sort operation is currently in progress.
        /// </summary>
        public bool IsSorting => _isSorting;

        /// <summary>
        /// Event raised when sorting state changes.
        /// </summary>
        public event EventHandler<bool>? SortingStateChanged;

        public DataGridSortManager(DataGrid dataGrid) {
            _dataGrid = dataGrid ?? throw new ArgumentNullException(nameof(dataGrid));
        }

        /// <summary>
        /// Handles the Sorting event for a DataGrid column.
        /// Call this from the DataGrid.Sorting event handler.
        /// </summary>
        public void HandleSorting(DataGridSortingEventArgs e) {
            if (e?.Column == null) {
                return;
            }

            e.Handled = true;

            if (_dataGrid.ItemsSource == null) {
                return;
            }

            string sortMemberPath = GetSortMemberPath(e.Column);
            if (string.IsNullOrEmpty(sortMemberPath)) {
                e.Handled = false;
                return;
            }

            // Determine new direction based on our tracked state (not column's SortDirection)
            ListSortDirection newDirection;
            if (_currentSortColumn == sortMemberPath) {
                // Same column clicked - toggle direction
                newDirection = _currentDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            } else {
                // Different column - start with Ascending
                newDirection = ListSortDirection.Ascending;
            }

            // Update tracked state
            _currentSortColumn = sortMemberPath;
            _currentDirection = newDirection;

            ApplySort(sortMemberPath, newDirection, e.Column);
        }

        /// <summary>
        /// Applies sort to the DataGrid with the specified parameters.
        /// </summary>
        private void ApplySort(string sortMemberPath, ListSortDirection direction, DataGridColumn clickedColumn) {
            try {
                _isSorting = true;
                SortingStateChanged?.Invoke(this, true);

                ICollectionView? dataView = CollectionViewSource.GetDefaultView(_dataGrid.ItemsSource);
                if (dataView == null) {
                    return;
                }

                // Apply sorting
                if (dataView is ListCollectionView listView) {
                    // Use CustomSort for better performance (no reflection)
                    listView.CustomSort = new ResourceComparer(sortMemberPath, direction);
                } else {
                    // Fallback for non-ListCollectionView
                    using (dataView.DeferRefresh()) {
                        dataView.SortDescriptions.Clear();
                        dataView.SortDescriptions.Add(new SortDescription(sortMemberPath, direction));
                    }
                }

                // Update visual state of columns
                UpdateColumnSortDirections(clickedColumn, direction);

            } finally {
                // Reset sorting flag on next dispatcher cycle
                _dataGrid.Dispatcher.BeginInvoke(() => {
                    _isSorting = false;
                    SortingStateChanged?.Invoke(this, false);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Updates the SortDirection property of all columns.
        /// </summary>
        private void UpdateColumnSortDirections(DataGridColumn activeColumn, ListSortDirection direction) {
            foreach (var column in _dataGrid.Columns) {
                if (column == activeColumn) {
                    column.SortDirection = direction;
                } else {
                    column.SortDirection = null;
                }
            }
        }

        /// <summary>
        /// Gets the sort member path from a column.
        /// </summary>
        private static string GetSortMemberPath(DataGridColumn column) {
            // First try SortMemberPath
            if (!string.IsNullOrEmpty(column.SortMemberPath)) {
                return column.SortMemberPath;
            }

            // Then try to get from Binding
            if (column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding) {
                return binding.Path?.Path ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Resets the sort state. Call this when the data source changes.
        /// </summary>
        public void ResetSortState() {
            _currentSortColumn = null;
            _currentDirection = ListSortDirection.Ascending;

            // Clear visual state
            foreach (var column in _dataGrid.Columns) {
                column.SortDirection = null;
            }
        }

        /// <summary>
        /// Reapplies the current sort after data source changes.
        /// </summary>
        public void ReapplyCurrentSort() {
            if (string.IsNullOrEmpty(_currentSortColumn) || _dataGrid.ItemsSource == null) {
                return;
            }

            ICollectionView? dataView = CollectionViewSource.GetDefaultView(_dataGrid.ItemsSource);
            if (dataView == null) {
                return;
            }

            if (dataView is ListCollectionView listView) {
                listView.CustomSort = new ResourceComparer(_currentSortColumn, _currentDirection);
            } else {
                using (dataView.DeferRefresh()) {
                    dataView.SortDescriptions.Clear();
                    dataView.SortDescriptions.Add(new SortDescription(_currentSortColumn, _currentDirection));
                }
            }

            // Update visual state
            foreach (var column in _dataGrid.Columns) {
                string colPath = GetSortMemberPath(column);
                if (colPath == _currentSortColumn) {
                    column.SortDirection = _currentDirection;
                } else {
                    column.SortDirection = null;
                }
            }
        }
    }
}

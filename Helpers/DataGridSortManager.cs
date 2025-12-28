using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Manages DataGrid sorting with proper state tracking and direction toggling.
    /// Optimized for performance with large datasets.
    /// </summary>
    public class DataGridSortManager {
        private readonly DataGrid _dataGrid;
        private string? _currentSortColumn;
        private ListSortDirection _currentDirection = ListSortDirection.Ascending;
        private bool _isSorting;

        // Cache comparer to avoid repeated allocations
        private ResourceComparer? _cachedComparer;
        private string? _cachedComparerProperty;
        private ListSortDirection _cachedComparerDirection;

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

            // Use column's current SortDirection to determine next direction
            // null or Descending -> Ascending, Ascending -> Descending
            ListSortDirection newDirection = e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _currentSortColumn = sortMemberPath;
            _currentDirection = newDirection;

            ApplySortOptimized(sortMemberPath, newDirection, e.Column);
        }

        /// <summary>
        /// Optimized sort application with UI updates deferred.
        /// </summary>
        private void ApplySortOptimized(string sortMemberPath, ListSortDirection direction, DataGridColumn clickedColumn) {
            _isSorting = true;
            SortingStateChanged?.Invoke(this, true);

            try {
                ICollectionView? dataView = CollectionViewSource.GetDefaultView(_dataGrid.ItemsSource);
                if (dataView == null) {
                    return;
                }

                // Get or create cached comparer
                var comparer = GetOrCreateComparer(sortMemberPath, direction);

                if (dataView is ListCollectionView listView) {
                    // Use DeferRefresh to batch all changes
                    using (listView.DeferRefresh()) {
                        listView.CustomSort = comparer;
                    }
                } else {
                    using (dataView.DeferRefresh()) {
                        dataView.SortDescriptions.Clear();
                        dataView.SortDescriptions.Add(new SortDescription(sortMemberPath, direction));
                    }
                }

                // Update column headers after sort completes
                UpdateColumnSortDirections(clickedColumn, direction);

            } finally {
                // Reset on next dispatcher cycle
                _dataGrid.Dispatcher.BeginInvoke(() => {
                    _isSorting = false;
                    SortingStateChanged?.Invoke(this, false);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Gets cached comparer or creates new one if parameters changed.
        /// </summary>
        private ResourceComparer GetOrCreateComparer(string propertyName, ListSortDirection direction) {
            if (_cachedComparer != null &&
                _cachedComparerProperty == propertyName &&
                _cachedComparerDirection == direction) {
                return _cachedComparer;
            }

            _cachedComparer = new ResourceComparer(propertyName, direction);
            _cachedComparerProperty = propertyName;
            _cachedComparerDirection = direction;
            return _cachedComparer;
        }

        /// <summary>
        /// Updates SortDirection property of columns.
        /// </summary>
        private void UpdateColumnSortDirections(DataGridColumn activeColumn, ListSortDirection direction) {
            foreach (var column in _dataGrid.Columns) {
                column.SortDirection = column == activeColumn ? direction : null;
            }
        }

        /// <summary>
        /// Gets sort member path from column.
        /// </summary>
        private static string GetSortMemberPath(DataGridColumn column) {
            if (!string.IsNullOrEmpty(column.SortMemberPath)) {
                return column.SortMemberPath;
            }

            if (column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding) {
                return binding.Path?.Path ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Resets sort state.
        /// </summary>
        public void ResetSortState() {
            _currentSortColumn = null;
            _currentDirection = ListSortDirection.Ascending;
            _cachedComparer = null;

            foreach (var column in _dataGrid.Columns) {
                column.SortDirection = null;
            }
        }

        /// <summary>
        /// Reapplies current sort after data source changes.
        /// </summary>
        public void ReapplyCurrentSort() {
            if (string.IsNullOrEmpty(_currentSortColumn) || _dataGrid.ItemsSource == null) {
                return;
            }

            ICollectionView? dataView = CollectionViewSource.GetDefaultView(_dataGrid.ItemsSource);
            if (dataView == null) {
                return;
            }

            var comparer = GetOrCreateComparer(_currentSortColumn, _currentDirection);

            if (dataView is ListCollectionView listView) {
                using (listView.DeferRefresh()) {
                    listView.CustomSort = comparer;
                }
            } else {
                using (dataView.DeferRefresh()) {
                    dataView.SortDescriptions.Clear();
                    dataView.SortDescriptions.Add(new SortDescription(_currentSortColumn, _currentDirection));
                }
            }

            foreach (var column in _dataGrid.Columns) {
                string colPath = GetSortMemberPath(column);
                column.SortDirection = colPath == _currentSortColumn ? _currentDirection : null;
            }
        }
    }
}

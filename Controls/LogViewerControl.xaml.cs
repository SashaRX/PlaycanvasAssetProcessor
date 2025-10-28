using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NLog;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Log entry model for display
    /// </summary>
    public class LogEntry {
        public string Timestamp { get; set; } = "";
        public string Level { get; set; } = "";
        public string Logger { get; set; } = "";
        public string Message { get; set; } = "";
        public Brush LevelColor { get; set; } = Brushes.Black;
        public Brush MessageColor { get; set; } = Brushes.Black;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        public LogEntry(LogEventInfo logEvent) {
            Timestamp = logEvent.TimeStamp.ToString("HH:mm:ss.fff");
            Level = logEvent.Level.Name.ToUpper();
            Logger = logEvent.LoggerName?.Split('.').LastOrDefault() ?? "";
            Message = logEvent.FormattedMessage ?? "";
            LogLevel = logEvent.Level;

            // Color coding based on level
            LevelColor = GetLevelColor(logEvent.Level);
            MessageColor = GetMessageColor(Message);
        }

        private Brush GetLevelColor(LogLevel level) {
            if (level == LogLevel.Error || level == LogLevel.Fatal) {
                return new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red
            } else if (level == LogLevel.Warn) {
                return new SolidColorBrush(Color.FromRgb(245, 124, 0)); // Orange
            } else if (level == LogLevel.Info) {
                return new SolidColorBrush(Color.FromRgb(25, 118, 210)); // Blue
            } else if (level == LogLevel.Debug) {
                return new SolidColorBrush(Color.FromRgb(96, 96, 96)); // Gray
            }
            return Brushes.Black;
        }

        private Brush GetMessageColor(string message) {
            // Highlight special messages
            if (message.Contains("✓") || message.Contains("SUCCESS") || message.Contains("успешно")) {
                return new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
            } else if (message.Contains("✗") || message.Contains("FAIL") || message.Contains("ошибк")) {
                return new SolidColorBrush(Color.FromRgb(198, 40, 40)); // Dark Red
            } else if (message.Contains("━━━") || message.Contains("📊") || message.Contains("🔧")) {
                return new SolidColorBrush(Color.FromRgb(123, 31, 162)); // Purple (headers)
            }
            return Brushes.Black;
        }
    }

    public partial class LogViewerControl : UserControl {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ObservableCollection<LogEntry> _allLogs = new();
        private readonly ObservableCollection<LogEntry> _filteredLogs = new();
        private bool _autoScroll = true;
        private string _searchText = "";

        public LogViewerControl() {
            InitializeComponent();
            LogListView.ItemsSource = _filteredLogs;
            UpdateAutoScrollButton();

            // Subscribe to NLog events via custom target
            LogTarget.Instance.LogReceived += OnLogReceived;

            Logger.Info("Log Viewer initialized");
        }

        private void OnLogReceived(LogEventInfo logEvent) {
            // Must run on UI thread
            Dispatcher.Invoke(() => {
                var entry = new LogEntry(logEvent);
                _allLogs.Add(entry);

                // Apply filters
                if (ShouldShowLog(entry)) {
                    _filteredLogs.Add(entry);

                    // Auto-scroll to bottom
                    if (_autoScroll && _filteredLogs.Count > 0) {
                        LogListView.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
                    }
                }

                UpdateStatusBar();
            });
        }

        private bool ShouldShowLog(LogEntry entry) {
            // Filter by level
            bool levelMatch = false;
            if (FilterDebugBtn.IsChecked == true && entry.LogLevel == LogLevel.Debug) levelMatch = true;
            if (FilterInfoBtn.IsChecked == true && entry.LogLevel == LogLevel.Info) levelMatch = true;
            if (FilterWarnBtn.IsChecked == true && entry.LogLevel == LogLevel.Warn) levelMatch = true;
            if (FilterErrorBtn.IsChecked == true && (entry.LogLevel == LogLevel.Error || entry.LogLevel == LogLevel.Fatal)) levelMatch = true;

            if (!levelMatch) return false;

            // Filter by search text
            if (!string.IsNullOrWhiteSpace(_searchText)) {
                return entry.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                       entry.Logger.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void ApplyFilters() {
            _filteredLogs.Clear();
            foreach (var log in _allLogs) {
                if (ShouldShowLog(log)) {
                    _filteredLogs.Add(log);
                }
            }
            UpdateStatusBar();

            if (_autoScroll && _filteredLogs.Count > 0) {
                LogListView.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
            }
        }

        private void UpdateStatusBar() {
            LogCountText.Text = $"{_filteredLogs.Count} / {_allLogs.Count} logs";

            if (_filteredLogs.Count < _allLogs.Count) {
                StatusText.Text = $"Filtered: {_allLogs.Count - _filteredLogs.Count} logs hidden";
            } else {
                StatusText.Text = "Ready";
            }
        }

        private void UpdateAutoScrollButton() {
            AutoScrollBtn.Content = _autoScroll ? "📌 Auto-scroll ON" : "📌 Auto-scroll OFF";
            AutoScrollBtn.FontWeight = _autoScroll ? FontWeights.Bold : FontWeights.Normal;
        }

        // Event Handlers
        private void FilterButton_Changed(object sender, RoutedEventArgs e) {
            ApplyFilters();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            _searchText = SearchTextBox.Text;
            ApplyFilters();
        }

        private void AutoScrollBtn_Click(object sender, RoutedEventArgs e) {
            _autoScroll = !_autoScroll;
            UpdateAutoScrollButton();

            if (_autoScroll && _filteredLogs.Count > 0) {
                LogListView.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
            }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e) {
            var result = MessageBox.Show(
                "Are you sure you want to clear all logs?",
                "Clear Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) {
                _allLogs.Clear();
                _filteredLogs.Clear();
                UpdateStatusBar();
                Logger.Info("Logs cleared by user");
            }
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e) {
            var dialog = new SaveFileDialog {
                Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*",
                FileName = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    using var writer = new StreamWriter(dialog.FileName);
                    foreach (var log in _filteredLogs) {
                        writer.WriteLine($"{log.Timestamp} [{log.Level}] {log.Logger}: {log.Message}");
                    }
                    Logger.Info($"Logs exported to {dialog.FileName}");
                    MessageBox.Show($"Logs exported successfully to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    Logger.Error($"Failed to export logs: {ex.Message}");
                    MessageBox.Show($"Failed to export logs:\n{ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    /// <summary>
    /// Custom NLog target for capturing logs in memory
    /// </summary>
    public class LogTarget : NLog.Targets.TargetWithLayout {
        private static LogTarget? _instance;
        public static LogTarget Instance => _instance ??= new LogTarget();

        public event Action<LogEventInfo>? LogReceived;

        protected override void Write(LogEventInfo logEvent) {
            LogReceived?.Invoke(logEvent);
        }
    }
}

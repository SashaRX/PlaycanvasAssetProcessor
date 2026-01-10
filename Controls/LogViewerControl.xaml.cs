using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AssetProcessor.Helpers;
using Microsoft.Win32;
using NLog;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Converter that returns true if value is null, false otherwise
    /// </summary>
    public class NullCheckConverter : IValueConverter {
        public static readonly NullCheckConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return value == null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Log entry model for display
    /// </summary>
    public class LogEntry {
        public string Timestamp { get; set; } = "";
        public string Level { get; set; } = "";
        public string Logger { get; set; } = "";
        public string Message { get; set; } = "";
        public Brush LevelColor { get; set; } = Brushes.Gray;
        public Brush MessageColor { get; set; } = Brushes.Gray;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        public LogEntry(LogEventInfo logEvent) {
            Timestamp = logEvent.TimeStamp.ToString("HH:mm:ss.fff");
            Level = logEvent.Level.Name.ToUpper();
            Logger = logEvent.LoggerName?.Split('.').LastOrDefault() ?? "";
            Message = logEvent.FormattedMessage ?? "";
            LogLevel = logEvent.Level;

            // Color coding based on level (theme-aware)
            LevelColor = GetLevelColor(logEvent.Level);
            MessageColor = GetMessageColor(logEvent.Level, Message);
        }

        private static Brush GetLevelColor(LogLevel level) {
            if (level == LogLevel.Error || level == LogLevel.Fatal) {
                return ThemeBrushCache.LogErrorBrush;
            } else if (level == LogLevel.Warn) {
                return ThemeBrushCache.LogWarnBrush;
            } else if (level == LogLevel.Info) {
                return ThemeBrushCache.LogInfoBrush;
            } else if (level == LogLevel.Debug) {
                return ThemeBrushCache.LogDebugBrush;
            }
            return ThemeBrushCache.GetThemeForeground();
        }

        private static Brush GetMessageColor(LogLevel level, string message) {
            // Highlight special messages with cached frozen brushes
            if (message.Contains("✓") || message.Contains("SUCCESS") || message.Contains("успешно")) {
                return ThemeBrushCache.SuccessBrush;
            } else if (message.Contains("✗") || message.Contains("FAIL") || message.Contains("ошибк")) {
                return ThemeBrushCache.FailureBrush;
            } else if (message.Contains("━━━") || message.Contains("📊") || message.Contains("🔧")) {
                return ThemeBrushCache.SpecialBrush;
            }
            // Return null for normal messages - XAML will use ThemeForeground as fallback
            return null!;
        }
    }

    public partial class LogViewerControl : UserControl {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ObservableCollection<LogEntry> _allLogs = new();
        private readonly ObservableCollection<LogEntry> _filteredLogs = new();
        private bool _autoScroll = true;
        private string _searchText = "";
        private bool _isInitialized = false;

        public LogViewerControl() {
            InitializeComponent();

            if (LogDataGrid != null) {
                LogDataGrid.ItemsSource = _filteredLogs;
            }

            UpdateAutoScrollButton();

            // Register custom target and subscribe in Loaded event to avoid initialization issues
            Loaded += LogViewerControl_Loaded;
        }

        private void LogViewerControl_Loaded(object sender, RoutedEventArgs e) {
            // Only register once
            if (!_isInitialized) {
                _isInitialized = true;

                // Register custom target programmatically
                RegisterLogTarget();

                // Subscribe to NLog events via custom target
                if (LogTarget.Instance != null) {
                    LogTarget.Instance.LogReceived += OnLogReceived;
                }

                Logger.Info("Log Viewer initialized");
            }
        }

        private void RegisterLogTarget() {
            try {
                var config = LogManager.Configuration;
                if (config == null) {
                    // Create new configuration if none exists
                    config = new NLog.Config.LoggingConfiguration();
                }

                // Check if already registered
                if (config.FindTargetByName<LogTarget>("memoryTarget") == null) {
                    // Add memory target
                    config.AddTarget("memoryTarget", LogTarget.Instance);

                    // Add rule to route all logs to memory target
                    var rule = new NLog.Config.LoggingRule("*", LogLevel.Info, LogTarget.Instance);
                    config.LoggingRules.Add(rule);

                    // Reconfigure NLog
                    LogManager.Configuration = config;
                }
            } catch (Exception ex) {
                // Fallback: write to debug output if NLog setup fails
                System.Diagnostics.Debug.WriteLine($"Failed to register log target: {ex.Message}");
            }
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
                        LogDataGrid.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
                    }
                }

                UpdateStatusBar();
            });
        }

        private bool ShouldShowLog(LogEntry entry) {
            // Filter by level (if buttons are initialized)
            bool levelMatch = false;
            if (FilterDebugBtn?.IsChecked == true && entry.LogLevel == LogLevel.Debug) levelMatch = true;
            if (FilterInfoBtn?.IsChecked == true && entry.LogLevel == LogLevel.Info) levelMatch = true;
            if (FilterWarnBtn?.IsChecked == true && entry.LogLevel == LogLevel.Warn) levelMatch = true;
            if (FilterErrorBtn?.IsChecked == true && (entry.LogLevel == LogLevel.Error || entry.LogLevel == LogLevel.Fatal)) levelMatch = true;

            // If no buttons are initialized yet, show all logs
            if (FilterDebugBtn == null && FilterInfoBtn == null && FilterWarnBtn == null && FilterErrorBtn == null) {
                levelMatch = true;
            }

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

            if (_autoScroll && _filteredLogs.Count > 0 && LogDataGrid != null) {
                LogDataGrid.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
            }
        }

        private void UpdateStatusBar() {
            if (LogCountText != null) {
                LogCountText.Text = $"{_filteredLogs.Count} / {_allLogs.Count} logs";
            }

            if (StatusText != null) {
                if (_filteredLogs.Count < _allLogs.Count) {
                    StatusText.Text = $"Filtered: {_allLogs.Count - _filteredLogs.Count} logs hidden";
                } else {
                    StatusText.Text = "Ready";
                }
            }
        }

        private void UpdateAutoScrollButton() {
            if (AutoScrollBtn != null) {
                AutoScrollBtn.Content = _autoScroll ? "📌 Auto-scroll ON" : "📌 Auto-scroll OFF";
                AutoScrollBtn.FontWeight = _autoScroll ? FontWeights.Bold : FontWeights.Normal;
            }
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

            if (_autoScroll && _filteredLogs.Count > 0 && LogDataGrid != null) {
                LogDataGrid.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
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

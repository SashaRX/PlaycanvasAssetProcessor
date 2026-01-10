using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Thread-safe cache for theme-aware brushes. Freezes brushes for performance
    /// and invalidates cache on theme changes.
    /// </summary>
    public static class ThemeBrushCache {
        private static readonly ConcurrentDictionary<string, Brush> _cache = new();
        private static bool _isDarkTheme = ThemeHelper.IsDarkTheme;

        // Static frozen brushes for fixed colors (not theme-dependent)
        // Sync status colors
        public static readonly Brush SyncedBrush = CreateFrozenBrush(76, 175, 80);       // Green
        public static readonly Brush LocalOnlyBrush = CreateFrozenBrush(255, 152, 0);    // Orange
        public static readonly Brush ServerOnlyBrush = CreateFrozenBrush(100, 181, 246); // Light Blue
        public static readonly Brush HashMismatchBrush = CreateFrozenBrush(244, 67, 54); // Red
        public static readonly Brush OutdatedBrush = CreateFrozenBrush(255, 167, 38);    // Dark Orange
        public static readonly Brush UnknownBrush = CreateFrozenBrush(158, 158, 158);    // Gray

        // Log level brushes
        public static readonly Brush LogErrorBrush = CreateFrozenBrush(211, 47, 47);     // Red
        public static readonly Brush LogWarnBrush = CreateFrozenBrush(245, 124, 0);      // Orange
        public static readonly Brush LogInfoBrush = CreateFrozenBrush(86, 156, 214);     // Light Blue
        public static readonly Brush LogDebugBrush = CreateFrozenBrush(128, 128, 128);   // Gray

        // Message highlight brushes
        public static readonly Brush SuccessBrush = CreateFrozenBrush(76, 175, 80);      // Green
        public static readonly Brush FailureBrush = CreateFrozenBrush(244, 67, 54);      // Red
        public static readonly Brush SpecialBrush = CreateFrozenBrush(156, 39, 176);     // Purple

        // Theme foreground cached brushes
        private static readonly Brush _darkForeground = CreateFrozenBrush(220, 220, 220);
        private static readonly Brush _lightForeground = CreateFrozenBrush(32, 32, 32);

        /// <summary>
        /// Get a cached theme-aware brush by resource key.
        /// Automatically invalidates cache if theme has changed.
        /// </summary>
        public static Brush GetThemeBrush(string resourceKey, Brush fallback) {
            // Check for theme change
            bool currentTheme = ThemeHelper.IsDarkTheme;
            if (currentTheme != _isDarkTheme) {
                _cache.Clear();
                _isDarkTheme = currentTheme;
            }

            return _cache.GetOrAdd(resourceKey, key => {
                var brush = Application.Current?.Resources[key] as Brush ?? fallback;
                // Freeze if not already frozen for thread-safety and performance
                if (brush is Freezable freezable && !freezable.IsFrozen && freezable.CanFreeze) {
                    brush = (Brush)freezable.Clone();
                    ((Freezable)brush).Freeze();
                }
                return brush;
            });
        }

        /// <summary>
        /// Force cache invalidation (call after theme change)
        /// </summary>
        public static void InvalidateCache() {
            _cache.Clear();
            _isDarkTheme = ThemeHelper.IsDarkTheme;
        }

        /// <summary>
        /// Get theme-aware foreground brush (for log messages)
        /// </summary>
        public static Brush GetThemeForeground() {
            return ThemeHelper.IsDarkTheme ? _darkForeground : _lightForeground;
        }

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b) {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}

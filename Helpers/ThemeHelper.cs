using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media;

namespace AssetProcessor.Helpers {
    public enum ThemeMode {
        Auto,
        Light,
        Dark
    }

    public static class ThemeHelper {
        private static ThemeMode _currentMode = ThemeMode.Auto;

        public static ThemeMode CurrentMode {
            get => _currentMode;
            set {
                _currentMode = value;
                ApplyTheme(Application.Current);
            }
        }

        public static bool IsSystemDarkTheme {
            get {
                try {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var value = key?.GetValue("AppsUseLightTheme");
                    return value is int intValue && intValue == 0;
                } catch {
                    return false;
                }
            }
        }

        public static bool IsDarkTheme => _currentMode switch {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => IsSystemDarkTheme
        };

        public static void ApplyTheme(Application app) {
            bool isDark = IsDarkTheme;

            // Background colors
            app.Resources["ThemeBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(45, 45, 48) : Colors.White);
            app.Resources["ThemeBackgroundAlt"] = new SolidColorBrush(
                isDark ? Color.FromRgb(37, 37, 38) : Color.FromRgb(240, 240, 240));
            app.Resources["ThemeBackgroundHover"] = new SolidColorBrush(
                isDark ? Color.FromRgb(62, 62, 64) : Color.FromRgb(190, 230, 253));

            // Foreground colors
            app.Resources["ThemeForeground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(241, 241, 241) : Colors.Black);
            app.Resources["ThemeForegroundDim"] = new SolidColorBrush(
                isDark ? Color.FromRgb(160, 160, 160) : Color.FromRgb(96, 96, 96));

            // Border colors
            app.Resources["ThemeBorder"] = new SolidColorBrush(
                isDark ? Color.FromRgb(67, 67, 70) : Color.FromRgb(172, 172, 172));
            app.Resources["ThemeBorderHover"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0, 122, 204) : Color.FromRgb(126, 180, 234));

            // Accent colors
            app.Resources["ThemeAccent"] = new SolidColorBrush(
                Color.FromRgb(0, 120, 215));
            app.Resources["ThemeAccentForeground"] = new SolidColorBrush(Colors.White);

            // Selection colors
            app.Resources["ThemeSelection"] = new SolidColorBrush(
                isDark ? Color.FromRgb(51, 51, 52) : Color.FromRgb(0, 120, 215));
            app.Resources["ThemeSelectionForeground"] = new SolidColorBrush(
                isDark ? Colors.White : Colors.White);

            // DataGrid colors
            app.Resources["ThemeDataGridBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(37, 37, 38) : Color.FromRgb(214, 214, 214));
            app.Resources["ThemeDataGridRowBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(45, 45, 48) : Colors.White);
            app.Resources["ThemeDataGridRowAltBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(37, 37, 38) : Color.FromRgb(240, 240, 240));
            app.Resources["ThemeDataGridGridLines"] = new SolidColorBrush(
                isDark ? Color.FromRgb(67, 67, 70) : Color.FromRgb(211, 211, 211));

            // ScrollBar colors
            app.Resources["ThemeScrollBarBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(37, 37, 38) : Color.FromRgb(240, 240, 240));
            app.Resources["ThemeScrollBarThumb"] = new SolidColorBrush(
                isDark ? Color.FromRgb(104, 104, 104) : Color.FromRgb(205, 205, 205));
            app.Resources["ThemeScrollBarThumbHover"] = new SolidColorBrush(
                isDark ? Color.FromRgb(158, 158, 158) : Color.FromRgb(166, 166, 166));

            // CheckBox colors
            app.Resources["ThemeCheckBoxBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(45, 45, 48) : Colors.White);
            app.Resources["ThemeCheckBoxBorder"] = new SolidColorBrush(
                isDark ? Color.FromRgb(136, 136, 136) : Color.FromRgb(112, 112, 112));
            app.Resources["ThemeCheckBoxGlyph"] = new SolidColorBrush(
                isDark ? Color.FromRgb(241, 241, 241) : Color.FromRgb(33, 33, 33));

            // Slider track colors
            app.Resources["ThemeSliderTrack"] = new SolidColorBrush(
                isDark ? Color.FromRgb(85, 85, 85) : Color.FromRgb(200, 200, 200));

            // TextBox colors (input fields)
            app.Resources["ThemeTextBoxBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(30, 30, 30) : Colors.White);
            app.Resources["ThemeTextBoxBorder"] = new SolidColorBrush(
                isDark ? Color.FromRgb(67, 67, 70) : Color.FromRgb(172, 172, 172));

            // Material section colors (semantic coloring for texture types)
            app.Resources["ThemeMaterialORM"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x8B, 0x45, 0x65) : Color.FromRgb(255, 200, 220));  // Pink/magenta
            app.Resources["ThemeMaterialDiffuse"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x61, 0x61, 0x61) : Color.FromRgb(200, 200, 200));  // Gray
            app.Resources["ThemeMaterialNormal"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x50, 0x50, 0xA0) : Color.FromRgb(180, 180, 240));  // Blue/purple
            app.Resources["ThemeMaterialSpecular"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x8c, 0x61, 0x40) : Color.FromRgb(255, 220, 180));  // Orange/gold
            app.Resources["ThemeMaterialOther"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x50, 0x74, 0x8a) : Color.FromRgb(180, 210, 240));  // Blue-gray
            // Additional texture type colors
            app.Resources["ThemeTextureAlbedo"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x6B, 0x5A, 0x1A) : Color.FromRgb(230, 220, 150));  // Gold/olive
            app.Resources["ThemeTextureGloss"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(210, 210, 180));  // Gray/yellow
            app.Resources["ThemeTextureAO"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0x4A, 0x4A, 0x4A) : Color.FromRgb(190, 190, 190));  // Gray

            // Hyperlink colors
            app.Resources["ThemeHyperlink"] = new SolidColorBrush(
                isDark ? Color.FromRgb(0xee, 0x90, 0x00) : Color.FromRgb(0, 102, 204));

            // OxyPlot Tracker (tooltip) colors
            app.Resources["ThemeTrackerBackground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(45, 45, 48) : Color.FromRgb(255, 255, 225));
            app.Resources["ThemeTrackerForeground"] = new SolidColorBrush(
                isDark ? Color.FromRgb(241, 241, 241) : Color.FromRgb(0, 0, 0));
            app.Resources["ThemeTrackerBorder"] = new SolidColorBrush(
                isDark ? Color.FromRgb(100, 100, 100) : Color.FromRgb(180, 180, 150));
        }

        /// <summary>
        /// Gets histogram background color as RGB bytes for OxyPlot
        /// </summary>
        public static (byte R, byte G, byte B) GetHistogramBackgroundColor() {
            return IsDarkTheme
                ? ((byte)0x25, (byte)0x25, (byte)0x26) // Dark: #252526 (darker than main bg)
                : ((byte)0xF5, (byte)0xF5, (byte)0xF5); // Light: #F5F5F5 (light gray)
        }

        /// <summary>
        /// Gets histogram border color as RGB bytes for OxyPlot
        /// </summary>
        public static (byte R, byte G, byte B) GetHistogramBorderColor() {
            return IsDarkTheme
                ? ((byte)0x3F, (byte)0x3F, (byte)0x46) // Dark: #3F3F46
                : ((byte)0xD0, (byte)0xD0, (byte)0xD0); // Light: #D0D0D0
        }
    }
}

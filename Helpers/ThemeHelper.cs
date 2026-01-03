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
                isDark ? Color.FromRgb(45, 60, 45) : Color.FromRgb(232, 245, 233));
            app.Resources["ThemeMaterialDiffuse"] = new SolidColorBrush(
                isDark ? Color.FromRgb(55, 55, 55) : Color.FromRgb(177, 177, 177));
            app.Resources["ThemeMaterialNormal"] = new SolidColorBrush(
                isDark ? Color.FromRgb(50, 50, 75) : Color.FromRgb(142, 154, 255));
            app.Resources["ThemeMaterialSpecular"] = new SolidColorBrush(
                isDark ? Color.FromRgb(70, 55, 45) : Color.FromRgb(255, 198, 140));
            app.Resources["ThemeMaterialOther"] = new SolidColorBrush(
                isDark ? Color.FromRgb(50, 60, 70) : Color.FromRgb(157, 196, 232));
        }
    }
}

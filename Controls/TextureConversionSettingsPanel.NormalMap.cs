using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AssetProcessor.Helpers;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Partial class containing normal map auto-detection logic:
    /// - SetCurrentTexturePath / ClearNormalMapPath
    /// - FindNormalMapForTexture (file system search)
    /// - UpdateNormalMapAutoDetect (UI status updates)
    /// - GetToksvigSettingsWithAutoDetect
    /// </summary>
    public partial class TextureConversionSettingsPanel {

        /// <summary>
        /// Устанавливает путь текущей текстуры (для auto-detect normal map)
        /// </summary>
        public void SetCurrentTexturePath(string? texturePath) {
            _currentTexturePath = texturePath;
            UpdateNormalMapAutoDetect();
        }

        /// <summary>
        /// Очищает путь к normal map (для auto-detect при выборе новой текстуры)
        /// </summary>
        public void ClearNormalMapPath() {
            _isLoading = true;
            NormalMapPathTextBox.Text = string.Empty;
            UpdateNormalMapAutoDetect();
            _isLoading = false;
        }

        /// <summary>
        /// Пытается автоматически найти normal map для текстуры gloss
        /// </summary>
        public ToksvigSettings GetToksvigSettingsWithAutoDetect(string glossTexturePath) {
            var settings = GetToksvigSettings();

            if (!string.IsNullOrWhiteSpace(settings.NormalMapPath)) {
                return settings;
            }

            if (settings.Enabled) {
                var normalMapPath = FindNormalMapForTexture(glossTexturePath);
                if (!string.IsNullOrWhiteSpace(normalMapPath)) {
                    settings.NormalMapPath = normalMapPath;

                    _ = Dispatcher.BeginInvoke(() => {
                        NormalMapStatusTextBlock.Text = $"⚙ Auto-detected: {System.IO.Path.GetFileName(normalMapPath)}";
                        NormalMapStatusTextBlock.Foreground = GetThemeForegroundDim();
                    });
                }
            }

            return settings;
        }

        private void UpdateNormalMapAutoDetect() {
            bool toksvigEnabled = ToksvigEnabledCheckBox.IsChecked ?? false;
            bool pathEmpty = string.IsNullOrWhiteSpace(NormalMapPathTextBox.Text);

            if (toksvigEnabled && pathEmpty && !string.IsNullOrWhiteSpace(_currentTexturePath)) {
                var normalMapPath = FindNormalMapForTexture(_currentTexturePath);

                if (!string.IsNullOrWhiteSpace(normalMapPath)) {
                    _isLoading = true;
                    NormalMapPathTextBox.Text = normalMapPath;
                    _isLoading = false;

                    var fileName = System.IO.Path.GetFileName(normalMapPath);
                    NormalMapStatusTextBlock.Text = $"⚙ Auto-detected: {fileName}";
                    NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    Logger.Info($"Normal map auto-detected: {fileName}");
                    return;
                }
            }

            // Обновляем статус UI
            if (string.IsNullOrWhiteSpace(NormalMapPathTextBox.Text)) {
                NormalMapStatusTextBlock.Text = "(auto-detect from filename)";
                NormalMapStatusTextBlock.Foreground = GetThemeForegroundDim();
            } else {
                var fileName = System.IO.Path.GetFileName(NormalMapPathTextBox.Text);
                if (System.IO.File.Exists(NormalMapPathTextBox.Text)) {
                    NormalMapStatusTextBlock.Text = $"✓ Using: {fileName}";
                    NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                } else {
                    NormalMapStatusTextBlock.Text = $"⚠ Not found: {fileName}";
                    NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
                }
            }
        }

        /// <summary>
        /// Gets theme-aware dim foreground brush for secondary text
        /// </summary>
        private static System.Windows.Media.Brush GetThemeForegroundDim() {
            bool isDark = ThemeHelper.IsDarkTheme;
            return new System.Windows.Media.SolidColorBrush(
                isDark
                    ? System.Windows.Media.Color.FromRgb(160, 160, 160)
                    : System.Windows.Media.Color.FromRgb(96, 96, 96));
        }

        /// <summary>
        /// Ищет normal map по имени файла gloss текстуры
        /// </summary>
        private string? FindNormalMapForTexture(string texturePath) {
            if (string.IsNullOrWhiteSpace(texturePath)) return null;

            try {
                texturePath = PathSanitizer.SanitizePath(texturePath);

                var directory = System.IO.Path.GetDirectoryName(texturePath);
                if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory)) return null;

                var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(texturePath);

                var glossSuffixes = new[] { "_gloss", "_glossiness", "_smoothness", "_sm", "_gls" };
                string baseName = fileNameWithoutExt;
                foreach (var suffix in glossSuffixes) {
                    if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                        baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                        break;
                    }
                }

                var allFiles = System.IO.Directory.GetFiles(directory);
                var fileNameLookup = new HashSet<string>(
                    allFiles.Select(System.IO.Path.GetFileName).Where(fn => fn != null)!,
                    StringComparer.OrdinalIgnoreCase);

                var normalSuffixes = new[] { "_normal", "_norm", "_nrm", "_n", "_normals" };
                var extensions = new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff" };

                foreach (var normalSuffix in normalSuffixes) {
                    foreach (var ext in extensions) {
                        var normalFileName = baseName + normalSuffix + ext;

                        if (fileNameLookup.Contains(normalFileName)) {
                            var actualFileName = allFiles.FirstOrDefault(f =>
                                string.Equals(System.IO.Path.GetFileName(f), normalFileName, StringComparison.OrdinalIgnoreCase));
                            if (actualFileName != null) {
                                return actualFileName;
                            }
                        }
                    }
                }
            } catch {
                // Ignore auto-detect errors
            }

            return null;
        }
    }
}

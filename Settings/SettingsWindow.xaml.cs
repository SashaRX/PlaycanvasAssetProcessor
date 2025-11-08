using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.BasisU;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.ModelConversion.Settings;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace AssetProcessor {
    public partial class SettingsWindow : Window, INotifyPropertyChanged {
        private GlobalTextureConversionSettings _textureSettings;
        private GlobalModelConversionSettings _modelSettings;

        // Event for preview renderer changes
        public event Action<bool>? OnPreviewRendererChanged;

        public string? ProjectsFolder {
            get => AppSettings.Default.ProjectsFolderPath;
            set {
                if (value != null) {
                    AppSettings.Default.ProjectsFolderPath = value;
                    OnPropertyChanged(nameof(ProjectsFolder));
                }
            }
        }

        public string ToktxExecutablePath {
            get => _textureSettings.ToktxExecutablePath;
            set {
                if (_textureSettings.ToktxExecutablePath != value) {
                    _textureSettings.ToktxExecutablePath = value;
                    OnPropertyChanged(nameof(ToktxExecutablePath));
                }
            }
        }

        public string KtxExecutablePath {
            get => _textureSettings.KtxExecutablePath;
            set {
                if (_textureSettings.KtxExecutablePath != value) {
                    _textureSettings.KtxExecutablePath = value;
                    OnPropertyChanged(nameof(KtxExecutablePath));
                }
            }
        }

        public bool UseSSE41 {
            get => _textureSettings.UseSSE41;
            set {
                if (_textureSettings.UseSSE41 != value) {
                    _textureSettings.UseSSE41 = value;
                    OnPropertyChanged(nameof(UseSSE41));
                }
            }
        }

        public bool UseMultithreading {
            get => _textureSettings.UseMultithreading;
            set {
                if (_textureSettings.UseMultithreading != value) {
                    _textureSettings.UseMultithreading = value;
                    OnPropertyChanged(nameof(UseMultithreading));
                }
            }
        }

        public int ThreadCount {
            get => _textureSettings.ThreadCount;
            set {
                if (_textureSettings.ThreadCount != value) {
                    _textureSettings.ThreadCount = value;
                    OnPropertyChanged(nameof(ThreadCount));
                }
            }
        }

        public string FBX2glTFExecutablePath {
            get => _modelSettings.FBX2glTFExecutablePath;
            set {
                if (_modelSettings.FBX2glTFExecutablePath != value) {
                    _modelSettings.FBX2glTFExecutablePath = value;
                    OnPropertyChanged(nameof(FBX2glTFExecutablePath));
                }
            }
        }

        public string GltfPackExecutablePath {
            get => _modelSettings.GltfPackExecutablePath;
            set {
                if (_modelSettings.GltfPackExecutablePath != value) {
                    _modelSettings.GltfPackExecutablePath = value;
                    OnPropertyChanged(nameof(GltfPackExecutablePath));
                }
            }
        }

        public SettingsWindow() {
            _textureSettings = TextureConversionSettingsManager.LoadSettings();
            _modelSettings = ModelConversionSettingsManager.LoadSettings();
            InitializeComponent();
            DataContext = this;
            LoadSettings();
            CheckAndRemoveWatermarks();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadSettings() {
            UsernameTextBox.Text = AppSettings.Default.UserName;
            PlaycanvasApiKeyTextBox.Text = AppSettings.Default.PlaycanvasApiKey;
            ProjectsFolderBox.Text = AppSettings.Default.ProjectsFolderPath;
            GetTexturesSemaphoreSlider.Value = AppSettings.Default.GetTexturesSemaphoreLimit;
            DownloadSemaphoreSlider.Value = AppSettings.Default.DownloadSemaphoreLimit;
            GetTexturesSemaphoreTextBlock.Text = AppSettings.Default.GetTexturesSemaphoreLimit.ToString();
            DownloadSemaphoreTextBlock.Text = AppSettings.Default.DownloadSemaphoreLimit.ToString();
            ToktxExecutableBox.Text = _textureSettings.ToktxExecutablePath;
            KtxExecutableBox.Text = _textureSettings.KtxExecutablePath;

            // Load model conversion settings
            FBX2glTFExecutableBox.Text = _modelSettings.FBX2glTFExecutablePath;
            GltfPackExecutableBox.Text = _modelSettings.GltfPackExecutablePath;

            // Load D3D11 Preview checkbox
            UseD3D11PreviewCheckBox.Checked -= D3D11PreviewCheckBox_Changed;
            UseD3D11PreviewCheckBox.Unchecked -= D3D11PreviewCheckBox_Changed;

            bool useD3D11 = AppSettings.Default.UseD3D11Preview;
            UseD3D11PreviewCheckBox.IsChecked = useD3D11;
            NLog.LogManager.GetCurrentClassLogger().Info($"[Settings] LoadSettings: UseD3D11Preview = {useD3D11}");

            UseD3D11PreviewCheckBox.Checked += D3D11PreviewCheckBox_Changed;
            UseD3D11PreviewCheckBox.Unchecked += D3D11PreviewCheckBox_Changed;
        }

        private void CheckAndRemoveWatermarks() {
            RemoveWatermarkIfFilled(UsernameTextBox);
            RemoveWatermarkIfFilled(PlaycanvasApiKeyTextBox);
        }

        private static void RemoveWatermarkIfFilled(TextBox textBox) {
            if (!string.IsNullOrEmpty(textBox.Text)) {
                textBox.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void SemaphoreLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (sender == GetTexturesSemaphoreSlider && GetTexturesSemaphoreTextBlock != null) {
                GetTexturesSemaphoreTextBlock.Text = ((int)GetTexturesSemaphoreSlider.Value).ToString();
            }
            if (sender == DownloadSemaphoreSlider && DownloadSemaphoreTextBlock != null) {
                DownloadSemaphoreTextBlock.Text = ((int)DownloadSemaphoreSlider.Value).ToString();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
            // Handle text changed events if necessary
        }

        private void SelectFolder(object sender, RoutedEventArgs e) {
            VistaFolderBrowserDialog folderDialog = new() {
                Description = "Select a folder to save projects",
                UseDescriptionForTitle = true
            };

            if ((folderDialog.ShowDialog(this) ?? false)) {
                ProjectsFolder = folderDialog.SelectedPath;
                if (ProjectsFolderBox != null) {
                    ProjectsFolderBox.Text = folderDialog.SelectedPath;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            AppSettings.Default.UserName = UsernameTextBox.Text;
            AppSettings.Default.PlaycanvasApiKey = PlaycanvasApiKeyTextBox.Text;
            AppSettings.Default.GetTexturesSemaphoreLimit = (int)GetTexturesSemaphoreSlider.Value;
            AppSettings.Default.DownloadSemaphoreLimit = (int)DownloadSemaphoreSlider.Value;
            AppSettings.Default.ProjectsFolderPath = ProjectsFolderBox.Text;
            bool useD3D11 = UseD3D11PreviewCheckBox.IsChecked ?? true;
            AppSettings.Default.UseD3D11Preview = useD3D11;

            NLog.LogManager.GetCurrentClassLogger().Info($"[Settings] Save_Click: Before Save() UseD3D11Preview = {useD3D11}");
            AppSettings.Default.Save();
            NLog.LogManager.GetCurrentClassLogger().Info($"[Settings] Save_Click: After Save() UseD3D11Preview = {AppSettings.Default.UseD3D11Preview}");

            // Save texture conversion settings
            _textureSettings.ToktxExecutablePath = ToktxExecutableBox.Text;
            _textureSettings.KtxExecutablePath = KtxExecutableBox.Text;
            TextureConversionSettingsManager.SaveSettings(_textureSettings);

            // Save model conversion settings
            _modelSettings.FBX2glTFExecutablePath = FBX2glTFExecutableBox.Text;
            _modelSettings.GltfPackExecutablePath = GltfPackExecutableBox.Text;
            ModelConversionSettingsManager.SaveSettings(_modelSettings);

            this.Close();
        }

        private void SelectToktxExecutable(object sender, RoutedEventArgs e) {
            OpenFileDialog fileDialog = new() {
                Title = "Select toktx executable",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (fileDialog.ShowDialog() == true) {
                ToktxExecutablePath = fileDialog.FileName;
                ToktxExecutableBox.Text = fileDialog.FileName;
            }
        }

        private async void TestToktx_Click(object sender, RoutedEventArgs e) {
            if (ToktxStatusText != null) {
                ToktxStatusText.Text = "Testing...";
                ToktxStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            try {
                var path = string.IsNullOrWhiteSpace(ToktxExecutableBox.Text) ? "toktx" : ToktxExecutableBox.Text;

                // Получаем версию toktx
                ProcessStartInfo startInfo = new() {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    if (ToktxStatusText != null) {
                        ToktxStatusText.Text = "✗ toktx not found";
                        ToktxStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    return;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Логируем для отладки
                System.Diagnostics.Debug.WriteLine($"toktx --version ExitCode: {process.ExitCode}");
                System.Diagnostics.Debug.WriteLine($"toktx --version stdout: '{stdout}'");
                System.Diagnostics.Debug.WriteLine($"toktx --version stderr: '{stderr}'");

                if (ToktxStatusText != null) {
                    // toktx --version выводит версию в stdout
                    bool hasOutput = !string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr);
                    if (hasOutput || process.ExitCode == 0) {
                        // Извлекаем версию из stdout (формат: "toktx v4.0" или "toktx v4.3.2" или "toktx v4.4.1~5")
                        string version = "unknown";
                        string output = stdout + stderr; // Проверяем оба потока
                        if (!string.IsNullOrWhiteSpace(output)) {
                            // Ищем версию: v4.4.1~5 или v4.3.2 или просто 4.0
                            var match = System.Text.RegularExpressions.Regex.Match(output, @"(v\d+\.\d+(?:\.\d+)?(?:~\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success) {
                                version = match.Groups[1].Value.Trim();
                            }
                        }
                        ToktxStatusText.Text = $"✓ toktx {version}";
                        ToktxStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    } else {
                        ToktxStatusText.Text = $"✗ Exit code: {process.ExitCode}";
                        ToktxStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
            } catch (Exception ex) {
                if (ToktxStatusText != null) {
                    ToktxStatusText.Text = $"✗ Error: {ex.Message}";
                    ToktxStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        private void SelectKtxExecutable(object sender, RoutedEventArgs e) {
            OpenFileDialog fileDialog = new() {
                Title = "Select ktx executable",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (fileDialog.ShowDialog() == true) {
                KtxExecutablePath = fileDialog.FileName;
                KtxExecutableBox.Text = fileDialog.FileName;
            }
        }

        private async void TestKtx_Click(object sender, RoutedEventArgs e) {
            if (KtxStatusText != null) {
                KtxStatusText.Text = "Testing...";
                KtxStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            try {
                var path = string.IsNullOrWhiteSpace(KtxExecutableBox.Text) ? "ktx" : KtxExecutableBox.Text;

                // Получаем версию ktx
                ProcessStartInfo startInfo = new() {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    if (KtxStatusText != null) {
                        KtxStatusText.Text = "✗ ktx not found";
                        KtxStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    return;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (KtxStatusText != null) {
                    // ktx --version выводит версию в stdout
                    bool hasOutput = !string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr);
                    if (hasOutput || process.ExitCode == 0) {
                        // Извлекаем версию из stdout (формат: "ktx version: v4.0" или "ktx version: v4.3.2")
                        string version = "unknown";
                        if (!string.IsNullOrWhiteSpace(stdout)) {
                            var match = System.Text.RegularExpressions.Regex.Match(stdout, @"version:\s*(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success) {
                                version = match.Groups[1].Value.Trim();
                            }
                        }
                        KtxStatusText.Text = $"✓ ktx {version}";
                        KtxStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    } else {
                        KtxStatusText.Text = $"✗ Exit code: {process.ExitCode}";
                        KtxStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
            } catch (Exception ex) {
                if (KtxStatusText != null) {
                    KtxStatusText.Text = $"✗ Error: {ex.Message}";
                    KtxStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        private void D3D11PreviewCheckBox_Changed(object sender, RoutedEventArgs e) {
            // Apply preview renderer change immediately (real-time)
            bool useD3D11 = UseD3D11PreviewCheckBox.IsChecked ?? true;
            AppSettings.Default.UseD3D11Preview = useD3D11;
            AppSettings.Default.Save();
            NLog.LogManager.GetCurrentClassLogger().Info($"[Settings] D3D11PreviewCheckBox_Changed: saved UseD3D11Preview = {useD3D11}");

            // Notify MainWindow about the change
            OnPreviewRendererChanged?.Invoke(useD3D11);
        }

        private void SelectFBX2glTFExecutable(object sender, RoutedEventArgs e) {
            OpenFileDialog fileDialog = new() {
                Title = "Select FBX2glTF executable",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (fileDialog.ShowDialog() == true) {
                FBX2glTFExecutablePath = fileDialog.FileName;
                FBX2glTFExecutableBox.Text = fileDialog.FileName;
            }
        }

        private async void TestFBX2glTF_Click(object sender, RoutedEventArgs e) {
            if (FBX2glTFStatusText != null) {
                FBX2glTFStatusText.Text = "Testing...";
                FBX2glTFStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            try {
                var path = string.IsNullOrWhiteSpace(FBX2glTFExecutableBox.Text) ? "FBX2glTF-windows-x64.exe" : FBX2glTFExecutableBox.Text;

                ProcessStartInfo startInfo = new() {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    if (FBX2glTFStatusText != null) {
                        FBX2glTFStatusText.Text = "✗ FBX2glTF not found";
                        FBX2glTFStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    return;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (FBX2glTFStatusText != null) {
                    bool hasOutput = !string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr);
                    if (hasOutput || process.ExitCode == 0) {
                        string version = "unknown";
                        string output = stdout + stderr;
                        if (!string.IsNullOrWhiteSpace(output)) {
                            var match = System.Text.RegularExpressions.Regex.Match(output, @"version[:\s]+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success) {
                                version = match.Groups[1].Value.Trim();
                            }
                        }
                        FBX2glTFStatusText.Text = $"✓ FBX2glTF {version}";
                        FBX2glTFStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    } else {
                        FBX2glTFStatusText.Text = $"✗ Exit code: {process.ExitCode}";
                        FBX2glTFStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
            } catch (Exception ex) {
                if (FBX2glTFStatusText != null) {
                    FBX2glTFStatusText.Text = $"✗ Error: {ex.Message}";
                    FBX2glTFStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        private void SelectGltfPackExecutable(object sender, RoutedEventArgs e) {
            OpenFileDialog fileDialog = new() {
                Title = "Select gltfpack executable",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (fileDialog.ShowDialog() == true) {
                GltfPackExecutablePath = fileDialog.FileName;
                GltfPackExecutableBox.Text = fileDialog.FileName;
            }
        }

        private async void TestGltfPack_Click(object sender, RoutedEventArgs e) {
            if (GltfPackStatusText != null) {
                GltfPackStatusText.Text = "Testing...";
                GltfPackStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            try {
                var path = string.IsNullOrWhiteSpace(GltfPackExecutableBox.Text) ? "gltfpack.exe" : GltfPackExecutableBox.Text;

                ProcessStartInfo startInfo = new() {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    if (GltfPackStatusText != null) {
                        GltfPackStatusText.Text = "✗ gltfpack not found";
                        GltfPackStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    return;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (GltfPackStatusText != null) {
                    bool hasOutput = !string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr);
                    if (hasOutput || process.ExitCode == 0) {
                        string version = "unknown";
                        string output = stdout + stderr;
                        if (!string.IsNullOrWhiteSpace(output)) {
                            var match = System.Text.RegularExpressions.Regex.Match(output, @"v(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success) {
                                version = match.Groups[1].Value.Trim();
                            }
                        }
                        GltfPackStatusText.Text = $"✓ gltfpack {version}";
                        GltfPackStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    } else {
                        GltfPackStatusText.Text = $"✗ Exit code: {process.ExitCode}";
                        GltfPackStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
            } catch (Exception ex) {
                if (GltfPackStatusText != null) {
                    GltfPackStatusText.Text = $"✗ Error: {ex.Message}";
                    GltfPackStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}
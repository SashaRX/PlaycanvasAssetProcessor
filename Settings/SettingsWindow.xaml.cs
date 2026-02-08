using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.BasisU;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.ModelConversion.Settings;
using AssetProcessor.Upload;
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
        private string? _playcanvasApiKey;
        private bool _isApiKeyVisible;
        private bool _suppressApiKeyUpdates;

        // B2 settings
        private string? _b2ApplicationKey;
        private bool _isB2KeyVisible;
        private bool _suppressB2KeyUpdates;

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

            if (!AppSettings.Default.TryGetDecryptedPlaycanvasApiKey(out _playcanvasApiKey)) {
                _playcanvasApiKey = null;
                MessageBox.Show(
                    "Не удалось расшифровать сохранённый API-ключ. Проверьте мастер-пароль или удалите ключ и сохраните заново.",
                    "Ошибка безопасности",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            UpdateApiKeyControls();

            ProjectsFolderBox.Text = AppSettings.Default.ProjectsFolderPath;
            GetTexturesSemaphoreSlider.Value = AppSettings.Default.GetTexturesSemaphoreLimit;
            DownloadSemaphoreSlider.Value = AppSettings.Default.DownloadSemaphoreLimit;
            GetTexturesSemaphoreTextBlock.Text = AppSettings.Default.GetTexturesSemaphoreLimit.ToString();
            DownloadSemaphoreTextBlock.Text = AppSettings.Default.DownloadSemaphoreLimit.ToString();
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

            // Load B2/CDN settings
            LoadB2Settings();
        }

        private void LoadB2Settings() {
            B2KeyIdTextBox.Text = AppSettings.Default.B2KeyId;
            B2BucketNameTextBox.Text = AppSettings.Default.B2BucketName;
            B2BucketIdTextBox.Text = AppSettings.Default.B2BucketId;
            CdnBaseUrlTextBox.Text = AppSettings.Default.CdnBaseUrl;
            B2PathPrefixTextBox.Text = AppSettings.Default.B2PathPrefix;
            B2MaxConcurrentUploadsSlider.Value = AppSettings.Default.B2MaxConcurrentUploads;
            B2MaxConcurrentUploadsText.Text = AppSettings.Default.B2MaxConcurrentUploads.ToString();
            B2AutoUploadMappingCheckBox.IsChecked = AppSettings.Default.B2AutoUploadMapping;

            if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out _b2ApplicationKey)) {
                _b2ApplicationKey = null;
            }

            UpdateB2KeyControls();
        }

        private void CheckAndRemoveWatermarks() {
            RemoveWatermarkIfFilled(UsernameTextBox);
        }

        private static void RemoveWatermarkIfFilled(TextBox textBox) {
            if (!string.IsNullOrEmpty(textBox.Text)) {
                textBox.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void PlaycanvasApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) {
            if (_suppressApiKeyUpdates || _isApiKeyVisible) {
                return;
            }

            _playcanvasApiKey = PlaycanvasApiKeyPasswordBox.Password;
            SyncApiKeyControls();
        }

        private void PlaycanvasApiKeyRevealTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (_suppressApiKeyUpdates || !_isApiKeyVisible) {
                return;
            }

            _playcanvasApiKey = PlaycanvasApiKeyRevealTextBox.Text;
            SyncApiKeyControls();
        }

        private void ToggleApiKeyVisibilityButton_Click(object sender, RoutedEventArgs e) {
            if (!_isApiKeyVisible) {
                MessageBoxResult result = MessageBox.Show(
                    "Показать API-ключ в открытом виде? Убедитесь, что рядом нет посторонних.",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) {
                    return;
                }
                _isApiKeyVisible = true;
            } else {
                _isApiKeyVisible = false;
            }

            UpdateApiKeyControls();
        }

        private void UpdateApiKeyControls() {
            _suppressApiKeyUpdates = true;

            string currentValue = _playcanvasApiKey ?? string.Empty;
            PlaycanvasApiKeyPasswordBox.Password = currentValue;
            PlaycanvasApiKeyRevealTextBox.Text = currentValue;

            PlaycanvasApiKeyPasswordBox.Visibility = _isApiKeyVisible ? Visibility.Collapsed : Visibility.Visible;
            PlaycanvasApiKeyRevealTextBox.Visibility = _isApiKeyVisible ? Visibility.Visible : Visibility.Collapsed;
            ToggleApiKeyVisibilityButton.Content = _isApiKeyVisible ? "Скрыть" : "Показать";
            ApiKeyVisibilityWarningText.Text = _isApiKeyVisible
                ? "API-ключ показан. Скрывайте значение после проверки."
                : "Значение ключа скрыто. Нажмите «Показать» для временного просмотра.";

            _suppressApiKeyUpdates = false;
        }

        private void SyncApiKeyControls() {
            _suppressApiKeyUpdates = true;
            string currentValue = _playcanvasApiKey ?? string.Empty;
            if (_isApiKeyVisible) {
                PlaycanvasApiKeyRevealTextBox.Text = currentValue;
            } else {
                PlaycanvasApiKeyPasswordBox.Password = currentValue;
            }
            _suppressApiKeyUpdates = false;
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
            try {
                AppSettings.Default.PlaycanvasApiKey = _playcanvasApiKey ?? string.Empty;
            } catch (InvalidOperationException ex) {
                MessageBox.Show(ex.Message, "Ошибка сохранения ключа", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AppSettings.Default.GetTexturesSemaphoreLimit = (int)GetTexturesSemaphoreSlider.Value;
            AppSettings.Default.DownloadSemaphoreLimit = (int)DownloadSemaphoreSlider.Value;
            AppSettings.Default.ProjectsFolderPath = ProjectsFolderBox.Text;
            bool useD3D11 = UseD3D11PreviewCheckBox.IsChecked ?? true;
            AppSettings.Default.UseD3D11Preview = useD3D11;

            NLog.LogManager.GetCurrentClassLogger().Info($"[Settings] Save_Click: Before Save() UseD3D11Preview = {useD3D11}");
            AppSettings.Default.Save();
            NLog.LogManager.GetCurrentClassLogger().Info($"[Settings] Save_Click: After Save() UseD3D11Preview = {AppSettings.Default.UseD3D11Preview}");

            // Save texture conversion settings
            _textureSettings.KtxExecutablePath = KtxExecutableBox.Text;
            TextureConversionSettingsManager.SaveSettings(_textureSettings);

            // Save model conversion settings
            _modelSettings.FBX2glTFExecutablePath = FBX2glTFExecutableBox.Text;
            _modelSettings.GltfPackExecutablePath = GltfPackExecutableBox.Text;
            ModelConversionSettingsManager.SaveSettings(_modelSettings);

            // Save B2/CDN settings
            SaveB2Settings();

            this.Close();
        }

        private void SaveB2Settings() {
            AppSettings.Default.B2KeyId = B2KeyIdTextBox.Text;
            try {
                AppSettings.Default.B2ApplicationKey = _b2ApplicationKey ?? string.Empty;
            } catch (InvalidOperationException ex) {
                MessageBox.Show(ex.Message, "Error saving B2 key", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            AppSettings.Default.B2BucketName = B2BucketNameTextBox.Text;
            AppSettings.Default.B2BucketId = B2BucketIdTextBox.Text;
            AppSettings.Default.CdnBaseUrl = CdnBaseUrlTextBox.Text;
            AppSettings.Default.B2PathPrefix = B2PathPrefixTextBox.Text;
            AppSettings.Default.B2MaxConcurrentUploads = (int)B2MaxConcurrentUploadsSlider.Value;
            AppSettings.Default.B2AutoUploadMapping = B2AutoUploadMappingCheckBox.IsChecked ?? false;
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
            var path = string.IsNullOrWhiteSpace(KtxExecutableBox.Text) ? "ktx" : KtxExecutableBox.Text;

            await RunToolVersionCheckAsync(
                KtxStatusText,
                path,
                "ktx not found",
                stdout => {
                    var version = ExtractMatch(stdout, @"version:\s*(.+)");
                    return $"✓ ktx {version}";
                });
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
            var path = string.IsNullOrWhiteSpace(FBX2glTFExecutableBox.Text) ? "FBX2glTF-windows-x86_64.exe" : FBX2glTFExecutableBox.Text;

            await RunToolVersionCheckAsync(
                FBX2glTFStatusText,
                path,
                "FBX2glTF not found",
                output => {
                    var version = ExtractMatch(output, @"version[:\s]+(\S+)");
                    return $"✓ FBX2glTF {version}";
                });
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
            var path = string.IsNullOrWhiteSpace(GltfPackExecutableBox.Text) ? "gltfpack.exe" : GltfPackExecutableBox.Text;

            await RunToolVersionCheckAsync(
                GltfPackStatusText,
                path,
                "gltfpack not found",
                output => {
                    var version = ExtractMatch(output, @"v(\S+)");
                    return $"✓ gltfpack {version}";
                });
        }

        private async Task RunToolVersionCheckAsync(
            TextBlock? statusText,
            string executablePath,
            string notFoundMessage,
            Func<string, string> successTextFactory) {

            if (statusText != null) {
                statusText.Text = "Testing...";
                statusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            try {
                ProcessStartInfo startInfo = new() {
                    FileName = executablePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    SetStatus(statusText, $"✗ {notFoundMessage}", Colors.Red);
                    return;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                bool hasOutput = !string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr);
                if (hasOutput || process.ExitCode == 0) {
                    SetStatus(statusText, successTextFactory(stdout + stderr), Colors.Green);
                } else {
                    SetStatus(statusText, $"✗ Exit code: {process.ExitCode}", Colors.Red);
                }
            } catch (Exception ex) {
                SetStatus(statusText, $"✗ Error: {ex.Message}", Colors.Red);
            }
        }

        private static string ExtractMatch(string input, string pattern) {
            if (!string.IsNullOrWhiteSpace(input)) {
                var match = System.Text.RegularExpressions.Regex.Match(
                    input,
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) {
                    return match.Groups[1].Value.Trim();
                }
            }

            return "unknown";
        }

        private static void SetStatus(TextBlock? statusText, string message, Color color) {
            if (statusText != null) {
                statusText.Text = message;
                statusText.Foreground = new SolidColorBrush(color);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        #region B2/CDN Settings Event Handlers

        private void B2ApplicationKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) {
            if (_suppressB2KeyUpdates || _isB2KeyVisible) {
                return;
            }
            _b2ApplicationKey = B2ApplicationKeyPasswordBox.Password;
            SyncB2KeyControls();
        }

        private void B2ApplicationKeyRevealTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (_suppressB2KeyUpdates || !_isB2KeyVisible) {
                return;
            }
            _b2ApplicationKey = B2ApplicationKeyRevealTextBox.Text;
            SyncB2KeyControls();
        }

        private void ToggleB2KeyVisibilityButton_Click(object sender, RoutedEventArgs e) {
            if (!_isB2KeyVisible) {
                MessageBoxResult result = MessageBox.Show(
                    "Show Application Key in plain text?",
                    "Confirm",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) {
                    return;
                }
                _isB2KeyVisible = true;
            } else {
                _isB2KeyVisible = false;
            }

            UpdateB2KeyControls();
        }

        private void UpdateB2KeyControls() {
            _suppressB2KeyUpdates = true;

            string currentValue = _b2ApplicationKey ?? string.Empty;
            B2ApplicationKeyPasswordBox.Password = currentValue;
            B2ApplicationKeyRevealTextBox.Text = currentValue;

            B2ApplicationKeyPasswordBox.Visibility = _isB2KeyVisible ? Visibility.Collapsed : Visibility.Visible;
            B2ApplicationKeyRevealTextBox.Visibility = _isB2KeyVisible ? Visibility.Visible : Visibility.Collapsed;
            ToggleB2KeyVisibilityButton.Content = _isB2KeyVisible ? "Hide" : "Show";

            _suppressB2KeyUpdates = false;
        }

        private void SyncB2KeyControls() {
            _suppressB2KeyUpdates = true;
            string currentValue = _b2ApplicationKey ?? string.Empty;
            if (_isB2KeyVisible) {
                B2ApplicationKeyRevealTextBox.Text = currentValue;
            } else {
                B2ApplicationKeyPasswordBox.Password = currentValue;
            }
            _suppressB2KeyUpdates = false;
        }

        private void B2MaxConcurrentUploadsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (B2MaxConcurrentUploadsText != null) {
                B2MaxConcurrentUploadsText.Text = ((int)B2MaxConcurrentUploadsSlider.Value).ToString();
            }
        }

        private async void TestB2Connection_Click(object sender, RoutedEventArgs e) {
            if (B2ConnectionStatusText != null) {
                B2ConnectionStatusText.Text = "Testing connection...";
                B2ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            TestB2ConnectionButton.IsEnabled = false;

            try {
                var keyId = B2KeyIdTextBox.Text;
                var applicationKey = _b2ApplicationKey;
                var bucketName = B2BucketNameTextBox.Text;

                if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(applicationKey) || string.IsNullOrWhiteSpace(bucketName)) {
                    if (B2ConnectionStatusText != null) {
                        B2ConnectionStatusText.Text = "✗ Please fill in Key ID, Application Key, and Bucket Name";
                        B2ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    return;
                }

                var settings = new B2UploadSettings {
                    KeyId = keyId,
                    ApplicationKey = applicationKey,
                    BucketName = bucketName,
                    BucketId = B2BucketIdTextBox.Text
                };

                using var uploadService = new B2UploadService();
                bool success = await uploadService.AuthorizeAsync(settings);

                if (B2ConnectionStatusText != null) {
                    if (success) {
                        // Update bucket ID if it was auto-detected
                        if (!string.IsNullOrEmpty(settings.BucketId) && string.IsNullOrEmpty(B2BucketIdTextBox.Text)) {
                            B2BucketIdTextBox.Text = settings.BucketId;
                        }

                        B2ConnectionStatusText.Text = $"✓ Connected to bucket: {bucketName}";
                        B2ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    } else {
                        B2ConnectionStatusText.Text = "✗ Connection failed. Check credentials.";
                        B2ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }

            } catch (Exception ex) {
                if (B2ConnectionStatusText != null) {
                    B2ConnectionStatusText.Text = $"✗ Error: {ex.Message}";
                    B2ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
            } finally {
                TestB2ConnectionButton.IsEnabled = true;
            }
        }

        #endregion
    }
}

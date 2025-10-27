using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.BasisU;
using AssetProcessor.TextureConversion.Settings;
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

        public string? ProjectsFolder {
            get => AppSettings.Default.ProjectsFolderPath;
            set {
                if (value != null) {
                    AppSettings.Default.ProjectsFolderPath = value;
                    OnPropertyChanged(nameof(ProjectsFolder));
                }
            }
        }

        public string BasisUExecutablePath {
            get => _textureSettings.BasisUExecutablePath;
            set {
                if (_textureSettings.BasisUExecutablePath != value) {
                    _textureSettings.BasisUExecutablePath = value;
                    OnPropertyChanged(nameof(BasisUExecutablePath));
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

        public bool UseOpenCL {
            get => _textureSettings.UseOpenCL;
            set {
                if (_textureSettings.UseOpenCL != value) {
                    _textureSettings.UseOpenCL = value;
                    OnPropertyChanged(nameof(UseOpenCL));
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

        public SettingsWindow() {
            _textureSettings = TextureConversionSettingsManager.LoadSettings();
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
            BasisUExecutableBox.Text = _textureSettings.BasisUExecutablePath;
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

            AppSettings.Default.Save();

            // Save texture conversion settings
            _textureSettings.BasisUExecutablePath = BasisUExecutableBox.Text;
            TextureConversionSettingsManager.SaveSettings(_textureSettings);

            this.Close();
        }

        private void SelectBasisUExecutable(object sender, RoutedEventArgs e) {
            OpenFileDialog fileDialog = new() {
                Title = "Select basisu executable",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (fileDialog.ShowDialog() == true) {
                BasisUExecutablePath = fileDialog.FileName;
                BasisUExecutableBox.Text = fileDialog.FileName;
            }
        }

        private async void TestBasisU_Click(object sender, RoutedEventArgs e) {
            if (BasisUStatusText != null) {
                BasisUStatusText.Text = "Testing...";
                BasisUStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            try {
                var path = string.IsNullOrWhiteSpace(BasisUExecutableBox.Text) ? "basisu" : BasisUExecutableBox.Text;
                var wrapper = new BasisUWrapper(path);
                var available = await wrapper.IsAvailableAsync();

                if (BasisUStatusText != null) {
                    if (available) {
                        BasisUStatusText.Text = "✓ basisu is available and working!";
                        BasisUStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    } else {
                        BasisUStatusText.Text = "✗ basisu not found or not working";
                        BasisUStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
            } catch (Exception ex) {
                if (BasisUStatusText != null) {
                    BasisUStatusText.Text = $"✗ Error: {ex.Message}";
                    BasisUStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}
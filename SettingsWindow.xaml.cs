using Ookii.Dialogs.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using TexTool.Properties;

namespace TexTool {
    public partial class SettingsWindow : Window, INotifyPropertyChanged {
        public string? ProjectsFolder {
            get => Settings.Default.ProjectsFolderPath;
            set {
                Settings.Default.ProjectsFolderPath = value;
                OnPropertyChanged(nameof(ProjectsFolder));
            }
        }

        public SettingsWindow() {
            InitializeComponent();
            DataContext = this; // Устанавливаем DataContext
            LoadSettings();
            CheckAndRemoveWatermarks();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadSettings() {
            UsernameTextBox.Text = Settings.Default.Username;
            PlaycanvasApiKeyTextBox.Text = Settings.Default.PlaycanvasApiKey;
            ProjectsFolderBox.Text = Settings.Default.ProjectsFolderPath; // Обновляем TextBox напрямую
            SemaphoreLimitSlider.Value = Settings.Default.SemaphoreLimit;
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
            if (SemaphoreLimitTextBlock != null) {
                SemaphoreLimitTextBlock.Text = SemaphoreLimitSlider.Value.ToString();
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
            var folderDialog = new VistaFolderBrowserDialog {
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
            Settings.Default.Username = UsernameTextBox.Text;
            Settings.Default.PlaycanvasApiKey = PlaycanvasApiKeyTextBox.Text;
            Settings.Default.SemaphoreLimit = (int)SemaphoreLimitSlider.Value;
            Settings.Default.ProjectsFolderPath = ProjectsFolderBox.Text;

            Settings.Default.Save();
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}

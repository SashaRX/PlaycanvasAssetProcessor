using System.Windows;
using System.Windows.Controls;

namespace TexTool {
    public partial class SettingsWindow : Window {
        public SettingsWindow() {
            InitializeComponent();
            LoadSettings();
            // Проверяем и убираем ватермарки для заполненных полей
            CheckAndRemoveWatermarks();
        }

        private void LoadSettings() {
            ProjectIdTextBox.Text = Settings.Default.ProjectId;
            BranchIdTextBox.Text = Settings.Default.BranchId;
            PlaycanvasApiKeyTextBox.Text = Settings.Default.PlaycanvasApiKey;
            BaseUrlTextBox.Text = Settings.Default.BaseUrl;
            SemaphoreLimitTextBox.Text = Settings.Default.SemaphoreLimit.ToString();
        }

        private void CheckAndRemoveWatermarks() {
            // Убираем ватермарки для заполненных полей
            RemoveWatermarkIfFilled(ProjectIdTextBox);
            RemoveWatermarkIfFilled(BranchIdTextBox);
            RemoveWatermarkIfFilled(PlaycanvasApiKeyTextBox);
            RemoveWatermarkIfFilled(BaseUrlTextBox);
            RemoveWatermarkIfFilled(SemaphoreLimitTextBox);
        }

        private void RemoveWatermarkIfFilled(TextBox textBox) {
            if (!string.IsNullOrEmpty(textBox.Text)) {
                textBox.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            Settings.Default.ProjectId = ProjectIdTextBox.Text;
            Settings.Default.BranchId = BranchIdTextBox.Text;
            Settings.Default.PlaycanvasApiKey = PlaycanvasApiKeyTextBox.Text;
            Settings.Default.BaseUrl = BaseUrlTextBox.Text;

            if (int.TryParse(SemaphoreLimitTextBox.Text, out int semaphoreLimit)) {
                Settings.Default.SemaphoreLimit = semaphoreLimit;
            }

            Settings.Default.Save();
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}

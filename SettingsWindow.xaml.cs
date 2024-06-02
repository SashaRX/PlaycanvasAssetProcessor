using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace TexTool {
    public partial class SettingsWindow : Window {
        public SettingsWindow() {
            InitializeComponent();
            LoadSettings();
            CheckAndRemoveWatermarks();
        }

        private void LoadSettings() {
            ProjectIdTextBox.Text = Settings.Default.ProjectId;
            BranchIdTextBox.Text = Settings.Default.BranchId;
            PlaycanvasApiKeyTextBox.Text = Settings.Default.PlaycanvasApiKey;
            BaseUrlTextBox.Text = Settings.Default.BaseUrl;
            SemaphoreLimitSlider.Value = Settings.Default.SemaphoreLimit;
        }

        private void CheckAndRemoveWatermarks() {
            RemoveWatermarkIfFilled(ProjectIdTextBox);
            RemoveWatermarkIfFilled(BranchIdTextBox);
            RemoveWatermarkIfFilled(PlaycanvasApiKeyTextBox);
            RemoveWatermarkIfFilled(BaseUrlTextBox);
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
            Settings.Default.SemaphoreLimit = (int)SemaphoreLimitSlider.Value;

            Settings.Default.Save();
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
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
    }
}

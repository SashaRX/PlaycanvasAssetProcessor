using System.Windows;

namespace TexTool {
    public partial class SettingsWindow : Window {
        public SettingsWindow() {
            InitializeComponent();

            ProjectIdTextBox.Text = Settings.Default.ProjectId;
            BranchIdTextBox.Text = Settings.Default.BranchId;
            PlaycanvasApiKeyTextBox.Text = Settings.Default.PlaycanvasApiKey;
            BaseUrlTextBox.Text = Settings.Default.BaseUrl;
            SemaphoreLimitTextBox.Text = Settings.Default.SemaphoreLimit.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            Settings.Default.ProjectId = ProjectIdTextBox.Text;
            Settings.Default.BranchId = BranchIdTextBox.Text;
            Settings.Default.PlaycanvasApiKey = PlaycanvasApiKeyTextBox.Text;
            Settings.Default.BaseUrl = BaseUrlTextBox.Text;
            Settings.Default.SemaphoreLimit = int.Parse(SemaphoreLimitTextBox.Text);

            Settings.Default.Save();

            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}

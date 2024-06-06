﻿using System.Diagnostics;
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
        public event EventHandler? SettingsSaved; // Объявляем событие как nullable

        private void LoadSettings() {
            UsernameTextBox.Text = Settings.Default.Username;
            //BranchIdTextBox.Text = Settings.Default.BranchId;
            PlaycanvasApiKeyTextBox.Text = Settings.Default.PlaycanvasApiKey;
            //BaseUrlTextBox.Text = Settings.Default.BaseUrl;
            SemaphoreLimitSlider.Value = Settings.Default.SemaphoreLimit;
        }

        private void CheckAndRemoveWatermarks() {
            RemoveWatermarkIfFilled(UsernameTextBox);
            //RemoveWatermarkIfFilled(BranchIdTextBox);
            RemoveWatermarkIfFilled(PlaycanvasApiKeyTextBox);
            //RemoveWatermarkIfFilled(BaseUrlTextBox);
        }

        private void RemoveWatermarkIfFilled(TextBox textBox) {
            if (!string.IsNullOrEmpty(textBox.Text)) {
                textBox.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            Settings.Default.Username = UsernameTextBox.Text;
            //Settings.Default.BranchId = BranchIdTextBox.Text;
            Settings.Default.PlaycanvasApiKey = PlaycanvasApiKeyTextBox.Text;
            //Settings.Default.BaseUrl = BaseUrlTextBox.Text;
            Settings.Default.SemaphoreLimit = (int)SemaphoreLimitSlider.Value;

            Settings.Default.Save();
            SettingsSaved?.Invoke(this, EventArgs.Empty);
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

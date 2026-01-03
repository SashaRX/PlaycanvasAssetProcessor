using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.Windows {
    public partial class PresetManagementWindow : Window {
        private readonly PresetManager _texturePresetManager;
        private readonly ORMPresetManager _ormPresetManager;

        // Tab indices
        private const int TextureTabIndex = 0;
        private const int ORMTabIndex = 1;

        public PresetManagementWindow(PresetManager texturePresetManager, int initialTab = 0) {
            InitializeComponent();
            _texturePresetManager = texturePresetManager;
            _ormPresetManager = ORMPresetManager.Instance;

            LoadTexturePresets();
            LoadORMPresets();

            // Select initial tab
            PresetTabControl.SelectedIndex = initialTab;
        }

        #region Tab Control

        private void PresetTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Handled by individual tab selection handlers
        }

        #endregion

        #region Texture Presets

        private void LoadTexturePresets() {
            TexturePresetsListBox.ItemsSource = null;
            TexturePresetsListBox.ItemsSource = _texturePresetManager.GetAllPresets();
        }

        private void TexturePresetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            bool hasSelection = TexturePresetsListBox.SelectedItem != null;
            EditTextureButton.IsEnabled = hasSelection;
            DeleteTextureButton.IsEnabled = hasSelection;
        }

        private void NewTexturePreset_Click(object sender, RoutedEventArgs e) {
            var editorWindow = new PresetEditorWindow(null);
            if (editorWindow.ShowDialog() == true && editorWindow.EditedPreset != null) {
                try {
                    _texturePresetManager.AddPreset(editorWindow.EditedPreset);
                    LoadTexturePresets();
                    MessageBox.Show($"Preset '{editorWindow.EditedPreset.Name}' created!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error creating preset: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EditTexturePreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = TexturePresetsListBox.SelectedItem as TextureConversionPreset;
            if (selectedPreset == null) return;

            if (selectedPreset.IsBuiltIn) {
                var result = MessageBox.Show(
                    "Editing a built-in preset will create a customized version.\nContinue?",
                    "Edit Built-in Preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            var editorWindow = new PresetEditorWindow(selectedPreset);
            if (editorWindow.ShowDialog() == true && editorWindow.EditedPreset != null) {
                try {
                    _texturePresetManager.UpdatePreset(selectedPreset.Name, editorWindow.EditedPreset);
                    LoadTexturePresets();
                    MessageBox.Show($"Preset '{editorWindow.EditedPreset.Name}' updated!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error updating preset: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteTexturePreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = TexturePresetsListBox.SelectedItem as TextureConversionPreset;
            if (selectedPreset == null) return;

            string message = selectedPreset.IsBuiltIn
                ? $"Hide built-in preset '{selectedPreset.Name}'?\nYou can restore it using 'Reset to Defaults'."
                : $"Delete preset '{selectedPreset.Name}'?";

            if (MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                try {
                    _texturePresetManager.DeletePreset(selectedPreset.Name);
                    LoadTexturePresets();
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportTexturePresets_Click(object sender, RoutedEventArgs e) {
            var dialog = new OpenFileDialog {
                Filter = "JSON files (*.json)|*.json",
                Title = "Import Texture Presets"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    int count = _texturePresetManager.ImportPresets(dialog.FileName);
                    LoadTexturePresets();
                    MessageBox.Show($"Imported {count} preset(s)!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportTexturePresets_Click(object sender, RoutedEventArgs e) {
            var dialog = new SaveFileDialog {
                Filter = "JSON files (*.json)|*.json",
                FileName = "texture_presets.json",
                Title = "Export Texture Presets"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    _texturePresetManager.ExportPresets(dialog.FileName);
                    MessageBox.Show("Presets exported!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region ORM Presets

        private void LoadORMPresets() {
            ORMPresetsListBox.ItemsSource = null;
            ORMPresetsListBox.ItemsSource = _ormPresetManager.GetAllPresets();
        }

        private void ORMPresetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            bool hasSelection = ORMPresetsListBox.SelectedItem != null;
            EditORMButton.IsEnabled = hasSelection;
            DuplicateORMButton.IsEnabled = hasSelection;
            DeleteORMButton.IsEnabled = hasSelection;
        }

        private void NewORMPreset_Click(object sender, RoutedEventArgs e) {
            var inputDialog = new InputDialog("New ORM Preset", "Enter preset name:", "My Preset");
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.ResponseText)) {
                try {
                    var newPreset = ORMSettings.CreateStandard();
                    newPreset.Name = inputDialog.ResponseText;
                    newPreset.IsBuiltIn = false;
                    newPreset.Description = "Custom ORM preset";

                    _ormPresetManager.AddPreset(newPreset);
                    LoadORMPresets();

                    // Select and edit the new preset
                    for (int i = 0; i < ORMPresetsListBox.Items.Count; i++) {
                        if (ORMPresetsListBox.Items[i] is ORMSettings preset && preset.Name == newPreset.Name) {
                            ORMPresetsListBox.SelectedIndex = i;
                            EditORMPreset_Click(sender, e);
                            break;
                        }
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EditORMPreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = ORMPresetsListBox.SelectedItem as ORMSettings;
            if (selectedPreset == null) return;

            if (selectedPreset.IsBuiltIn) {
                var result = MessageBox.Show(
                    "Editing a built-in preset will create a customized version.\nContinue?",
                    "Edit Built-in Preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            var editorWindow = new ORMPresetEditorWindow(selectedPreset);
            if (editorWindow.ShowDialog() == true && editorWindow.EditedPreset != null) {
                try {
                    _ormPresetManager.UpdatePreset(selectedPreset.Name, editorWindow.EditedPreset);
                    LoadORMPresets();
                    MessageBox.Show($"Preset '{editorWindow.EditedPreset.Name}' updated!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DuplicateORMPreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = ORMPresetsListBox.SelectedItem as ORMSettings;
            if (selectedPreset == null) return;

            var inputDialog = new InputDialog("Duplicate Preset", "Enter new name:", $"{selectedPreset.Name} Copy");
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.ResponseText)) {
                try {
                    _ormPresetManager.DuplicatePreset(selectedPreset.Name, inputDialog.ResponseText);
                    LoadORMPresets();
                    MessageBox.Show($"Preset duplicated as '{inputDialog.ResponseText}'!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteORMPreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = ORMPresetsListBox.SelectedItem as ORMSettings;
            if (selectedPreset == null) return;

            string message = selectedPreset.IsBuiltIn
                ? $"Hide built-in preset '{selectedPreset.Name}'?\nYou can restore it using 'Reset to Defaults'."
                : $"Delete preset '{selectedPreset.Name}'?";

            if (MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                try {
                    _ormPresetManager.DeletePreset(selectedPreset.Name);
                    LoadORMPresets();
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportORMPresets_Click(object sender, RoutedEventArgs e) {
            var dialog = new OpenFileDialog {
                Filter = "JSON files (*.json)|*.json",
                Title = "Import ORM Presets"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    int count = _ormPresetManager.ImportPresets(dialog.FileName);
                    LoadORMPresets();
                    MessageBox.Show($"Imported {count} preset(s)!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportORMPresets_Click(object sender, RoutedEventArgs e) {
            var dialog = new SaveFileDialog {
                Filter = "JSON files (*.json)|*.json",
                FileName = "orm_presets.json",
                Title = "Export ORM Presets"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    _ormPresetManager.ExportPresets(dialog.FileName);
                    MessageBox.Show("Presets exported!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Common

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e) {
            string currentTab = PresetTabControl.SelectedIndex == TextureTabIndex ? "Texture" : "ORM";
            var result = MessageBox.Show(
                $"Reset all {currentTab} presets to defaults?\nThis will delete custom presets.",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes) {
                if (PresetTabControl.SelectedIndex == TextureTabIndex) {
                    _texturePresetManager.ResetToDefaults();
                    LoadTexturePresets();
                } else {
                    _ormPresetManager.ResetToDefaults();
                    LoadORMPresets();
                }
                MessageBox.Show("Presets reset to defaults!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        #endregion
    }

    /// <summary>
    /// Simple input dialog for getting text input
    /// </summary>
    public class InputDialog : Window {
        private TextBox _textBox;

        public string ResponseText => _textBox.Text;

        public InputDialog(string title, string prompt, string defaultValue = "") {
            Title = title;
            Width = 350;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            _textBox = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            okButton.Click += (s, e) => { DialogResult = true; Close(); };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(buttonPanel);
            Content = grid;

            _textBox.SelectAll();
            _textBox.Focus();
        }
    }
}

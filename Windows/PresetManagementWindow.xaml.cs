using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Windows {
    public partial class PresetManagementWindow : Window {
        private PresetManager _presetManager;

        public PresetManagementWindow(PresetManager presetManager) {
            InitializeComponent();
            _presetManager = presetManager;
            LoadPresets();
        }

        private void LoadPresets() {
            PresetsListBox.ItemsSource = null;
            PresetsListBox.ItemsSource = _presetManager.GetAllPresets();
        }

        private void PresetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            bool hasSelection = PresetsListBox.SelectedItem != null;

            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection; // Теперь можно удалять и встроенные пресеты
        }

        private void NewPreset_Click(object sender, RoutedEventArgs e) {
            var editorWindow = new PresetEditorWindow(null);
            if (editorWindow.ShowDialog() == true && editorWindow.EditedPreset != null) {
                try {
                    _presetManager.AddPreset(editorWindow.EditedPreset);
                    LoadPresets();
                    MessageBox.Show($"Preset '{editorWindow.EditedPreset.Name}' created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error creating preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EditPreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = PresetsListBox.SelectedItem as TextureConversionPreset;
            if (selectedPreset == null) return;

            // Show warning for built-in presets
            if (selectedPreset.IsBuiltIn) {
                var result = MessageBox.Show(
                    "Editing a built-in preset will create a customized version that overrides the default.\nContinue?",
                    "Edit Built-in Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result != MessageBoxResult.Yes) {
                    return;
                }
            }

            var editorWindow = new PresetEditorWindow(selectedPreset);
            if (editorWindow.ShowDialog() == true && editorWindow.EditedPreset != null) {
                try {
                    _presetManager.UpdatePreset(selectedPreset.Name, editorWindow.EditedPreset);
                    LoadPresets();
                    MessageBox.Show($"Preset '{editorWindow.EditedPreset.Name}' updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error updating preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e) {
            var selectedPreset = PresetsListBox.SelectedItem as TextureConversionPreset;
            if (selectedPreset == null) return;

            string message;
            if (selectedPreset.IsBuiltIn) {
                message = $"Are you sure you want to hide the built-in preset '{selectedPreset.Name}'?\n\nYou can restore it later using 'Reset to Defaults'.";
            } else {
                // Проверяем, переопределяет ли этот пользовательский пресет встроенный
                var builtInPresets = TextureConversionPreset.GetBuiltInPresets();
                bool isOverridingBuiltIn = builtInPresets.Any(p =>
                    p.Name.Equals(selectedPreset.Name, StringComparison.OrdinalIgnoreCase));

                if (isOverridingBuiltIn) {
                    message = $"This will revert '{selectedPreset.Name}' to its default settings.\n\nAre you sure?";
                } else {
                    message = $"Are you sure you want to delete preset '{selectedPreset.Name}'?";
                }
            }

            var result = MessageBox.Show(
                message,
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes) {
                try {
                    _presetManager.DeletePreset(selectedPreset.Name);
                    LoadPresets();
                    MessageBox.Show($"Preset '{selectedPreset.Name}' deleted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error deleting preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportPresets_Click(object sender, RoutedEventArgs e) {
            var openFileDialog = new OpenFileDialog {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Import Presets"
            };

            if (openFileDialog.ShowDialog() == true) {
                try {
                    int count = _presetManager.ImportPresets(openFileDialog.FileName, overwriteExisting: false);
                    LoadPresets();
                    MessageBox.Show($"Successfully imported {count} preset(s)!", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error importing presets: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportPresets_Click(object sender, RoutedEventArgs e) {
            var saveFileDialog = new SaveFileDialog {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "texture_presets.json",
                Title = "Export Presets"
            };

            if (saveFileDialog.ShowDialog() == true) {
                try {
                    _presetManager.ExportPresets(saveFileDialog.FileName, includeBuiltIn: false);
                    MessageBox.Show("Presets exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Error exporting presets: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e) {
            var result = MessageBox.Show(
                "This will delete all custom presets and keep only built-in presets. Are you sure?",
                "Confirm Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes) {
                _presetManager.ResetToDefaults();
                LoadPresets();
                MessageBox.Show("Presets reset to defaults!", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }
    }
}

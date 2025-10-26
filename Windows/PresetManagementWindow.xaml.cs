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
            var selectedPreset = PresetsListBox.SelectedItem as TextureConversionPreset;

            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection && selectedPreset != null && !selectedPreset.IsBuiltIn;
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

            if (selectedPreset.IsBuiltIn) {
                MessageBox.Show("Built-in presets cannot be edited. You can create a copy instead.", "Cannot Edit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
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

            if (selectedPreset.IsBuiltIn) {
                MessageBox.Show("Built-in presets cannot be deleted.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete preset '{selectedPreset.Name}'?",
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

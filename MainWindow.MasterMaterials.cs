using AssetProcessor.MasterMaterials.Models;
using AssetProcessor.ViewModels;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using NLog;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing Master Materials tab event handlers:
    /// - Master material DataGrid interactions
    /// - Chunk selection and editing
    /// - Embedded chunk code editor (GLSL/WGSL)
    /// </summary>
    public partial class MainWindow {

        #region Master Materials Tab Event Handlers

        /// <summary>
        /// Wires event handlers for ChunkSlotsPanel controls.
        /// Called from MainWindow constructor after InitializeComponent.
        /// </summary>
        private void InitializeChunkSlotsPanel() {
            // Catch SlotEditChunk_Click via bubbling Button.Click from DataTemplate
            chunkSlotsPanel.AddHandler(Button.ClickEvent, new RoutedEventHandler(ChunkSlotsPanel_ButtonClick));
        }

        /// <summary>
        /// Wires event handlers for MasterMaterialsEditorPanel controls.
        /// Called from MainWindow constructor after InitializeComponent.
        /// </summary>
        private void InitializeMasterMaterialsEditorPanel() {
            masterMaterialsEditorPanel.MasterMaterialsDataGrid.MouseDoubleClick += MasterMaterialsDataGrid_MouseDoubleClick;
            masterMaterialsEditorPanel.ChunkEditorSaveButton.Click += ChunkEditorSaveButton_Click;
            masterMaterialsEditorPanel.ChunkEditorNewButton.Click += ChunkEditorNewButton_Click;
        }

        /// <summary>
        /// Handles bubbling Button.Click from ChunkSlotsPanel edit buttons.
        /// </summary>
        private void ChunkSlotsPanel_ButtonClick(object sender, RoutedEventArgs e) {
            if (e.OriginalSource is Button btn && btn.Tag is ChunkSlotViewModel slotVm) {
                var selectedChunk = slotVm.SelectedChunk;
                if (selectedChunk != null) {
                    viewModel.MasterMaterialsViewModel.SelectedChunk = selectedChunk;
                    LoadChunkIntoEditor(selectedChunk);
                }
            }
        }

        private void MasterMaterialsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            if (masterMaterialsEditorPanel.MasterMaterialsDataGrid.SelectedItem is MasterMaterial master) {
                if (!master.IsBuiltIn) {
                    OpenMasterMaterialEditor(master);
                }
            }
        }

        private void ChunkCheckBox_Click(object sender, RoutedEventArgs e) {
            if (sender is not CheckBox checkBox) return;
            if (checkBox.Tag is not ShaderChunk chunk) return;

            var selectedMaster = viewModel.MasterMaterialsViewModel.SelectedMaster;
            if (selectedMaster == null || selectedMaster.IsBuiltIn) return;

            if (checkBox.IsChecked == true) {
                // Add chunk to master
                _ = viewModel.MasterMaterialsViewModel.AddChunkToMasterAsync(selectedMaster, chunk);
                logger.Info($"Added chunk '{chunk.Id}' to master '{selectedMaster.Name}'");
            } else {
                // Remove chunk from master
                _ = viewModel.MasterMaterialsViewModel.RemoveChunkFromMasterAsync(selectedMaster, chunk.Id);
                logger.Info($"Removed chunk '{chunk.Id}' from master '{selectedMaster.Name}'");
            }

            // Force refresh the ListBox to update chunk count display
            chunkSlotsPanel.AllChunksListBox.Items.Refresh();
        }

        // ChunkLabel_MouseLeftButtonDown removed - selection is now handled by ListBox.SelectedItem binding

        private void MasterMaterial_Clone_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterial master) {
                viewModel.MasterMaterialsViewModel.CloneMasterCommand.Execute(master);
            }
        }

        private void MasterMaterial_Edit_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterial master) {
                if (!master.IsBuiltIn) {
                    OpenMasterMaterialEditor(master);
                }
            }
        }

        private void MasterMaterial_Delete_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterial master) {
                if (master.IsBuiltIn) {
                    MessageBox.Show("Cannot delete built-in master materials.", "Delete Master Material",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"Are you sure you want to delete master material '{master.Name}'?",
                    "Delete Master Material", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    viewModel.MasterMaterialsViewModel.DeleteMasterCommand.Execute(master);
                }
            }
        }

        private void Chunk_Edit_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is ShaderChunk chunk) {
                // Select the chunk, which will load it into the embedded editor via PropertyChanged
                viewModel.MasterMaterialsViewModel.SelectedChunk = chunk;
            }
        }

        private async void Chunk_Delete_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is ShaderChunk chunk) {
                if (chunk.IsBuiltIn) {
                    MessageBox.Show("Cannot delete built-in chunks.", "Delete Chunk", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"Are you sure you want to delete chunk '{chunk.Id}'?",
                    "Delete Chunk", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    await viewModel.MasterMaterialsViewModel.DeleteChunkCommand.ExecuteAsync(chunk);
                }
            }
        }

        private void Chunk_Copy_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is ShaderChunk chunk) {
                viewModel.MasterMaterialsViewModel.CopyChunkCommand.Execute(chunk);
            }
        }

        private void OpenMasterMaterialEditor(MasterMaterial master) {
            var availableChunks = viewModel.MasterMaterialsViewModel.Chunks.ToList();
            var editorWindow = new Windows.MasterMaterialEditorWindow(master, availableChunks) {
                Owner = this
            };

            if (editorWindow.ShowDialog() == true && editorWindow.EditedMaster != null) {
                // Update the master in the collection
                var index = viewModel.MasterMaterialsViewModel.MasterMaterials.IndexOf(master);
                if (index >= 0) {
                    viewModel.MasterMaterialsViewModel.MasterMaterials[index] = editorWindow.EditedMaster;
                }
                viewModel.MasterMaterialsViewModel.HasUnsavedChanges = true;
                logger.Info($"Master material '{editorWindow.EditedMaster.Name}' updated");
            }
        }

        private void OpenChunkEditor(ShaderChunk chunk) {
            // Instead of opening a separate window, load the chunk into the embedded editor
            LoadChunkIntoEditor(chunk);
        }

        #region Embedded Chunk Editor

        /// <summary>
        /// Initializes the embedded chunk code editor with syntax highlighting
        /// </summary>
        private void InitializeChunkCodeEditor() {
            // Load syntax highlighting definitions
            if (_glslHighlighting == null) {
                _glslHighlighting = LoadHighlightingDefinition("AssetProcessor.SyntaxHighlighting.GLSL.xshd");
            }
            if (_wgslHighlighting == null) {
                _wgslHighlighting = LoadHighlightingDefinition("AssetProcessor.SyntaxHighlighting.WGSL.xshd");
            }

            // Apply highlighting to editors
            if (_glslHighlighting != null) {
                masterMaterialsEditorPanel.GlslCodeEditor.SyntaxHighlighting = _glslHighlighting;
            }
            if (_wgslHighlighting != null) {
                masterMaterialsEditorPanel.WgslCodeEditor.SyntaxHighlighting = _wgslHighlighting;
            }

            // Wire up text change events
            masterMaterialsEditorPanel.GlslCodeEditor.TextChanged += (_, _) => OnChunkCodeChanged();
            masterMaterialsEditorPanel.WgslCodeEditor.TextChanged += (_, _) => OnChunkCodeChanged();

            // Subscribe to SelectedChunk changes
            viewModel.MasterMaterialsViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(viewModel.MasterMaterialsViewModel.SelectedChunk)) {
                    var selectedChunk = viewModel.MasterMaterialsViewModel.SelectedChunk;
                    if (selectedChunk != null) {
                        LoadChunkIntoEditor(selectedChunk);
                    }
                }
            };

            // Initial state
            UpdateChunkEditorStatus("Select a chunk to edit");
        }

        /// <summary>
        /// Loads a syntax highlighting definition from an embedded resource
        /// </summary>
        private static IHighlightingDefinition? LoadHighlightingDefinition(string resourceName) {
            try {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null) {
                    System.Diagnostics.Debug.WriteLine($"Could not find embedded resource: {resourceName}");
                    return null;
                }

                using var reader = new XmlTextReader(stream);
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error loading syntax highlighting: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads a chunk into the embedded editor
        /// </summary>
        private void LoadChunkIntoEditor(ShaderChunk chunk) {
            // Check for unsaved changes
            if (_chunkEditorHasUnsavedChanges && _currentEditingChunk != null) {
                var result = MessageBox.Show(
                    $"You have unsaved changes in chunk '{_currentEditingChunk.Id}'. Do you want to save them first?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    SaveCurrentChunk();
                } else if (result == MessageBoxResult.Cancel) {
                    // Restore selection to current chunk
                    viewModel.MasterMaterialsViewModel.SelectedChunk = _currentEditingChunk;
                    return;
                }
            }

            _currentEditingChunk = chunk;
            _originalGlslCode = chunk.Glsl;
            _originalWgslCode = chunk.Wgsl;

            // Load code into editors
            masterMaterialsEditorPanel.GlslCodeEditor.Text = chunk.Glsl ?? "";
            masterMaterialsEditorPanel.WgslCodeEditor.Text = chunk.Wgsl ?? "";

            // Set read-only state for built-in chunks
            masterMaterialsEditorPanel.GlslCodeEditor.IsReadOnly = chunk.IsBuiltIn;
            masterMaterialsEditorPanel.WgslCodeEditor.IsReadOnly = chunk.IsBuiltIn;

            // Update UI state
            _chunkEditorHasUnsavedChanges = false;
            UpdateChunkEditorUnsavedIndicator();

            if (chunk.IsBuiltIn) {
                UpdateChunkEditorStatus($"Viewing built-in chunk '{chunk.Id}' (read-only)");
            } else {
                UpdateChunkEditorStatus($"Editing chunk '{chunk.Id}'");
            }

            logger.Info($"Loaded chunk '{chunk.Id}' into editor");
        }

        /// <summary>
        /// Called when code changes in either editor
        /// </summary>
        private void OnChunkCodeChanged() {
            if (_currentEditingChunk == null || _currentEditingChunk.IsBuiltIn) return;

            // Check if there are actual changes
            bool glslChanged = masterMaterialsEditorPanel.GlslCodeEditor.Text != _originalGlslCode;
            bool wgslChanged = masterMaterialsEditorPanel.WgslCodeEditor.Text != _originalWgslCode;

            _chunkEditorHasUnsavedChanges = glslChanged || wgslChanged;
            UpdateChunkEditorUnsavedIndicator();
        }

        /// <summary>
        /// Updates the unsaved changes indicator visibility
        /// </summary>
        private void UpdateChunkEditorUnsavedIndicator() {
            masterMaterialsEditorPanel.ChunkEditorUnsavedIndicator.Visibility = _chunkEditorHasUnsavedChanges
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Updates the status text in the chunk editor
        /// </summary>
        private void UpdateChunkEditorStatus(string status) {
            masterMaterialsEditorPanel.ChunkEditorStatusText.Text = status;
        }

        /// <summary>
        /// Saves the current chunk being edited
        /// </summary>
        private void SaveCurrentChunk() {
            if (_currentEditingChunk == null) return;

            if (_currentEditingChunk.IsBuiltIn) {
                MessageBox.Show("Cannot save built-in chunks. Use 'Copy' to create an editable version.",
                    "Read-Only Chunk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Update chunk with new code
            _currentEditingChunk.Glsl = masterMaterialsEditorPanel.GlslCodeEditor.Text;
            _currentEditingChunk.Wgsl = masterMaterialsEditorPanel.WgslCodeEditor.Text;

            // Update in ViewModel
            viewModel.MasterMaterialsViewModel.UpdateChunk(_currentEditingChunk);

            // Update original values
            _originalGlslCode = masterMaterialsEditorPanel.GlslCodeEditor.Text;
            _originalWgslCode = masterMaterialsEditorPanel.WgslCodeEditor.Text;

            _chunkEditorHasUnsavedChanges = false;
            UpdateChunkEditorUnsavedIndicator();
            UpdateChunkEditorStatus($"Chunk '{_currentEditingChunk.Id}' saved");

            logger.Info($"Chunk '{_currentEditingChunk.Id}' saved");
        }

        /// <summary>
        /// Handler for Save Chunk button click
        /// </summary>
        private void ChunkEditorSaveButton_Click(object sender, RoutedEventArgs e) {
            SaveCurrentChunk();
        }

        /// <summary>
        /// Handler for New Chunk button click
        /// </summary>
        private void ChunkEditorNewButton_Click(object sender, RoutedEventArgs e) {
            // Check for unsaved changes
            if (_chunkEditorHasUnsavedChanges && _currentEditingChunk != null) {
                var result = MessageBox.Show(
                    $"You have unsaved changes in chunk '{_currentEditingChunk.Id}'. Do you want to save them first?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    SaveCurrentChunk();
                } else if (result == MessageBoxResult.Cancel) {
                    return;
                }
            }

            // Create new chunk
            viewModel.MasterMaterialsViewModel.AddNewChunkCommand.Execute(null);
        }

        #endregion

        #endregion
    }
}

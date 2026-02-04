using AssetProcessor.Resources;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing context menu handlers:
    /// - ORM creation handlers
    /// - Master Material context menu handlers
    /// - Texture context menu handlers
    /// - Model context menu handlers
    /// - Material context menu handlers
    /// - File/folder location helpers
    /// </summary>
    public partial class MainWindow {

        #region ORM Creation Handlers

        private void CreateORMButton_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            viewModel.ORMTexture.CreateEmptyORMCommand.Execute(viewModel.Textures);
        }

        // ORM from Material handlers
        private void CreateORMFromMaterial_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            var material = MaterialsDataGrid.SelectedItem as MaterialResource;
            viewModel.ORMTexture.CreateORMFromMaterialCommand.Execute(new ORMFromMaterialRequest {
                Material = material,
                Textures = viewModel.Textures
            });
        }

        private async void CreateORMForAllMaterials_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            await viewModel.ORMTexture.CreateAllORMsCommand.ExecuteAsync(new ORMBatchCreationRequest {
                Materials = viewModel.Materials,
                Textures = viewModel.Textures
            });
        }

        private void DeleteORMTexture_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            var material = MaterialsDataGrid.SelectedItem as MaterialResource;
            if (material == null) return;

            viewModel.ORMTexture.DeleteORMForMaterialCommand.Execute(new ORMDeleteRequest {
                Material = material,
                Textures = viewModel.Textures
            });
        }

        private void DeleteORMFromList_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            var ormTexture = TexturesDataGrid.SelectedItem as ORMTextureResource;
            viewModel.ORMTexture.DeleteORMCommand.Execute(new ORMDirectDeleteRequest {
                ORMTexture = ormTexture,
                Textures = viewModel.Textures
            });
        }

        #endregion

        #region Master Material Context Menu

        // Master Material assignment handlers
        private void SetMasterForSelectedMaterials_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem) {
                string? masterName = menuItem.Tag as string;
                if (string.IsNullOrEmpty(masterName)) masterName = null;

                // Get selected materials
                var selectedMaterials = MaterialsDataGrid.SelectedItems
                    .OfType<MaterialResource>()
                    .ToList();

                if (selectedMaterials.Count == 0) return;

                // Set master for all selected materials
                var materialIds = selectedMaterials.Select(m => m.ID).ToList();
                viewModel.MasterMaterialsViewModel.SetMasterForMaterials(materialIds, masterName);

                // Update UI
                foreach (var material in selectedMaterials) {
                    material.MasterMaterialName = masterName;
                }

                logService.LogInfo($"Set master '{masterName ?? "(none)"}' for {selectedMaterials.Count} materials");
            }
        }

        private void MaterialRowContextMenu_Opened(object sender, RoutedEventArgs e) {
            if (sender is ContextMenu contextMenu) {
                // Find "Set Master Material" menu item
                var setMasterMenuItem = contextMenu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(m => m.Header?.ToString() == "Set Master Material");

                if (setMasterMenuItem != null) {
                    setMasterMenuItem.Items.Clear();

                    // Add "(None)" option
                    var noneItem = new MenuItem {
                        Header = "(None)",
                        Tag = ""
                    };
                    noneItem.Click += SetMasterForSelectedMaterials_Click;
                    setMasterMenuItem.Items.Add(noneItem);

                    // Add separator
                    setMasterMenuItem.Items.Add(new Separator());

                    // Add all master materials
                    foreach (var master in viewModel.MasterMaterialsViewModel.MasterMaterials) {
                        var masterItem = new MenuItem {
                            Header = master.Name,
                            Tag = master.Name,
                            FontWeight = master.IsBuiltIn ? FontWeights.Normal : FontWeights.Bold
                        };
                        masterItem.Click += SetMasterForSelectedMaterials_Click;
                        setMasterMenuItem.Items.Add(masterItem);
                    }
                }
            }
        }

        #endregion

        #region Texture Context Menu Handlers

        private async void ProcessSelectedTextures_Click(object sender, RoutedEventArgs e) {
            if (viewModel.ProcessTexturesCommand is IAsyncRelayCommand<IList?> command) {
                await command.ExecuteAsync(TexturesDataGrid.SelectedItems);
            }
        }

        private void UploadTexture_Click(object sender, RoutedEventArgs e) {
            UploadTexturesButton_Click(sender, e);
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture && !string.IsNullOrEmpty(texture.Path)) {
                OpenFileInExplorer(texture.Path);
            }
        }

        private void OpenProcessedFolder_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture) {
                var processedPath = FindProcessedTexturePath(texture);
                if (!string.IsNullOrEmpty(processedPath)) {
                    OpenFileInExplorer(processedPath);
                } else {
                    MessageBox.Show("Processed file not found. Export the model first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void CopyTexturePath_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture && !string.IsNullOrEmpty(texture.Path)) {
                Helpers.ClipboardHelper.SetTextWithFeedback(texture.Path);
            }
        }

        private void RefreshPreview_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture) {
                // Trigger a refresh by re-selecting the texture
                TexturesDataGrid_SelectionChanged(TexturesDataGrid, null!);
            }
        }

        #endregion

        #region Model Context Menu Handlers

        private async void ProcessSelectedModel_Click(object sender, RoutedEventArgs e) {
            try {
                var selectedModel = ModelsDataGrid.SelectedItem as ModelResource;
                if (selectedModel == null) {
                    MessageBox.Show("No model selected for processing.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (string.IsNullOrEmpty(selectedModel.Path)) {
                    MessageBox.Show("Model file path is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get settings from ModelConversionSettingsPanel
                // Передаём путь к файлу для автоматического определения типа конвертера (FBX/GLB)
                var settings = ModelConversionSettingsPanel.GetSettings(selectedModel.Path);

                // Create output directory
                var modelName = Path.GetFileNameWithoutExtension(selectedModel.Path);
                var sourceDir = Path.GetDirectoryName(selectedModel.Path) ?? Environment.CurrentDirectory;
                var outputDir = Path.Combine(sourceDir, "glb");
                Directory.CreateDirectory(outputDir);

                logService.LogInfo($"Processing model: {selectedModel.Name}");
                logService.LogInfo($"  Source: {selectedModel.Path}");
                logService.LogInfo($"  Output: {outputDir}");

                // Load FBX2glTF and gltfpack paths from global settings
                var modelConversionSettings = ModelConversion.Settings.ModelConversionSettingsManager.LoadSettings();
                var fbx2glTFPath = string.IsNullOrWhiteSpace(modelConversionSettings.FBX2glTFExecutablePath)
                    ? "FBX2glTF-windows-x86_64.exe"
                    : modelConversionSettings.FBX2glTFExecutablePath;
                var gltfPackPath = string.IsNullOrWhiteSpace(modelConversionSettings.GltfPackExecutablePath)
                    ? "gltfpack.exe"
                    : modelConversionSettings.GltfPackExecutablePath;

                logService.LogInfo($"  FBX2glTF: {fbx2glTFPath}");
                logService.LogInfo($"  gltfpack: {gltfPackPath}");

                ProgressTextBlock.Text = $"Processing {selectedModel.Name}...";

                // Create the model conversion pipeline
                var pipeline = new ModelConversion.Pipeline.ModelConversionPipeline(fbx2glTFPath, gltfPackPath);

                var result = await pipeline.ConvertAsync(selectedModel.Path, outputDir, settings);

                if (result.Success) {
                    logService.LogInfo($"Model processed successfully");
                    logService.LogInfo($"  LOD files: {result.LodFiles.Count}");
                    logService.LogInfo($"  Manifest: {result.ManifestPath}");

                    // Автоматически обновляем viewport с новыми GLB LOD файлами
                    logService.LogInfo("Refreshing viewport with converted GLB LOD files...");
                    await TryLoadGlbLodAsync(selectedModel.Path);

                    MessageBox.Show($"Model processed successfully!\n\nLOD files: {result.LodFiles.Count}\nOutput: {outputDir}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    var errors = string.Join("\n", result.Errors);
                    logService.LogError($"❌ Model processing failed:\n{errors}");
                    MessageBox.Show($"Model processing failed:\n\n{errors}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                ProgressTextBlock.Text = "Ready";
            } catch (Exception ex) {
                logService.LogError($"Error processing model: {ex.Message}");
                MessageBox.Show($"Error processing model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressTextBlock.Text = "Ready";
            }
        }

        private void OpenModelFileLocation_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model && !string.IsNullOrEmpty(model.Path)) {
                OpenFileInExplorer(model.Path);
            }
        }

        private void OpenModelProcessedFolder_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model) {
                var processedPath = FindProcessedModelPath(model);
                if (!string.IsNullOrEmpty(processedPath)) {
                    OpenFileInExplorer(processedPath);
                } else {
                    MessageBox.Show("Processed file not found. Export the model first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void CopyModelPath_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model && !string.IsNullOrEmpty(model.Path)) {
                Helpers.ClipboardHelper.SetTextWithFeedback(model.Path);
            }
        }

        private void RefreshModelPreview_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model) {
                // Trigger a refresh by re-selecting the model
                ModelsDataGrid_SelectionChanged(ModelsDataGrid, null!);
            }
        }

        private void ModelConversionSettingsPanel_ProcessRequested(object sender, EventArgs e) {
            // When user clicks "Process Selected Model" button in the settings panel
            ProcessSelectedModel_Click(sender, new RoutedEventArgs());
        }

        #endregion

        #region Material Context Menu Handlers

        private void OpenMaterialSourceFolder_Click(object sender, RoutedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource material && !string.IsNullOrEmpty(material.Path)) {
                OpenFileInExplorer(material.Path);
            }
        }

        private void OpenMaterialProcessedFolder_Click(object sender, RoutedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource material) {
                var processedPath = FindProcessedMaterialPath(material);
                if (!string.IsNullOrEmpty(processedPath)) {
                    OpenFileInExplorer(processedPath);
                } else {
                    MessageBox.Show("Processed file not found. Export the model first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        #endregion

        #region File Location Helpers

        /// <summary>
        /// Finds the processed KTX2 file path for a texture
        /// </summary>
        private string? FindProcessedTexturePath(TextureResource texture) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) return null;

            var serverContentPath = Path.Combine(ProjectFolderPath, "server", "assets", "content");
            if (!Directory.Exists(serverContentPath)) return null;

            // Try to find KTX2 file by texture name
            var textureName = Path.GetFileNameWithoutExtension(texture.Path ?? texture.Name);
            if (string.IsNullOrEmpty(textureName)) return null;

            // Search for matching KTX2 file
            var ktx2Files = Directory.GetFiles(serverContentPath, $"{textureName}.ktx2", SearchOption.AllDirectories);
            if (ktx2Files.Length > 0) return ktx2Files[0];

            // Also try with _lod0 suffix (for some textures)
            ktx2Files = Directory.GetFiles(serverContentPath, $"{textureName}_*.ktx2", SearchOption.AllDirectories);
            if (ktx2Files.Length > 0) return ktx2Files[0];

            return null;
        }

        /// <summary>
        /// Finds the processed GLB file path for a model
        /// </summary>
        private string? FindProcessedModelPath(ModelResource model) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) return null;

            var serverContentPath = Path.Combine(ProjectFolderPath, "server", "assets", "content");
            if (!Directory.Exists(serverContentPath)) return null;

            var modelName = Path.GetFileNameWithoutExtension(model.Path ?? model.Name);
            if (string.IsNullOrEmpty(modelName)) return null;

            // Search for GLB files
            var glbFiles = Directory.GetFiles(serverContentPath, $"{modelName}*.glb", SearchOption.AllDirectories);
            if (glbFiles.Length > 0) return glbFiles[0];

            return null;
        }

        /// <summary>
        /// Finds the processed JSON file path for a material
        /// </summary>
        private string? FindProcessedMaterialPath(MaterialResource material) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) return null;

            var serverContentPath = Path.Combine(ProjectFolderPath, "server", "assets", "content");
            if (!Directory.Exists(serverContentPath)) return null;

            var materialName = material.Name;
            if (string.IsNullOrEmpty(materialName)) return null;

            // Search for JSON files
            var jsonFiles = Directory.GetFiles(serverContentPath, $"{materialName}.json", SearchOption.AllDirectories);
            if (jsonFiles.Length > 0) return jsonFiles[0];

            return null;
        }

        /// <summary>
        /// Opens Windows Explorer and selects the specified file.
        /// </summary>
        private void OpenFileInExplorer(string filePath) {
            try {
                if (File.Exists(filePath)) {
                    // Use /select to highlight the file in Explorer
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                } else {
                    // File doesn't exist, try to open the directory
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{directory}\"");
                    } else {
                        MessageBox.Show("File and directory not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show($"Failed to open file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region KTX2 Info Helper

        // Reads KTX2 file header to extract metadata (width, height, mip levels)
        private async Task<(int Width, int Height, int MipLevels, string CompressionFormat)> GetKtx2InfoAsync(string ktx2Path) {
            return await Task.Run(() => {
                using var stream = File.OpenRead(ktx2Path);
                using var reader = new BinaryReader(stream);

                // KTX2 header structure:
                // Bytes 0-11: identifier (12 bytes) - skip
                // Bytes 12-15: vkFormat (uint32) - 0 means Basis Universal
                // Bytes 16-19: typeSize (uint32) - skip
                // Bytes 20-23: pixelWidth (uint32)
                // Bytes 24-27: pixelHeight (uint32)
                // Bytes 28-31: pixelDepth (uint32) - skip
                // Bytes 32-35: layerCount (uint32) - skip
                // Bytes 36-39: faceCount (uint32) - skip
                // Bytes 40-43: levelCount (uint32)
                // Bytes 44-47: supercompressionScheme (uint32)

                reader.BaseStream.Seek(12, SeekOrigin.Begin);
                uint vkFormat = reader.ReadUInt32();

                reader.BaseStream.Seek(20, SeekOrigin.Begin);
                int width = (int)reader.ReadUInt32();
                int height = (int)reader.ReadUInt32();

                reader.BaseStream.Seek(40, SeekOrigin.Begin);
                int mipLevels = (int)reader.ReadUInt32();
                uint supercompression = reader.ReadUInt32();

                // Only set compression format for Basis Universal textures (vkFormat = 0)
                string compressionFormat = "";
                if (vkFormat == 0) {
                    // supercompressionScheme: 1=BasisLZ(ETC1S), 0/2=UASTC(None/Zstd)
                    compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
                }
                // vkFormat != 0 means raw texture format, no Basis compression

                return (width, height, mipLevels, compressionFormat);
            });
        }

        #endregion
    }
}

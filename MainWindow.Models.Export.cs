using AssetProcessor.Export;
using AssetProcessor.Resources;
using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.ModelConversion.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing export pipeline and asset selection logic:
    /// - ExportAssetsButton_Click (main export orchestrator)
    /// - SelectRelatedButton_Click / ClearExportMarksButton_Click (selection UI)
    /// - FindRelatedAssets / FindRelatedModels (dependency resolution)
    /// </summary>
    public partial class MainWindow {

        /// <summary>
        /// Wires event handlers for ExportToolsPanel controls.
        /// Called from MainWindow constructor after InitializeComponent.
        /// </summary>
        private void InitializeExportToolsPanel() {
            exportToolsPanel.SelectRelatedButton.Click += SelectRelatedButton_Click;
            exportToolsPanel.ClearExportMarksButton.Click += ClearExportMarksButton_Click;
            exportToolsPanel.ExportAssetsButton.Click += ExportAssetsButton_Click;
            exportToolsPanel.UploadToCloudButton.Click += UploadToCloudButton_Click;
            exportToolsPanel.CreateORMButton.Click += CreateORMButton_Click;
            exportToolsPanel.UploadTexturesButton.Click += UploadTexturesButton_Click;

            // Set CommandParameter directly (ElementName binding doesn't work across UserControl NameScopes)
            exportToolsPanel.ProcessTexturesButton.CommandParameter = TexturesDataGrid.SelectedItems;
            exportToolsPanel.AutoDetectAllButton.CommandParameter = TexturesDataGrid.SelectedItems;
        }

        /// <summary>
        /// Export all marked assets (models with related materials and textures)
        /// </summary>
        private async void ExportAssetsButton_Click(object sender, RoutedEventArgs e) {
            var modelsToExport = viewModel.Models.Where(m => m.ExportToServer).ToList();
            var materialsToExport = viewModel.Materials.Where(m => m.ExportToServer).ToList();
            var texturesToExport = viewModel.Textures.Where(t => t.ExportToServer).ToList();

            if (!modelsToExport.Any() && !materialsToExport.Any() && !texturesToExport.Any()) {
                MessageBox.Show("No assets marked for export.\nUse 'Select' to mark models, materials, or textures for export.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(AppSettings.Default.ProjectsFolderPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var projectName = ProjectName ?? "UnknownProject";
            var outputPath = AppSettings.Default.ProjectsFolderPath;

            var textureSettings = TextureConversionSettingsManager.LoadSettings();
            var ktxPath = string.IsNullOrWhiteSpace(textureSettings.KtxExecutablePath)
                ? "ktx" : textureSettings.KtxExecutablePath;

            var modelSettings = ModelConversionSettingsManager.LoadSettings();
            var fbx2glTFPath = string.IsNullOrWhiteSpace(modelSettings.FBX2glTFExecutablePath)
                ? "FBX2glTF-windows-x86_64.exe" : modelSettings.FBX2glTFExecutablePath;
            var gltfPackPath = string.IsNullOrWhiteSpace(modelSettings.GltfPackExecutablePath)
                ? "gltfpack.exe" : modelSettings.GltfPackExecutablePath;

            bool generateORM = viewModel.IsGenerateOrmChecked;
            bool generateLODs = viewModel.IsGenerateLodsChecked;

            try {
                viewModel.IsExportEnabled = false;
                viewModel.ExportButtonContent = "Exporting...";

                var pipeline = new ModelExportPipeline(
                    projectName, outputPath,
                    fbx2glTFPath: fbx2glTFPath,
                    gltfPackPath: gltfPackPath,
                    ktxPath: ktxPath
                );

                pipeline.ProgressChanged += progress => {
                    Dispatcher.Invoke(() => {
                        viewModel.ProgressText = progress.ShortStatus;
                    });
                };

                int projectId = 0;
                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId) &&
                    int.TryParse(viewModel.SelectedProjectId, out var pid)) {
                    projectId = pid;
                }

                var masterMaterialsConfig = viewModel.MasterMaterialsViewModel.Config;

                var options = new ExportOptions {
                    ProjectId = projectId,
                    ConvertModel = true,
                    ConvertTextures = true,
                    GenerateORMTextures = generateORM,
                    UsePackedTextures = generateORM,
                    GenerateLODs = generateLODs,
                    TextureQuality = 128,
                    ApplyToksvig = true,
                    UseSavedTextureSettings = true,
                    MasterMaterialsConfig = masterMaterialsConfig,
                    ProjectFolderPath = viewModel.MasterMaterialsViewModel.ProjectFolderPath,
                    DefaultMasterMaterial = masterMaterialsConfig?.DefaultMasterMaterial
                };

                int successCount = 0;
                int failCount = 0;
                var exportedFiles = new List<string>();

                // Identify model-owned materials to avoid double-exporting
                var modelMaterialIds = new HashSet<int>();
                foreach (var model in modelsToExport) {
                    var modelMaterials = viewModel.Materials.Where(m =>
                        m.Parent == model.Parent ||
                        (model.Name != null && m.Name != null && m.Name.StartsWith(model.Name.Split('_')[0], StringComparison.OrdinalIgnoreCase)));
                    foreach (var mat in modelMaterials) {
                        modelMaterialIds.Add(mat.ID);
                    }
                }

                var standaloneMaterials = materialsToExport.Where(m => !modelMaterialIds.Contains(m.ID)).ToList();

                // Identify standalone textures (not owned by any marked material)
                var allMaterialTextureIds = new HashSet<int>();
                foreach (var mat in materialsToExport) {
                    if (mat.DiffuseMapId.HasValue) allMaterialTextureIds.Add(mat.DiffuseMapId.Value);
                    if (mat.NormalMapId.HasValue) allMaterialTextureIds.Add(mat.NormalMapId.Value);
                    if (mat.AOMapId.HasValue) allMaterialTextureIds.Add(mat.AOMapId.Value);
                    if (mat.GlossMapId.HasValue) allMaterialTextureIds.Add(mat.GlossMapId.Value);
                    if (mat.MetalnessMapId.HasValue) allMaterialTextureIds.Add(mat.MetalnessMapId.Value);
                    if (mat.EmissiveMapId.HasValue) allMaterialTextureIds.Add(mat.EmissiveMapId.Value);
                }
                var standaloneTextures = texturesToExport.Where(t => !allMaterialTextureIds.Contains(t.ID)).ToList();

                int totalItems = modelsToExport.Count + standaloneMaterials.Count + standaloneTextures.Count;
                int currentItem = 0;

                viewModel.ProgressValue = 0;
                viewModel.ProgressMaximum = 100;

                // 1. Export models (with their materials and textures)
                foreach (var model in modelsToExport) {
                    try {
                        currentItem++;
                        viewModel.ProgressValue = (double)currentItem / totalItems * 100;
                        viewModel.ExportButtonContent = $"Export {currentItem}/{totalItems}";

                        logger.Info($"Exporting model: {model.Name} ({currentItem}/{totalItems})");

                        var result = await pipeline.ExportModelAsync(
                            model, viewModel.Materials, viewModel.Textures, folderPaths, options);

                        if (result.Success) {
                            successCount++;
                            logger.Info($"Export OK: {model.Name} -> {result.ExportPath}");

                            Dispatcher.Invoke(() => {
                                model.Status = "Processed";
                                foreach (var matId in result.ProcessedMaterialIds) {
                                    var mat = viewModel.Materials.FirstOrDefault(m => m.ID == matId);
                                    if (mat != null) mat.Status = "Processed";
                                }
                                foreach (var texId in result.ProcessedTextureIds) {
                                    var tex = viewModel.Textures.FirstOrDefault(t => t.ID == texId);
                                    if (tex != null) tex.Status = "Processed";
                                }
                            });

                            CollectExportedFiles(result, exportedFiles);
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {model.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {model.Name}");
                    }
                }

                // 2. Export standalone materials (JSON only)
                var materialOnlyOptions = new ExportOptions {
                    ProjectId = projectId,
                    ConvertModel = false,
                    ConvertTextures = false,
                    GenerateORMTextures = false,
                    MaterialJsonOnly = true,
                    UseSavedTextureSettings = true,
                    MasterMaterialsConfig = masterMaterialsConfig,
                    ProjectFolderPath = viewModel.MasterMaterialsViewModel.ProjectFolderPath,
                    DefaultMasterMaterial = masterMaterialsConfig?.DefaultMasterMaterial
                };

                foreach (var material in standaloneMaterials) {
                    try {
                        currentItem++;
                        viewModel.ProgressValue = (double)currentItem / totalItems * 100;
                        viewModel.ExportButtonContent = $"Export {currentItem}/{totalItems}";

                        logger.Info($"Exporting material (JSON only): {material.Name} ({currentItem}/{totalItems})");

                        var result = await pipeline.ExportMaterialAsync(
                            material, viewModel.Textures, folderPaths, materialOnlyOptions);

                        if (result.Success) {
                            successCount++;
                            Dispatcher.Invoke(() => { material.Status = "Processed"; });
                            if (!string.IsNullOrEmpty(result.GeneratedMaterialJson))
                                exportedFiles.Add(result.GeneratedMaterialJson);
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {material.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {material.Name}");
                    }
                }

                // 3. Export standalone textures
                foreach (var texture in standaloneTextures) {
                    try {
                        currentItem++;
                        viewModel.ProgressValue = (double)currentItem / totalItems * 100;
                        viewModel.ExportButtonContent = $"Export {currentItem}/{totalItems}";

                        logger.Info($"Exporting texture: {texture.Name} ({currentItem}/{totalItems})");

                        var result = await pipeline.ExportTextureAsync(texture, folderPaths, options);

                        if (result.Success) {
                            successCount++;
                            Dispatcher.Invoke(() => { texture.Status = "Processed"; });
                            if (!string.IsNullOrEmpty(result.ConvertedTexturePath))
                                exportedFiles.Add(result.ConvertedTexturePath);
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {texture.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {texture.Name}");
                    }
                }

                viewModel.ProgressValue = 100;
                logger.Info($"Export complete: {successCount} succeeded, {failCount} failed, {exportedFiles.Count} files");

                _lastExportedFiles = new List<string>(exportedFiles);

                var exportMessage = $"Export completed!\n\nSuccess: {successCount}\nFailed: {failCount}\n\nOutput: {pipeline.GetContentBasePath()}";

                if (successCount > 0 && viewModel.IsAutoUploadEnabled) {
                    var shouldUpload = MessageBox.Show(
                        exportMessage + "\n\nUpload to cloud now?",
                        "Export Result", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (shouldUpload == MessageBoxResult.Yes) {
                        await AutoUploadAfterExportAsync(pipeline.GetContentBasePath(), exportedFiles);
                    }
                } else {
                    MessageBox.Show(exportMessage, "Export Result", MessageBoxButton.OK,
                        successCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }

            } catch (Exception ex) {
                logger.Error(ex, "Export failed");
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                viewModel.IsExportEnabled = true;
                viewModel.ExportButtonContent = "Export";
                viewModel.ProgressValue = 0;
                viewModel.ProgressText = "";
            }
        }

        /// <summary>
        /// Collects all file paths from a model export result.
        /// </summary>
        private static void CollectExportedFiles(ModelExportResult result, List<string> exportedFiles) {
            if (!string.IsNullOrEmpty(result.ConvertedModelPath)) exportedFiles.Add(result.ConvertedModelPath);
            if (!string.IsNullOrEmpty(result.GeneratedModelJson)) exportedFiles.Add(result.GeneratedModelJson);
            exportedFiles.AddRange(result.LODPaths.Where(p => !string.IsNullOrEmpty(p)));
            exportedFiles.AddRange(result.GeneratedMaterialJsons.Where(p => !string.IsNullOrEmpty(p)));
            exportedFiles.AddRange(result.ConvertedTextures.Where(p => !string.IsNullOrEmpty(p)));
            exportedFiles.AddRange(result.GeneratedORMTextures.Where(p => !string.IsNullOrEmpty(p)));
            exportedFiles.AddRange(result.GeneratedChunksFiles.Where(p => !string.IsNullOrEmpty(p)));
        }

        /// <summary>
        /// Smart selection: marks selected assets and their related dependencies for export.
        /// </summary>
        private void SelectRelatedButton_Click(object sender, RoutedEventArgs e) {
            int modelsMarked = 0;
            int materialsMarked = 0;
            int texturesMarked = 0;

            var currentTab = tabControl.SelectedItem as System.Windows.Controls.TabItem;
            var tabHeader = currentTab?.Header?.ToString() ?? "";

            switch (tabHeader) {
                case "Models":
                    var selectedModels = ModelsDataGrid.SelectedItems.Cast<ModelResource>().ToList();
                    if (!selectedModels.Any()) {
                        MessageBox.Show("Select models in the table first", "Select", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    foreach (var model in selectedModels) {
                        if (!model.ExportToServer) { model.ExportToServer = true; modelsMarked++; }
                    }

                    var (relatedMaterials, relatedTextures) = FindRelatedAssets(selectedModels);
                    foreach (var material in relatedMaterials) {
                        if (!material.ExportToServer) { material.ExportToServer = true; materialsMarked++; }
                    }
                    foreach (var texture in relatedTextures) {
                        if (!texture.ExportToServer) { texture.ExportToServer = true; texturesMarked++; }
                    }
                    break;

                case "Materials":
                    var selectedMaterials = MaterialsDataGrid.SelectedItems.Cast<MaterialResource>().ToList();
                    if (!selectedMaterials.Any()) {
                        MessageBox.Show("Select materials in the table first", "Select", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    foreach (var material in selectedMaterials) {
                        if (!material.ExportToServer) { material.ExportToServer = true; materialsMarked++; }
                    }
                    break;

                case "Textures":
                    var selectedTextures = TexturesDataGrid.SelectedItems.Cast<TextureResource>().ToList();
                    if (!selectedTextures.Any()) {
                        MessageBox.Show("Select textures in the table first", "Select", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    foreach (var texture in selectedTextures) {
                        if (!texture.ExportToServer) { texture.ExportToServer = true; texturesMarked++; }
                    }
                    break;

                default:
                    MessageBox.Show("Switch to Models, Materials, or Textures tab to select assets",
                        "Select", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
            }

            UpdateExportCounts();

            var markedParts = new List<string>();
            if (modelsMarked > 0) markedParts.Add($"{modelsMarked} models");
            if (materialsMarked > 0) markedParts.Add($"{materialsMarked} materials");
            if (texturesMarked > 0) markedParts.Add($"{texturesMarked} textures");

            if (markedParts.Any()) {
                logService.LogInfo($"Marked for export: {string.Join(", ", markedParts)}");
            }
        }

        /// <summary>
        /// Clears all export marks from all asset types
        /// </summary>
        private void ClearExportMarksButton_Click(object sender, RoutedEventArgs e) {
            foreach (var model in viewModel.Models) model.ExportToServer = false;
            foreach (var material in viewModel.Materials) material.ExportToServer = false;
            foreach (var texture in viewModel.Textures) texture.ExportToServer = false;

            UpdateExportCounts();
            logService.LogInfo("All export marks cleared");
        }

        /// <summary>
        /// Finds materials and textures related to the given models.
        /// </summary>
        private (List<MaterialResource>, List<TextureResource>) FindRelatedAssets(List<ModelResource> models) {
            var relatedMaterials = new List<MaterialResource>();
            var relatedTextures = new HashSet<TextureResource>();

            foreach (var model in models) {
                string? modelFolderPath = null;
                if (model.Parent.HasValue && model.Parent.Value != 0) {
                    folderPaths.TryGetValue(model.Parent.Value, out modelFolderPath);
                }

                var modelBaseName = ExtractBaseName(model.Name);

                foreach (var material in viewModel.Materials) {
                    bool isRelated = false;

                    // By Parent ID
                    if (model.Parent.HasValue && material.Parent == model.Parent) {
                        isRelated = true;
                    }
                    // By folder path
                    else if (!string.IsNullOrEmpty(modelFolderPath) && material.Parent.HasValue) {
                        if (folderPaths.TryGetValue(material.Parent.Value, out var materialFolderPath)) {
                            if (materialFolderPath.StartsWith(modelFolderPath, StringComparison.OrdinalIgnoreCase)) {
                                isRelated = true;
                            }
                        }
                    }
                    // By name
                    else if (!string.IsNullOrEmpty(modelBaseName) && !string.IsNullOrEmpty(material.Name)) {
                        var materialBaseName = ExtractBaseName(material.Name);
                        if (materialBaseName.StartsWith(modelBaseName, StringComparison.OrdinalIgnoreCase) ||
                            modelBaseName.StartsWith(materialBaseName, StringComparison.OrdinalIgnoreCase)) {
                            isRelated = true;
                        }
                    }

                    if (isRelated && !relatedMaterials.Contains(material)) {
                        relatedMaterials.Add(material);

                        // Add all material textures
                        var textureIds = new[] {
                            material.DiffuseMapId, material.NormalMapId, material.SpecularMapId,
                            material.GlossMapId, material.MetalnessMapId, material.AOMapId,
                            material.EmissiveMapId, material.OpacityMapId
                        };

                        foreach (var id in textureIds.Where(id => id.HasValue)) {
                            var texture = viewModel.Textures.FirstOrDefault(t => t.ID == id!.Value);
                            if (texture != null) relatedTextures.Add(texture);
                        }
                    }
                }
            }

            logger.Info($"FindRelatedAssets: {models.Count} models -> {relatedMaterials.Count} materials, {relatedTextures.Count} textures");
            return (relatedMaterials, relatedTextures.ToList());
        }

        private static string ExtractBaseName(string? name) {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(name);
            var suffixes = new[] { "_mat", "_material", "_mtl" };
            foreach (var suffix in suffixes) {
                if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                    return baseName.Substring(0, baseName.Length - suffix.Length);
                }
            }
            return baseName;
        }

        /// <summary>
        /// Finds models related to the given materials (reverse lookup).
        /// </summary>
        private List<ModelResource> FindRelatedModels(List<MaterialResource> materials) {
            var relatedModels = new HashSet<ModelResource>();

            foreach (var material in materials) {
                string? materialFolderPath = null;
                if (material.Parent.HasValue && material.Parent.Value != 0) {
                    folderPaths.TryGetValue(material.Parent.Value, out materialFolderPath);
                }

                var materialBaseName = ExtractBaseName(material.Name);

                foreach (var model in viewModel.Models) {
                    bool isRelated = false;

                    if (material.Parent.HasValue && model.Parent == material.Parent) {
                        isRelated = true;
                    } else if (!string.IsNullOrEmpty(materialFolderPath) && model.Parent.HasValue) {
                        if (folderPaths.TryGetValue(model.Parent.Value, out var modelFolderPath)) {
                            if (materialFolderPath.StartsWith(modelFolderPath, StringComparison.OrdinalIgnoreCase)) {
                                isRelated = true;
                            }
                        }
                    } else if (!string.IsNullOrEmpty(materialBaseName) && !string.IsNullOrEmpty(model.Name)) {
                        var modelBaseName = ExtractBaseName(model.Name);
                        if (materialBaseName.StartsWith(modelBaseName, StringComparison.OrdinalIgnoreCase) ||
                            modelBaseName.StartsWith(materialBaseName, StringComparison.OrdinalIgnoreCase)) {
                            isRelated = true;
                        }
                    }

                    if (isRelated) relatedModels.Add(model);
                }
            }

            logger.Info($"FindRelatedModels: {materials.Count} materials -> {relatedModels.Count} models");
            return relatedModels.ToList();
        }
    }
}

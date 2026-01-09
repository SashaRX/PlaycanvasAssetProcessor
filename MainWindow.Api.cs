using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Assimp;
using HelixToolkit.Wpf;
using CommunityToolkit.Mvvm.Input;
using NLog;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;
using System.Linq;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;

namespace AssetProcessor {
    public partial class MainWindow {
        private async Task TryConnect(CancellationToken cancellationToken) {
            try {
                await viewModel.SyncProjectCommand.ExecuteAsync(cancellationToken);

                if (!string.IsNullOrEmpty(viewModel.CurrentProjectName)) {
                    projectSelectionService.UpdateProjectPath(
                        AppSettings.Default.ProjectsFolderPath,
                        new KeyValuePair<string, string>(string.Empty, viewModel.CurrentProjectName));
                }

                if (viewModel.FolderPaths != null) {
                    folderPaths = new Dictionary<int, string>(viewModel.FolderPaths);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in TryConnect: {ex.Message}");
                logService.LogError($"Error in TryConnect: {ex}");
            }
        }

        private async Task InitializeAsync() {
            // Попробуйте загрузить данные из сохраненного JSON
            bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
            if (!jsonLoaded) {
                MessageBox.Show("No saved data found. Please ensure the JSON file is available.");
            }
        }

        private async Task<bool> LoadAssetsFromJsonFileAsync() {
            // Delegate to AssetLoadingViewModel
            // The ViewModel raises events that are handled by MainWindow event handlers
            logService.LogInfo($"[LoadAssetsFromJsonFileAsync] Starting. ProjectFolderPath={ProjectFolderPath}, ProjectName={ProjectName}");

            if (string.IsNullOrEmpty(ProjectFolderPath) || string.IsNullOrEmpty(ProjectName)) {
                logService.LogError("Project folder path or name is null or empty");
                return false;
            }

            // Reset progress at start
            viewModel.ProgressValue = 0;
            viewModel.ProgressMaximum = 0;
            viewModel.ProgressText = "Loading...";

            try {
                logService.LogInfo("[LoadAssetsFromJsonFileAsync] Calling viewModel.AssetLoading.LoadAssetsCommand.ExecuteAsync...");
                await viewModel.AssetLoading.LoadAssetsCommand.ExecuteAsync(new ViewModels.AssetLoadRequest {
                    ProjectFolderPath = ProjectFolderPath,
                    ProjectName = ProjectName,
                    ProjectsBasePath = AppSettings.Default.ProjectsFolderPath,
                    ProjectId = CurrentProjectId
                });
                logService.LogInfo("[LoadAssetsFromJsonFileAsync] LoadAssetsCommand completed");

                // Start watching project folder for file deletions (UI-specific)
                StartFileWatcher();

                // Scan KTX2 files for compression info (UI-specific display update)
                ScanKtx2InfoForAllTextures();

                return true;
            } catch (Exception ex) {
                logService.LogError($"Error loading assets: {ex.Message}");
                MessageBox.Show($"Error loading JSON file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Post-processing after assets are loaded from JSON
        /// </summary>
        private async Task PostProcessLoadedAssetsAsync() {
            RecalculateIndices();
            DeferUpdateLayout();

            // Scan KTX2 files for compression info
            ScanKtx2InfoForAllTextures();

            // Detect and load local ORM textures (synchronous - fast header reads only)
            DetectAndLoadORMTextures();

            // Generate virtual ORM textures for groups with AO/Gloss/Metallic/Height
            GenerateVirtualORMTextures();

            // Start watching project folder for file deletions
            StartFileWatcher();

            // Restore upload state from database
            await RestoreUploadStatesAsync();
        }

        /// <summary>
        /// Восстанавливает состояние загрузки для всех текстур из базы данных
        /// </summary>
        private async Task RestoreUploadStatesAsync() {
            try {
                using var uploadStateService = new Data.UploadStateService();
                await uploadStateService.InitializeAsync();

                int restoredCount = 0;

                foreach (var texture in viewModel.Textures) {
                    if (string.IsNullOrEmpty(texture.Path)) continue;

                    // Compute KTX2 path from original path
                    var ktx2Path = System.IO.Path.ChangeExtension(texture.Path, ".ktx2");
                    if (string.IsNullOrEmpty(ktx2Path) || !System.IO.File.Exists(ktx2Path)) continue;

                    var record = await uploadStateService.GetByLocalPathAsync(ktx2Path);
                    if (record != null && record.Status == "Uploaded") {
                        texture.UploadedHash = record.ContentSha1;
                        texture.RemoteUrl = record.CdnUrl;
                        texture.LastUploadedAt = record.UploadedAt;

                        // Check if file has changed since last upload
                        using var stream = System.IO.File.OpenRead(ktx2Path);
                        var currentHash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream)).ToLowerInvariant();

                        if (string.Equals(currentHash, record.ContentSha1, StringComparison.OrdinalIgnoreCase)) {
                            texture.UploadStatus = "Uploaded";
                        } else {
                            texture.UploadStatus = "Outdated";
                        }

                        restoredCount++;
                    }
                }

                if (restoredCount > 0) {
                    logService.LogInfo($"Restored upload state for {restoredCount} textures");
                }
            } catch (Exception ex) {
                logService.LogWarn($"Failed to restore upload states: {ex.Message}");
            }
        }

        /// <summary>
        /// Детектирует и загружает локальные ORM текстуры (_og.ktx2, _ogm.ktx2, _ogmh.ktx2)
        /// Эти текстуры не являются частью PlayCanvas проекта, но хранятся локально
        /// </summary>
        private void DetectAndLoadORMTextures() {
            if (string.IsNullOrEmpty(ProjectFolderPath) || !Directory.Exists(ProjectFolderPath)) {
                return;
            }

            try {
                var ktx2Files = Directory.GetFiles(ProjectFolderPath!, "*.ktx2", SearchOption.AllDirectories);
                var ormAssociations = new List<(TextureResource texture, string subGroupName, ORMTextureResource orm)>();
                int ormCount = 0;

                foreach (var ktx2Path in ktx2Files) {
                    string fileName = Path.GetFileNameWithoutExtension(ktx2Path);

                    ChannelPackingMode? packingMode = null;
                    string baseName = fileName;

                    if (fileName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) {
                        packingMode = ChannelPackingMode.OG;
                        baseName = fileName.Substring(0, fileName.Length - 3);
                    } else if (fileName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) {
                        packingMode = ChannelPackingMode.OGM;
                        baseName = fileName.Substring(0, fileName.Length - 4);
                    } else if (fileName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) {
                        packingMode = ChannelPackingMode.OGMH;
                        baseName = fileName.Substring(0, fileName.Length - 5);
                    }

                    if (!packingMode.HasValue) continue;

                    string directory = Path.GetDirectoryName(ktx2Path) ?? "";
                    TextureResource? aoTexture = FindTextureByPattern(directory, baseName + "_ao");
                    TextureResource? glossTexture = FindTextureByPattern(directory, baseName + "_gloss");
                    TextureResource? metallicTexture = FindTextureByPattern(directory, baseName + "_metallic")
                                                    ?? FindTextureByPattern(directory, baseName + "_metalness")
                                                    ?? FindTextureByPattern(directory, baseName + "_Metalness")
                                                    ?? FindTextureByPattern(directory, baseName + "_metalic");

                    var ormTexture = new ORMTextureResource {
                        Name = fileName,
                        GroupName = baseName,
                        SubGroupName = fileName,
                        Path = ktx2Path,
                        PackingMode = packingMode.Value,
                        AOSource = aoTexture,
                        GlossSource = glossTexture,
                        MetallicSource = metallicTexture,
                        Status = "Converted",
                        Extension = ".ktx2",
                        ProjectId = CurrentProjectId,
                        SettingsKey = $"orm_{baseName}_{fileName}"
                    };

                    ormTexture.LoadSettings();

                    if (aoTexture != null) ormAssociations.Add((aoTexture, fileName, ormTexture));
                    if (glossTexture != null) ormAssociations.Add((glossTexture, fileName, ormTexture));
                    if (metallicTexture != null) ormAssociations.Add((metallicTexture, fileName, ormTexture));

                    if (File.Exists(ktx2Path)) {
                        var fileInfo = new FileInfo(ktx2Path);
                        ormTexture.CompressedSize = fileInfo.Length;
                        ormTexture.Size = (int)fileInfo.Length;

                        try {
                            var ktxInfo = GetKtx2InfoSync(ktx2Path);
                            if (ktxInfo.Width > 0 && ktxInfo.Height > 0) {
                                ormTexture.Resolution = new[] { ktxInfo.Width, ktxInfo.Height };
                                ormTexture.MipmapCount = ktxInfo.MipLevels;
                                if (!string.IsNullOrEmpty(ktxInfo.CompressionFormat)) {
                                    ormTexture.CompressionFormat = ktxInfo.CompressionFormat == "UASTC"
                                        ? TextureConversion.Core.CompressionFormat.UASTC
                                        : TextureConversion.Core.CompressionFormat.ETC1S;
                                }
                            }
                        } catch { }
                    }
                    ormCount++;
                }

                foreach (var (texture, subGroupName, orm) in ormAssociations) {
                    texture.SubGroupName = subGroupName;
                    texture.ParentORMTexture = orm;
                }

                if (ormCount > 0) {
                    logService.LogInfo($"Detected {ormCount} ORM textures, {ormAssociations.Count} associations");
                    RecalculateIndices();
                    DeferUpdateLayout();
                }
            } catch (Exception ex) {
                logService.LogError($"Error detecting ORM textures: {ex.Message}");
            }
        }

        /// <summary>
        /// Синхронное чтение метаданных KTX2 заголовка (быстрое - всего ~50 байт)
        /// </summary>
        private static (int Width, int Height, int MipLevels, string? CompressionFormat) GetKtx2InfoSync(string ktx2Path) {
            using var stream = File.OpenRead(ktx2Path);
            using var reader = new BinaryReader(stream);

            // KTX2 header structure
            reader.BaseStream.Seek(12, SeekOrigin.Begin);
            uint vkFormat = reader.ReadUInt32();

            reader.BaseStream.Seek(20, SeekOrigin.Begin);
            int width = (int)reader.ReadUInt32();
            int height = (int)reader.ReadUInt32();

            reader.BaseStream.Seek(40, SeekOrigin.Begin);
            int mipLevels = (int)reader.ReadUInt32();
            uint supercompression = reader.ReadUInt32();

            string? compressionFormat = null;
            if (vkFormat == 0) {
                compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
            }

            return (width, height, mipLevels, compressionFormat);
        }

        /// <summary>
        /// Находит текстуру по паттерну имени в указанной директории
        /// </summary>
        private TextureResource? FindTextureByPattern(string directory, string namePattern) {
            return viewModel.Textures.FirstOrDefault(t => {
                if (string.IsNullOrEmpty(t.Path)) return false;
                if (Path.GetDirectoryName(t.Path) != directory) return false;

                string textureName = Path.GetFileNameWithoutExtension(t.Path);
                return string.Equals(textureName, namePattern, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Генерирует виртуальные ORM текстуры для групп, содержащих AO/Gloss/Metallic/Height компоненты.
        /// Эти виртуальные текстуры отображаются в UI и могут быть обработаны для создания упакованных ORM текстур.
        /// </summary>
        private void GenerateVirtualORMTextures() {
            try {
                var textureGroups = viewModel.Textures
                    .Where(t => !t.IsORMTexture && !string.IsNullOrEmpty(t.GroupName))
                    .GroupBy(t => t.GroupName)
                    .ToList();

                int generatedCount = 0;

                foreach (var group in textureGroups) {
                    string groupName = group.Key!;

                    TextureResource? aoTexture = group.FirstOrDefault(t => t.TextureType == "AO");
                    TextureResource? glossTexture = group.FirstOrDefault(t =>
                        t.TextureType == "Gloss" || t.TextureType == "Roughness");
                    TextureResource? metallicTexture = group.FirstOrDefault(t => t.TextureType == "Metallic");
                    TextureResource? heightTexture = group.FirstOrDefault(t => t.TextureType == "Height");

                    if (aoTexture == null && glossTexture == null) continue;

                    int channelCount = (aoTexture != null ? 1 : 0) +
                                      (glossTexture != null ? 1 : 0) +
                                      (metallicTexture != null ? 1 : 0) +
                                      (heightTexture != null ? 1 : 0);

                    if (channelCount < 2) continue;

                    ChannelPackingMode packingMode;
                    string suffix;

                    if (heightTexture != null && metallicTexture != null) {
                        packingMode = ChannelPackingMode.OGMH;
                        suffix = "_ogmh";
                    } else if (metallicTexture != null) {
                        packingMode = ChannelPackingMode.OGM;
                        suffix = "_ogm";
                    } else {
                        packingMode = ChannelPackingMode.OG;
                        suffix = "_og";
                    }

                    string baseName = groupName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)
                        ? groupName.Substring(0, groupName.Length - 4)
                        : groupName;
                    string ormName = baseName + suffix;
                    bool alreadyHasORM = (aoTexture?.ParentORMTexture != null) ||
                                         (glossTexture?.ParentORMTexture != null) ||
                                         (metallicTexture?.ParentORMTexture != null) ||
                                         (heightTexture?.ParentORMTexture != null);

                    if (alreadyHasORM) continue;

                    var sourceForResolution = aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture;
                    int[] resolution = sourceForResolution?.Resolution ?? [0, 0];

                    string? sourcePath = (aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture)?.Path;
                    string? directory = !string.IsNullOrEmpty(sourcePath) ? Path.GetDirectoryName(sourcePath) : null;

                    var ormTexture = new ORMTextureResource {
                        Name = ormName,
                        GroupName = groupName,
                        Path = directory != null ? Path.Combine(directory, ormName + ".ktx2") : null,
                        PackingMode = packingMode,
                        AOSource = aoTexture,
                        GlossSource = glossTexture,
                        MetallicSource = metallicTexture,
                        HeightSource = heightTexture,
                        Resolution = resolution,
                        Status = "Not Packed",
                        Extension = ".ktx2",
                        TextureType = $"ORM ({packingMode})",
                        ProjectId = CurrentProjectId,
                        SettingsKey = $"orm_{groupName}_{ormName}"
                    };

                    ormTexture.LoadSettings();
                    ormTexture.RestoreSources(viewModel.Textures.OfType<TextureResource>().ToList());

                    if (aoTexture != null) {
                        aoTexture.SubGroupName = ormName;
                        aoTexture.ParentORMTexture = ormTexture;
                    }
                    if (glossTexture != null) {
                        glossTexture.SubGroupName = ormName;
                        glossTexture.ParentORMTexture = ormTexture;
                    }
                    if (metallicTexture != null) {
                        metallicTexture.SubGroupName = ormName;
                        metallicTexture.ParentORMTexture = ormTexture;
                    }
                    if (heightTexture != null) {
                        heightTexture.SubGroupName = ormName;
                        heightTexture.ParentORMTexture = ormTexture;
                    }

                    generatedCount++;
                }

                if (generatedCount > 0) {
                    logService.LogInfo($"Generated {generatedCount} virtual ORM textures");
                    RecalculateIndices();
                    DeferUpdateLayout();
                }
            } catch (Exception ex) {
                logService.LogError($"Error generating virtual ORM textures: {ex.Message}");
            }
        }
    }
}



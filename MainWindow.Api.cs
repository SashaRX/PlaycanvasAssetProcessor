using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Assimp;
using HelixToolkit.Wpf;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
using System.Windows.Controls.Primitives; // DragDeltaEventArgs для GridSplitter
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


        private void BuildFolderHierarchyFromAssets(JArray assetsResponse) {
            try {
                folderPaths.Clear();

                // Извлекаем только папки из списка ассетов
                var folders = assetsResponse.Where(asset => asset["type"]?.ToString() == "folder").ToList();

                // Создаем словарь для быстрого доступа к папкам по ID
                Dictionary<int, JToken> foldersById = new();
                foreach (JToken folder in folders) {
                    int? folderId = folder["id"]?.Type == JTokenType.Integer ? (int?)folder["id"] : null;
                    if (folderId.HasValue) {
                        foldersById[folderId.Value] = folder;
                    }
                }

                // Рекурсивная функция для построения полного пути папки
                string BuildFolderPath(int folderId) {
                    if (folderPaths.ContainsKey(folderId)) {
                        return folderPaths[folderId];
                    }

                    if (!foldersById.ContainsKey(folderId)) {
                        return string.Empty;
                    }

                    JToken folder = foldersById[folderId];
                    // КРИТИЧНО: Используем SanitizePath для очистки имени папки от \r, \n и пробелов!
                    string folderName = PathSanitizer.SanitizePath(folder["name"]?.ToString());
                    int? parentId = folder["parent"]?.Type == JTokenType.Integer ? (int?)folder["parent"] : null;

                    string fullPath;
                    if (parentId.HasValue && parentId.Value != 0) {
                        // Есть родительская папка - рекурсивно строим путь
                        string parentPath = BuildFolderPath(parentId.Value);
                        fullPath = string.IsNullOrEmpty(parentPath) ? folderName : Path.Combine(parentPath, folderName);
                    } else {
                        // Папка верхнего уровня (parent == 0 или null)
                        fullPath = folderName;
                    }

                    // КРИТИЧНО: Применяем SanitizePath к финальному пути для гарантии
                    fullPath = PathSanitizer.SanitizePath(fullPath);

                    folderPaths[folderId] = fullPath;
                    return fullPath;
                }

                // Строим пути для всех папок
                foreach (var folderId in foldersById.Keys) {
                    BuildFolderPath(folderId);
                }

                logService.LogInfo($"Built folder hierarchy with {folderPaths.Count} folders from assets list");
            } catch (Exception ex) {
                logService.LogError($"Error building folder hierarchy from assets: {ex.Message}");
                // Продолжаем работу даже если не удалось загрузить папки
            }
        }

        private async Task ProcessAsset(JToken asset, int index, CancellationToken cancellationToken) {
            AssetProcessingResult? result = null;

            try {
                await getAssetsSemaphore.WaitAsync(cancellationToken);
                AssetProcessingParameters parameters = CreateAssetProcessingParameters(index);
                result = await assetResourceService.ProcessAssetAsync(asset, parameters, cancellationToken);
            } catch (Exception ex) {
                logService.LogError($"Error in ProcessAsset: {ex}");
            } finally {
                getAssetsSemaphore.Release();
            }

            if (result != null) {
                await Dispatcher.InvokeAsync(() => ApplyProcessedAssetResult(result));
            }
        }

        private AssetProcessingParameters CreateAssetProcessingParameters(int assetIndex) {
            if (string.IsNullOrEmpty(ProjectName)) {
                throw new InvalidOperationException("Project name is required for asset processing.");
            }

            return new AssetProcessingParameters(
                AppSettings.Default.ProjectsFolderPath,
                ProjectName!,
                folderPaths,
                assetIndex);
        }

        private void ApplyProcessedAssetResult(AssetProcessingResult result) {
            switch (result.ResultType) {
                case AssetProcessingResultType.Texture when result.Resource is TextureResource texture:
                    viewModel.Textures.Add(texture);
                    viewModel.Assets.Add(texture);
                    UpdateTextureProgress();
                    break;
                case AssetProcessingResultType.Model when result.Resource is ModelResource model:
                    viewModel.Models.Add(model);
                    viewModel.Assets.Add(model);
                    break;
                case AssetProcessingResultType.Material when result.Resource is MaterialResource material:
                    viewModel.Materials.Add(material);
                    viewModel.Assets.Add(material);
                    break;
            }
        }

        private void UpdateTextureProgress() {
            if (ProgressBar.Maximum <= 0) {
                ProgressBar.Maximum = viewModel.Textures.Count;
            }

            ProgressBar.Value = Math.Min(ProgressBar.Maximum, ProgressBar.Value + 1);
            ProgressTextBlock.Text = $"{ProgressBar.Value}/{ProgressBar.Maximum}";
        }

        private async Task InitializeAsync() {
            // Попробуйте загрузить данные из сохраненного JSON
            bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
            if (!jsonLoaded) {
                MessageBox.Show("No saved data found. Please ensure the JSON file is available.");
            }
        }

        private async Task<bool> LoadAssetsFromJsonFileAsync() {
            try {
                logService.LogInfo("=== LoadAssetsFromJsonFileAsync CALLED ===");

                if (String.IsNullOrEmpty(ProjectFolderPath) || String.IsNullOrEmpty(ProjectName)) {
                    throw new Exception("Project folder path or name is null or empty");
                }

                JArray? assetsResponse = await projectAssetService.LoadAssetsFromJsonAsync(ProjectFolderPath!, CancellationToken.None);
                if (assetsResponse != null) {
                    logService.LogInfo($"Loaded {assetsResponse.Count} assets from local JSON cache");

                    // Строим иерархию папок из списка ассетов
                    assetResourceService.BuildFolderHierarchy(assetsResponse, folderPaths);

                    await ProcessAssetsFromJson(assetsResponse);
                    logService.LogInfo("=== LoadAssetsFromJsonFileAsync COMPLETED ===");
                    return true;
                }
            } catch (JsonReaderException ex) {
                MessageBox.Show($"Invalid JSON format: {ex.Message}");
            } catch (Exception ex) {
                MessageBox.Show($"Error loading JSON file: {ex.Message}");
            }
            return false;
        }

        private async Task ProcessAssetsFromJson(JToken assetsResponse) {
            viewModel.Textures.Clear();
            viewModel.Models.Clear();
            viewModel.Materials.Clear();
            viewModel.Assets.Clear();

            List<JToken> supportedAssets = [.. assetsResponse.Where(asset => asset["file"] != null)];
            int assetCount = supportedAssets.Count;

            await Dispatcher.InvokeAsync(() =>
            {
                ProgressBar.Value = 0;
                ProgressBar.Maximum = assetCount;
                ProgressTextBlock.Text = $"0/{assetCount}";
            });

            // Create throttled progress to batch UI updates (reduces Dispatcher calls from thousands to dozens)
            int processedCount = 0;
            var progress = new Progress<int>(increment => {
                int newCount = Interlocked.Add(ref processedCount, increment);
                Dispatcher.InvokeAsync(() => {
                    ProgressBar.Value = newCount;
                    ProgressTextBlock.Text = $"{newCount}/{assetCount}";
                });
            });
            using var throttledProgress = new ThrottledProgress<int>(progress, intervalMs: 100);

            IEnumerable<Task> tasks = supportedAssets.Select(asset => Task.Run(async () =>
            {
                await ProcessAsset(asset, 0, CancellationToken.None); // Используем токен отмены по умолчанию
                throttledProgress.Report(1); // Report increment, will be batched
            }));

            await Task.WhenAll(tasks);

            // Final flush to ensure progress shows 100%
            await Dispatcher.InvokeAsync(() => {
                ProgressBar.Value = assetCount;
                ProgressTextBlock.Text = $"{assetCount}/{assetCount}";
            });

            RecalculateIndices(); // Пересчитываем индексы после обработки всех ассетов
            DeferUpdateLayout(); // Отложенное обновление layout для предотвращения множественных перерисовок

            // Сканируем KTX2 файлы для получения информации о компрессии
            ScanKtx2InfoForAllTextures();

            // Детектируем и загружаем локальные ORM текстуры
            await DetectAndLoadORMTextures();

            // Генерируем виртуальные ORM текстуры для групп с AO/Gloss/Metallic/Height
            GenerateVirtualORMTextures();

            // Start watching project folder for file deletions
            StartFileWatcher();
        }

        /// <summary>
        /// Детектирует и загружает локальные ORM текстуры (_og.ktx2, _ogm.ktx2, _ogmh.ktx2)
        /// Эти текстуры не являются частью PlayCanvas проекта, но хранятся локально
        /// </summary>
        private async Task DetectAndLoadORMTextures() {
            if (string.IsNullOrEmpty(ProjectFolderPath) || !Directory.Exists(ProjectFolderPath)) {
                return;
            }

            logService.LogInfo("=== Detecting local ORM textures ===");

            try {
                // Сканируем все .ktx2 файлы рекурсивно
                var ktx2Files = Directory.GetFiles(ProjectFolderPath!, "*.ktx2", SearchOption.AllDirectories);

                int ormCount = 0;

                foreach (var ktx2Path in ktx2Files) {
                    string fileName = Path.GetFileNameWithoutExtension(ktx2Path);

                    // Проверяем паттерны _og/_ogm/_ogmh
                    ChannelPackingMode? packingMode = null;
                    string baseName = fileName;

                    if (fileName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) {
                        packingMode = ChannelPackingMode.OG;
                        baseName = fileName.Substring(0, fileName.Length - 3); // Remove "_og"
                    } else if (fileName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) {
                        packingMode = ChannelPackingMode.OGM;
                        baseName = fileName.Substring(0, fileName.Length - 4); // Remove "_ogm"
                    } else if (fileName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) {
                        packingMode = ChannelPackingMode.OGMH;
                        baseName = fileName.Substring(0, fileName.Length - 5); // Remove "_ogmh"
                    }

                    if (!packingMode.HasValue) {
                        continue; // Not an ORM texture
                    }

                    // Ищем source текстуры по имени base
                    string directory = Path.GetDirectoryName(ktx2Path) ?? "";
                    TextureResource? aoTexture = FindTextureByPattern(directory, baseName + "_ao");
                    TextureResource? glossTexture = FindTextureByPattern(directory, baseName + "_gloss");
                    TextureResource? metallicTexture = FindTextureByPattern(directory, baseName + "_metallic")
                                                    ?? FindTextureByPattern(directory, baseName + "_metalic"); // typo variant

                    // Создаем ORMTextureResource
                    var ormTexture = new ORMTextureResource {
                        Name = fileName,
                        Path = ktx2Path,
                        PackingMode = packingMode.Value,
                        AOSource = aoTexture,
                        GlossSource = glossTexture,
                        MetallicSource = metallicTexture,
                        Status = "Converted", // Already packed
                        Extension = ".ktx2"
                    };

                    // Извлекаем информацию о файле и метаданные из KTX2
                    if (File.Exists(ktx2Path)) {
                        var fileInfo = new FileInfo(ktx2Path);
                        ormTexture.CompressedSize = fileInfo.Length;
                        ormTexture.Size = (int)fileInfo.Length; // For ORM, Size = CompressedSize

                        // Извлекаем метаданные из KTX2: resolution, mipmap count, compression format
                        try {
                            var ktxInfo = await GetKtx2InfoAsync(ktx2Path);
                            if (ktxInfo.Width > 0 && ktxInfo.Height > 0) {
                                ormTexture.Resolution = new[] { ktxInfo.Width, ktxInfo.Height };
                                ormTexture.MipmapCount = ktxInfo.MipLevels;
                                // Set compression format from KTX2 header only if it's Basis Universal
                                if (!string.IsNullOrEmpty(ktxInfo.CompressionFormat)) {
                                    ormTexture.CompressionFormat = ktxInfo.CompressionFormat == "UASTC"
                                        ? TextureConversion.Core.CompressionFormat.UASTC
                                        : TextureConversion.Core.CompressionFormat.ETC1S;
                                    logService.LogInfo($"    Extracted metadata: {ktxInfo.Width}x{ktxInfo.Height}, {ktxInfo.MipLevels} mips, {ktxInfo.CompressionFormat}");
                                } else {
                                    logService.LogInfo($"    Extracted metadata: {ktxInfo.Width}x{ktxInfo.Height}, {ktxInfo.MipLevels} mips, (no Basis compression)");
                                }
                            }
                        } catch (Exception ex) {
                            logService.LogError($"  Failed to extract KTX2 metadata for {fileName}: {ex.Message}");
                        }
                    }

                    Dispatcher.Invoke(() => {
                        viewModel.Textures.Add(ormTexture);
                    });

                    ormCount++;
                    logService.LogInfo($"  Loaded ORM texture: {fileName} ({packingMode.Value})");
                }

                if (ormCount > 0) {
                    logService.LogInfo($"=== Detected {ormCount} ORM textures ===");
                    Dispatcher.Invoke(() => {
                        RecalculateIndices(); // Recalculate indices after adding ORM textures
                        DeferUpdateLayout(); // Отложенное обновление layout для предотвращения множественных перерисовок
                    });
                } else {
                    logService.LogInfo("  No ORM textures found");
                }

            } catch (Exception ex) {
                logService.LogError($"Error detecting ORM textures: {ex.Message}");
            }
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
            logService.LogInfo("=== Generating virtual ORM textures ===");

            try {
                // Группируем текстуры по GroupName, исключая уже существующие ORM текстуры
                var textureGroups = viewModel.Textures
                    .Where(t => !t.IsORMTexture && !string.IsNullOrEmpty(t.GroupName))
                    .GroupBy(t => t.GroupName)
                    .ToList();

                int generatedCount = 0;

                foreach (var group in textureGroups) {
                    string groupName = group.Key!;

                    // Ищем компоненты ORM в группе (значения из DetermineTextureType)
                    TextureResource? aoTexture = group.FirstOrDefault(t => t.TextureType == "AO");
                    TextureResource? glossTexture = group.FirstOrDefault(t =>
                        t.TextureType == "Gloss" || t.TextureType == "Roughness");
                    TextureResource? metallicTexture = group.FirstOrDefault(t => t.TextureType == "Metallic");
                    TextureResource? heightTexture = group.FirstOrDefault(t => t.TextureType == "Height");

                    // Определяем режим упаковки на основе доступных компонентов
                    // Минимум нужны AO и Gloss для создания ORM
                    if (aoTexture == null && glossTexture == null) {
                        continue; // Недостаточно компонентов для ORM
                    }

                    // Подсчитываем доступные каналы
                    int channelCount = (aoTexture != null ? 1 : 0) +
                                      (glossTexture != null ? 1 : 0) +
                                      (metallicTexture != null ? 1 : 0) +
                                      (heightTexture != null ? 1 : 0);

                    if (channelCount < 2) {
                        continue; // Нужно минимум 2 канала для ORM упаковки
                    }

                    // Определяем режим упаковки
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

                    // Проверяем, не существует ли уже ORM текстура с таким именем
                    string ormName = groupName + suffix;
                    bool alreadyExists = viewModel.Textures.Any(t =>
                        t.IsORMTexture && string.Equals(t.Name, ormName, StringComparison.OrdinalIgnoreCase));

                    if (alreadyExists) {
                        continue; // ORM текстура уже существует
                    }

                    // Определяем разрешение из первого доступного источника
                    var sourceForResolution = aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture;
                    int[] resolution = sourceForResolution?.Resolution ?? [0, 0];

                    // Определяем путь (папку) из первого доступного источника
                    string? sourcePath = (aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture)?.Path;
                    string? directory = !string.IsNullOrEmpty(sourcePath) ? Path.GetDirectoryName(sourcePath) : null;

                    // Создаём виртуальную ORM текстуру
                    // SubGroupName = ormName чтобы ORM и её компоненты были в одной подгруппе внутри основной группы
                    var ormTexture = new ORMTextureResource {
                        Name = ormName,
                        GroupName = groupName,
                        SubGroupName = ormName,  // ORM текстура - заголовок подгруппы
                        Path = directory != null ? Path.Combine(directory, ormName + ".ktx2") : null,
                        PackingMode = packingMode,
                        AOSource = aoTexture,
                        GlossSource = glossTexture,
                        MetallicSource = metallicTexture,
                        HeightSource = heightTexture,
                        Resolution = resolution,
                        Status = "Not Packed",
                        Extension = ".ktx2",
                        TextureType = $"ORM ({packingMode})"
                    };

                    // Устанавливаем SubGroupName для ORM компонентов чтобы они группировались вместе с ORM
                    if (aoTexture != null) aoTexture.SubGroupName = ormName;
                    if (glossTexture != null) glossTexture.SubGroupName = ormName;
                    if (metallicTexture != null) metallicTexture.SubGroupName = ormName;
                    if (heightTexture != null) heightTexture.SubGroupName = ormName;

                    viewModel.Textures.Add(ormTexture);
                    generatedCount++;

                    logService.LogInfo($"  Generated virtual ORM: {ormName} ({packingMode}) - " +
                        $"AO:{(aoTexture != null ? "✓" : "✗")} " +
                        $"Gloss:{(glossTexture != null ? "✓" : "✗")} " +
                        $"Metal:{(metallicTexture != null ? "✓" : "✗")} " +
                        $"Height:{(heightTexture != null ? "✓" : "✗")}");
                }

                if (generatedCount > 0) {
                    logService.LogInfo($"=== Generated {generatedCount} virtual ORM textures ===");
                    RecalculateIndices();
                    DeferUpdateLayout();
                } else {
                    logService.LogInfo("  No virtual ORM textures to generate");
                }

            } catch (Exception ex) {
                logService.LogError($"Error generating virtual ORM textures: {ex.Message}");
            }
        }
    }
}



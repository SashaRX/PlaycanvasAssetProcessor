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

        private async Task SaveJsonResponseToFile(JToken jsonResponse, string projectFolderPath, string projectName) {
            try {
                string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");

                if (!Directory.Exists(projectFolderPath)) {
                    Directory.CreateDirectory(projectFolderPath);
                }

                string jsonString = jsonResponse.ToString(Formatting.Indented);
                await File.WriteAllTextAsync(jsonFilePath, jsonString);

                logService.LogInfo($"Assets list saved to {jsonFilePath}");
            } catch (ArgumentNullException ex) {
                logService.LogError($"Argument error: {ex.Message}");
            } catch (ArgumentException ex) {
                logService.LogError($"Argument error: {ex.Message}");
            } catch (Exception ex) {
                logService.LogError($"Error saving assets list to JSON: {ex.Message}");
            }
        }

        private async Task ProcessAsset(JToken asset, int index, CancellationToken cancellationToken) {
            try {
                await getAssetsSemaphore.WaitAsync(cancellationToken);

                string? type = asset["type"]?.ToString() ?? string.Empty;
                string? assetPath = asset["path"]?.ToString() ?? string.Empty;
                logService.LogInfo($"Processing {type}, API path: {assetPath}");

                if (!string.IsNullOrEmpty(type) && ignoredAssetTypes.Contains(type)) {
                    lock (ignoredAssetTypesLock) {
                        if (reportedIgnoredAssetTypes.Add(type)) {
                            logService.LogInfo($"Asset type '{type}' is currently ignored (stub handler).");
                        }
                    }
                    return;
                }

                // Обработка материала без параметра file
                if (type == "material") {
                    await ProcessMaterialAsset(asset, index, cancellationToken);
                    return;
                }

                JToken? file = asset["file"];
                if (file == null || file.Type != JTokenType.Object) {
                    logService.LogError("Invalid asset file format");
                    return;
                }

                string? fileUrl = MainWindowHelpers.GetFileUrl(file);
                if (string.IsNullOrEmpty(fileUrl)) {
                    throw new Exception("File URL is null or empty");
                }

                string? extension = MainWindowHelpers.GetFileExtension(fileUrl);
                if (string.IsNullOrEmpty(extension)) {
                    throw new Exception("Unable to determine file extension");
                }

                switch (type) {
                    case "texture" when IsSupportedTextureFormat(extension):
                        await ProcessTextureAsset(asset, index, fileUrl, extension, cancellationToken);
                        break;
                    case "scene" when IsSupportedModelFormat(extension):
                        await ProcessModelAsset(asset, index, fileUrl, extension, cancellationToken);
                        break;
                    default:
                        logService.LogError($"Unsupported asset type or format: {type} - {extension}");
                        break;
                }
            } catch (Exception ex) {
                logService.LogError($"Error in ProcessAsset: {ex}");
            } finally {
                getAssetsSemaphore.Release();
            }
        }

        private async Task ProcessModelAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken _) {
            ArgumentNullException.ThrowIfNull(asset);

            if (!string.IsNullOrEmpty(fileUrl)) {
                if (string.IsNullOrEmpty(extension)) {
                    throw new ArgumentException($"'{nameof(extension)}' cannot be null or empty.", nameof(extension));
                }

                try {
                    string? assetPath = asset["path"]?.ToString();
                    int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
                    ModelResource model = new() {
                        ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                        Index = index,
                        Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                        Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
                        Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                        Path = GetResourcePath(asset["name"]?.ToString(), parentId),
                        Extension = extension,
                        Status = "On Server",
                        Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                        Parent = parentId,
                        UVChannels = 0 // Инициализация значения UV каналов
                    };

                    await MainWindowHelpers.VerifyAndProcessResourceAsync(model, async () => {
                        logService.LogInfo($"Adding model to list: {model.Name}");

                        switch (model.Status) {
                            case "Downloaded":
                                if (File.Exists(model.Path)) {
                                    AssimpContext context = new();
                                    logService.LogInfo($"Attempting to import file: {model.Path}");
                                    Scene scene = context.ImportFile(model.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
                                    logService.LogInfo($"Import result: {scene != null}");

                                    if (scene == null || scene.Meshes == null || scene.MeshCount <= 0) {
                                        logService.LogError("Scene is null or has no meshes.");
                                        return;
                                    }

                                    Mesh? mesh = scene.Meshes.FirstOrDefault();
                                    if (mesh != null) {
                                        model.UVChannels = mesh.TextureCoordinateChannelCount;
                                    }
                                }
                                break;
                            case "On Server":
                                break;
                            case "Size Mismatch":
                                break;
                            case "Corrupted":
                                break;
                            case "Empty File":
                                break;
                            case "Hash ERROR":
                                break;
                            case "Error":
                                break;
                        }


                    await Dispatcher.InvokeAsync(() => viewModel.Models.Add(model));
                }, logService);
                } catch (FileNotFoundException ex) {
                    logService.LogError($"File not found: {ex.FileName}");
                } catch (Exception ex) {
                    logService.LogError($"Error processing model: {ex.Message}");
                }
            } else {
                throw new ArgumentException($"'{nameof(fileUrl)}' cannot be null or empty.", nameof(fileUrl));
            }
        }

        private async Task ProcessTextureAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            try {
                // КРИТИЧНО: Используем SanitizePath для очистки имени файла от \r, \n и пробелов!
                string rawFileName = asset["name"]?.ToString() ?? "Unknown";
                string cleanFileName = PathSanitizer.SanitizePath(rawFileName);
                string textureName = cleanFileName.Split('.')[0];
                int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;

                // КРИТИЧНО: Применяем SanitizePath к пути текстуры!
                string texturePath = PathSanitizer.SanitizePath(GetResourcePath(cleanFileName, parentId));

                // Extract resolution from variants (eliminates HTTP request!)
                int[] resolution = new int[2];
                JToken? variants = asset["file"]?["variants"];
                if (variants != null && variants.Type == JTokenType.Object) {
                    // Try common variant formats in order: webp, jpg, png, original
                    foreach (string variantName in new[] { "webp", "jpg", "png", "original" }) {
                        JToken? variant = variants[variantName];
                        if (variant != null && variant.Type == JTokenType.Object) {
                            int? width = variant["width"]?.Type == JTokenType.Integer ? (int?)variant["width"] : null;
                            int? height = variant["height"]?.Type == JTokenType.Integer ? (int?)variant["height"] : null;
                            if (width.HasValue && height.HasValue) {
                                resolution[0] = width.Value;
                                resolution[1] = height.Value;
                                break;
                            }
                        }
                    }
                }

                TextureResource texture = new() {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = textureName,
                    Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
                    Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                    Path = texturePath,
                    Extension = extension,
                    Resolution = resolution,
                    ResizeResolution = new int[2],
                    Status = "On Server",
                    Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                    Parent = parentId,
                    Type = asset["type"]?.ToString(), // Устанавливаем свойство Type
                    GroupName = TextureResource.ExtractBaseTextureName(textureName),
                    TextureType = TextureResource.DetermineTextureType(textureName)
                };

                await MainWindowHelpers.VerifyAndProcessResourceAsync(texture, async () => {
                    logService.LogInfo($"Adding texture to list: {texture.Name}");

                    switch (texture.Status) {
                        case "Downloaded":
                            (int width, int height)? localResolution = MainWindowHelpers.GetLocalImageResolution(texture.Path, logService);
                            if (localResolution.HasValue) {
                                texture.Resolution[0] = localResolution.Value.width;
                                texture.Resolution[1] = localResolution.Value.height;
                            }
                            break;
                        case "On Server":
                            // Only fetch resolution via HTTP if not available from API variants
                            if (texture.Resolution[0] == 0 || texture.Resolution[1] == 0) {
                                await MainWindowHelpers.UpdateTextureResolutionAsync(texture, logService, cancellationToken);
                            }
                            break;
                        case "Size Mismatch":
                            break;
                        case "Corrupted":
                            break;
                        case "Empty File":
                            break;
                        case "Hash ERROR":
                            break;
                        case "Error":
                            break;
                    }

                    await Dispatcher.InvokeAsync(() => viewModel.Textures.Add(texture));
                    Dispatcher.Invoke(() => {
                        ProgressBar.Value++;
                        ProgressTextBlock.Text = $"{ProgressBar.Value}/{viewModel.Textures.Count}";
                    });
                }, logService);
            } catch (Exception ex) {
                logService.LogError($"Error processing texture: {ex.Message}");
            }
        }

        private async Task ProcessMaterialAsset(JToken asset, int index, CancellationToken cancellationToken) {
            try {
                string name = asset["name"]?.ToString() ?? "Unknown";
                string? assetPath = asset["path"]?.ToString();
                int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;

                MaterialResource material = new() {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = name,
                    Size = 0, // У материалов нет файла, поэтому размер 0
                    Path = GetResourcePath($"{name}.json", parentId),
                    Status = "On Server",
                    Hash = string.Empty, // У материалов нет хеша
                    Parent = parentId
                                         //TextureIds = []
                };

                await MainWindowHelpers.VerifyAndProcessResourceAsync(material, async () => {
                    logService.LogInfo($"Processing material: {material.Name}, Status: {material.Status}");

                    // Load full material data to get MapIds
                    if (material.Status == "Downloaded" && !string.IsNullOrEmpty(material.Path) && File.Exists(material.Path)) {
                        // Material JSON exists locally - parse MapIds from it
                        try {
                            MaterialResource detailedMaterial = await ParseMaterialJsonAsync(material.Path);
                            if (detailedMaterial != null) {
                                // Copy parsed MapIds and other properties to material
                                material.AOMapId = detailedMaterial.AOMapId;
                                material.GlossMapId = detailedMaterial.GlossMapId;
                                material.MetalnessMapId = detailedMaterial.MetalnessMapId;
                                material.SpecularMapId = detailedMaterial.SpecularMapId;
                                material.DiffuseMapId = detailedMaterial.DiffuseMapId;
                                material.NormalMapId = detailedMaterial.NormalMapId;
                                material.EmissiveMapId = detailedMaterial.EmissiveMapId;
                                material.OpacityMapId = detailedMaterial.OpacityMapId;
                                material.UseMetalness = detailedMaterial.UseMetalness;

                                logService.LogInfo($"Loaded MapIds for '{material.Name}': " +
                                    $"AO={material.AOMapId?.ToString() ?? "null"}, Gloss={material.GlossMapId?.ToString() ?? "null"}, " +
                                    $"Metalness={material.MetalnessMapId?.ToString() ?? "null"}, Specular={material.SpecularMapId?.ToString() ?? "null"}");
                            }
                        } catch (Exception ex) {
                            logService.LogWarn($"Failed to parse material JSON for '{material.Name}': {ex.Message}");
                        }
                    } else {
                        // Material not downloaded yet - MapIds will be unavailable until download
                        logService.LogInfo($"Material '{material.Name}' not downloaded, MapIds unavailable (Status: {material.Status})");
                    }

                    logService.LogInfo($"Adding material to list: {material.Name}");
                    await Dispatcher.InvokeAsync(() => viewModel.Materials.Add(material));
                }, logService);
            } catch (Exception ex) {
                logService.LogError($"Error processing material: {ex.Message}");
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
            try {
                logService.LogInfo("=== LoadAssetsFromJsonFileAsync CALLED ===");

                if (String.IsNullOrEmpty(ProjectFolderPath) || String.IsNullOrEmpty(ProjectName)) {
                    throw new Exception("Project folder path or name is null or empty");
                }

                JArray? assetsResponse = await projectAssetService.LoadAssetsFromJsonAsync(ProjectFolderPath!, CancellationToken.None);
                if (assetsResponse != null) {
                    logService.LogInfo($"Loaded {assetsResponse.Count} assets from local JSON cache");

                    // Строим иерархию папок из списка ассетов
                    BuildFolderHierarchyFromAssets(assetsResponse);

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

            // Детектируем и загружаем локальные ORM текстуры
            await DetectAndLoadORMTextures();
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
                        Status = "Converted" // Already packed
                    };

                    // Извлекаем информацию о файле
                    if (File.Exists(ktx2Path)) {
                        var fileInfo = new FileInfo(ktx2Path);
                        ormTexture.CompressedSize = fileInfo.Length;

                        // Извлекаем метаданные из KTX2: resolution и mipmap count
                        try {
                            var ktxInfo = await GetKtx2InfoAsync(ktx2Path);
                            if (ktxInfo.Width > 0 && ktxInfo.Height > 0) {
                                ormTexture.Resolution = new[] { ktxInfo.Width, ktxInfo.Height };
                                ormTexture.MipmapCount = ktxInfo.MipLevels;
                                logService.LogInfo($"    Extracted metadata: {ktxInfo.Width}x{ktxInfo.Height}, {ktxInfo.MipLevels} mips");
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
    }
}



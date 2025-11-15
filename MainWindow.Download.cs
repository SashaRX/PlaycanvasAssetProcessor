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
        private async Task Download(object? sender, RoutedEventArgs? e) {
            try {
                logger.Info("Download: Starting download process");
                logService.LogInfo("Download: Starting download process");

                List<BaseResource> selectedResources = [.. viewModel.Textures.Where(t => t.Status == "On Server" ||
                                                            t.Status == "Size Mismatch" ||
                                                            t.Status == "Corrupted" ||
                                                            t.Status == "Empty File" ||
                                                            t.Status == "Hash ERROR" ||
                                                            t.Status == "Error")
                                                .Cast<BaseResource>()
                                                .Concat(viewModel.Models.Where(m => m.Status == "On Server" ||
                                                                          m.Status == "Size Mismatch" ||
                                                                          m.Status == "Corrupted" ||
                                                                          m.Status == "Empty File" ||
                                                                          m.Status == "Hash ERROR" ||
                                                                          m.Status == "Error").Cast<BaseResource>())
                                                .Concat(viewModel.Materials.Where(m => m.Status == "On Server" ||
                                                                             m.Status == "Size Mismatch" ||
                                                                             m.Status == "Corrupted" ||
                                                                             m.Status == "Empty File" ||
                                                                             m.Status == "Hash ERROR" ||
                                                                             m.Status == "Error").Cast<BaseResource>())
                                                .OrderBy(r => r.Name)];

                logger.Info($"Download: Found {selectedResources.Count} resources to download");
                logService.LogInfo($"Download: Found {selectedResources.Count} resources to download");

                if (selectedResources.Count == 0) {
                    logger.Info("Download: No resources to download");
                    logService.LogInfo("Download: No resources to download - all files are already downloaded");
                    return;
                }

                IEnumerable<Task> downloadTasks = selectedResources.Select(resource => DownloadResourceAsync(resource));
                await Task.WhenAll(downloadTasks);

                logger.Info("Download: All downloads completed");
                logService.LogInfo("Download: All downloads completed");

                // НЕ вызываем RecalculateIndices() здесь, так как после этого будет вызван
                // CheckProjectState() -> LoadAssetsFromJsonFileAsync() -> ProcessAssetsFromJson(),
                // который уже вызовет RecalculateIndices() в конце. Это предотвращает множественную перерисовку.
            } catch (Exception ex) {
                logger.Error(ex, "Error in Download");
                MessageBox.Show($"Error: {ex.Message}");
                logService.LogError($"Error: {ex}");
            }
        }

        private async Task DownloadResourceAsync(BaseResource resource) {
            const int maxRetries = 5;

            await downloadSemaphore.WaitAsync(); // Ожидаем освобождения слота в семафоре
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        resource.Status = "Downloading";
                        resource.DownloadProgress = 0;

                        if (resource is MaterialResource materialResource) {
                            // Обработка загрузки материалов по ID
                            await DownloadMaterialByIdAsync(materialResource);
                        } else {
                            // Обработка загрузки файлов (текстур и моделей)
                            await DownloadFileAsync(resource);
                        }
                        break;
                    } catch (Exception ex) {
                        logService.LogError($"Error downloading resource: {ex.Message}");
                        resource.Status = "Error";
                        if (attempt == maxRetries) {
                            break;
                        }
                    }
                }
            } finally {
                downloadSemaphore.Release();
            }

        }

        private async Task DownloadMaterialByIdAsync(MaterialResource materialResource) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    string? apiKey = GetDecryptedApiKey();
                    PlayCanvasAssetDetail materialJson = await playCanvasService.GetAssetByIdAsync(materialResource.ID.ToString(), apiKey ?? "", default)
                        ?? throw new Exception($"Failed to get material JSON for ID: {materialResource.ID}");

                    // Изменение: заменяем последнюю папку на файл с расширением .json
                    string directoryPath = Path.GetDirectoryName(materialResource.Path) ?? throw new InvalidOperationException();
                    string materialPath = Path.Combine(directoryPath, $"{materialResource.Name}.json");

                    Directory.CreateDirectory(directoryPath);

                    await File.WriteAllTextAsync(materialPath, materialJson.ToJsonString(), default);
                    materialResource.Status = "Downloaded";
                    break;
                } catch (IOException ex) {
                    if (attempt == maxRetries) {
                        materialResource.Status = "Error";
                        logService.LogError($"Error downloading material after {maxRetries} attempts: {ex.Message}");
                    } else {
                        logService.LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                        await Task.Delay(delayMilliseconds);
                    }
                } catch (Exception ex) {
                    materialResource.Status = "Error";
                    logService.LogError($"Error downloading material: {ex.Message}");
                    break;
                }
            }
        }

        private async Task DownloadFileAsync(BaseResource resource) {
            if (resource == null || string.IsNullOrEmpty(resource.Path)) {
                return;
            }

            string? apiKey = GetDecryptedApiKey();
            if (string.IsNullOrEmpty(apiKey)) {
                resource.Status = "Error";
                logService.LogError("API key is missing for download operation.");
                return;
            }

            try {
                ResourceDownloadResult result = await localCacheService.DownloadFileAsync(resource, apiKey, cancellationTokenSource.Token);
                if (!result.IsSuccess) {
                    string message = result.ErrorMessage ?? $"Download finished with status {result.Status}";
                    logService.LogWarn($"Download for {resource.Name} completed with status {result.Status} after {result.Attempts} attempts. {message}");
                } else {
                    logService.LogInfo($"File downloaded successfully: {resource.Path}");
                }
            } catch (OperationCanceledException) {
                resource.Status = "Cancelled";
                logService.LogWarn($"Download cancelled for {resource.Name}");
            } catch (Exception ex) {
                resource.Status = "Error";
                logService.LogError($"Error downloading resource: {ex.Message}");
            }
        }
    }
}

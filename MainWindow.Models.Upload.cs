using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Settings;
using AssetProcessor.Upload;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing upload-to-cloud logic:
    /// - AutoUploadAfterExportAsync (post-export upload)
    /// - UploadToCloudButton_Click (manual upload)
    /// - Shared B2 upload pipeline (BuildUploadFilePairs, PerformB2UploadAsync)
    /// - SaveUploadRecordsAndUpdateStatusesAsync (persistence + UI status)
    /// </summary>
    public partial class MainWindow {

        /// <summary>
        /// Automatic upload after export — uploads only the specified exported files.
        /// </summary>
        private async Task AutoUploadAfterExportAsync(string contentPath, List<string> exportedFiles) {
            if (!ValidateB2Credentials()) return;

            if (exportedFiles.Count == 0) {
                MessageBox.Show("No files to upload.", "Upload", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var serverPath = Path.GetDirectoryName(Path.GetDirectoryName(contentPath));
            await PerformB2UploadAsync(exportedFiles, serverPath);
        }

        /// <summary>
        /// Manual upload button — uploads files from the last export (_lastExportedFiles).
        /// </summary>
        private async void UploadToCloudButton_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(AppSettings.Default.ProjectsFolderPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateB2Credentials()) return;

            if (_lastExportedFiles.Count == 0) {
                MessageBox.Show(
                    "No files to upload.\n\nPlease export assets first, then click Upload.",
                    "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var projectName = ProjectName ?? "UnknownProject";
            var serverPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName, "server");

            logger.Info($"Upload starting: {_lastExportedFiles.Count} files from last export");
            await PerformB2UploadAsync(_lastExportedFiles, serverPath);
        }

        /// <summary>
        /// Validates that B2 credentials are configured.
        /// </summary>
        private static bool ValidateB2Credentials() {
            if (string.IsNullOrEmpty(AppSettings.Default.B2KeyId) ||
                string.IsNullOrEmpty(AppSettings.Default.B2BucketName)) {
                MessageBox.Show(
                    "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure.",
                    "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Shared B2 upload pipeline: initialize service, build file pairs, upload batch,
        /// upload mapping.json, save records, update UI statuses.
        /// </summary>
        private async Task PerformB2UploadAsync(List<string> filesToUpload, string? serverPath) {
            var projectName = ProjectName ?? "UnknownProject";

            try {
                exportToolsPanel.UploadToCloudButton.IsEnabled = false;
                exportToolsPanel.UploadToCloudButton.Content = "Uploading...";

                using var b2Service = new B2UploadService();
                using var uploadStateService = new Data.UploadStateService();
                var uploadCoordinator = new AssetUploadCoordinator(b2Service, uploadStateService);

                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var filePairs = BuildUploadFilePairs(filesToUpload, serverPath);
                logger.Info($"Uploading {filePairs.Count} files (from {filesToUpload.Count} exported)");

                var result = await b2Service.UploadBatchAsync(
                    filePairs,
                    progress: new Progress<B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value = p.PercentComplete * 0.9;
                            var fileName = Path.GetFileName(p.CurrentFile);
                            viewModel.ProgressText = $"Upload: {fileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                        });
                    })
                );

                // Upload mapping.json separately (lives in server/, not content/)
                int mappingUploaded = 0;
                if (!string.IsNullOrEmpty(serverPath)) {
                    mappingUploaded = await TryUploadMappingJsonAsync(b2Service, uploadStateService, serverPath, projectName);
                    await SaveUploadRecordsAndUpdateStatusesAsync(result, serverPath, projectName, uploadStateService);
                }

                Dispatcher.Invoke(() => { ProgressBar.Value = 100; });

                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Uploaded: {result.SuccessCount + mappingUploaded}\n" +
                    $"Skipped (already exists): {result.SkippedCount}\n" +
                    $"Failed: {result.FailedCount}\n" +
                    (mappingUploaded > 0 ? "mapping.json: uploaded\n" : "") +
                    $"Duration: {result.Duration.TotalSeconds:F1}s",
                    "Upload Result", MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            } catch (Exception ex) {
                logger.Error(ex, "Upload failed");
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                exportToolsPanel.UploadToCloudButton.IsEnabled = true;
                exportToolsPanel.UploadToCloudButton.Content = "Upload to Cloud";
                ProgressBar.Value = 0;
                viewModel.ProgressText = "";
            }
        }

        /// <summary>
        /// Builds (localPath, remotePath) pairs from exported file list.
        /// Remote paths are relative to server/ with assets/ prefix stripped.
        /// </summary>
        private List<(string LocalPath, string RemotePath)> BuildUploadFilePairs(List<string> exportedFiles, string? serverPath) {
            var filePairs = new List<(string LocalPath, string RemotePath)>();

            foreach (var localPath in exportedFiles) {
                if (!File.Exists(localPath)) {
                    logger.Warn($"Exported file not found: {localPath}");
                    continue;
                }

                string remotePath;
                if (!string.IsNullOrEmpty(serverPath) && localPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase)) {
                    var relativePath = localPath.Substring(serverPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    // Strip assets/ prefix — remote path should be content/models/...
                    if (relativePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) {
                        relativePath = relativePath.Substring("assets/".Length);
                    }
                    remotePath = relativePath;
                } else {
                    remotePath = $"content/{Path.GetFileName(localPath)}";
                }

                filePairs.Add((localPath, remotePath));
            }

            return filePairs;
        }

        /// <summary>
        /// Uploads mapping.json if it exists. Returns 1 if uploaded, 0 otherwise.
        /// </summary>
        private async Task<int> TryUploadMappingJsonAsync(
            B2UploadService b2Service,
            Data.UploadStateService uploadStateService,
            string serverPath,
            string projectName) {

            var mappingPath = Path.Combine(serverPath, "mapping.json");
            if (!File.Exists(mappingPath)) return 0;

            try {
                var mappingResult = await b2Service.UploadFileAsync(mappingPath, "mapping.json", null);
                if (mappingResult.Success) {
                    logger.Info($"Uploaded mapping.json to {projectName}/mapping.json");

                    var mappingRecord = new Data.UploadRecord {
                        LocalPath = mappingPath,
                        RemotePath = "mapping.json",
                        ContentSha1 = mappingResult.ContentSha1 ?? "",
                        ContentLength = mappingResult.ContentLength,
                        UploadedAt = DateTime.UtcNow,
                        CdnUrl = mappingResult.CdnUrl ?? "",
                        Status = "Uploaded",
                        FileId = mappingResult.FileId,
                        ProjectName = projectName
                    };
                    await uploadStateService.SaveUploadAsync(mappingRecord);
                    return 1;
                }
            } catch (Exception ex) {
                logger.Warn(ex, "Failed to upload mapping.json");
            }

            return 0;
        }

        /// <summary>
        /// Saves upload records to SQLite and updates resource statuses in UI.
        /// </summary>
        private async Task SaveUploadRecordsAndUpdateStatusesAsync(
            Upload.B2BatchUploadResult uploadResult,
            string serverPath,
            string projectName,
            Data.UploadStateService uploadStateService) {

            // Read mapping.json for ResourceId lookup
            var mappingPath = Path.Combine(serverPath, "mapping.json");
            if (!File.Exists(mappingPath)) {
                logger.Warn($"[SaveUploadRecords] mapping.json not found at: {mappingPath}");
                return;
            }

            Export.MappingData? mapping;
            try {
                var json = await File.ReadAllTextAsync(mappingPath);
                mapping = Newtonsoft.Json.JsonConvert.DeserializeObject<Export.MappingData>(json);
            } catch (Exception ex) {
                logger.Error(ex, $"[SaveUploadRecords] Failed to parse mapping.json");
                return;
            }

            if (mapping == null) return;

            // Build reverse index: relativePath -> (resourceId, resourceType)
            var pathToResource = BuildPathToResourceIndex(mapping);

            // Save upload records and collect uploaded resource info
            var uploadedResources = new Dictionary<string, Dictionary<int, (string CdnUrl, string Hash)>> {
                ["Model"] = new(),
                ["Material"] = new(),
                ["Texture"] = new()
            };

            int savedCount = 0;
            int matchedCount = 0;

            foreach (var fileResult in uploadResult.Results.Where(r => r.Success || r.Skipped)) {
                var remotePath = fileResult.RemotePath?.Replace('\\', '/') ?? "";

                // Normalize path for mapping.json matching
                var relativePath = remotePath;
                if (relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase)) {
                    relativePath = "assets/" + relativePath;
                }

                int? resourceId = null;
                string? resourceType = null;

                if (pathToResource.TryGetValue(relativePath, out var resourceInfo)) {
                    resourceId = resourceInfo.ResourceId;
                    resourceType = resourceInfo.ResourceType;
                    matchedCount++;
                } else if (pathToResource.TryGetValue(remotePath, out resourceInfo)) {
                    resourceId = resourceInfo.ResourceId;
                    resourceType = resourceInfo.ResourceType;
                    matchedCount++;
                }

                var record = new Data.UploadRecord {
                    LocalPath = fileResult.LocalPath ?? "",
                    RemotePath = remotePath,
                    ContentSha1 = fileResult.ContentSha1 ?? "",
                    ContentLength = fileResult.ContentLength,
                    UploadedAt = DateTime.UtcNow,
                    CdnUrl = fileResult.CdnUrl ?? "",
                    Status = "Uploaded",
                    FileId = fileResult.FileId,
                    ProjectName = projectName,
                    ResourceId = resourceId,
                    ResourceType = resourceType
                };

                try {
                    await uploadStateService.SaveUploadAsync(record);
                    savedCount++;
                } catch (Exception ex) {
                    logger.Error(ex, $"[SaveUploadRecords] Failed to save record for: {remotePath}");
                }

                if (resourceId.HasValue && resourceType != null) {
                    uploadedResources[resourceType][resourceId.Value] = (fileResult.CdnUrl ?? "", fileResult.ContentSha1 ?? "");
                }
            }

            logger.Info($"[SaveUploadRecords] Saved {savedCount} records, matched {matchedCount} to resources");

            // Update resource statuses in UI
            Dispatcher.Invoke(() => {
                UpdateResourceUploadStatuses(uploadedResources["Model"], viewModel.Models);
                UpdateResourceUploadStatuses(uploadedResources["Material"], viewModel.Materials);
                UpdateResourceUploadStatuses(uploadedResources["Texture"], viewModel.Textures);
            });
        }

        /// <summary>
        /// Builds a reverse index from mapping.json paths to resource IDs.
        /// </summary>
        private static Dictionary<string, (int ResourceId, string ResourceType)> BuildPathToResourceIndex(Export.MappingData mapping) {
            var index = new Dictionary<string, (int ResourceId, string ResourceType)>(StringComparer.OrdinalIgnoreCase);

            static string Normalize(string p) => p.Replace('\\', '/').Replace("//", "/");

            if (mapping.Models != null) {
                foreach (var (idStr, entry) in mapping.Models) {
                    if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(entry.Path)) {
                        index[Normalize(entry.Path)] = (id, "Model");
                        foreach (var lod in entry.Lods) {
                            if (!string.IsNullOrEmpty(lod.File)) {
                                index[Normalize(lod.File)] = (id, "Model");
                            }
                        }
                    }
                }
            }

            if (mapping.Materials != null) {
                foreach (var (idStr, path) in mapping.Materials) {
                    if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(path)) {
                        index[Normalize(path)] = (id, "Material");
                    }
                }
            }

            if (mapping.Textures != null) {
                foreach (var (idStr, path) in mapping.Textures) {
                    if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(path)) {
                        index[Normalize(path)] = (id, "Texture");
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Updates upload status properties on resources that were successfully uploaded.
        /// </summary>
        private static void UpdateResourceUploadStatuses<T>(
            Dictionary<int, (string CdnUrl, string Hash)> uploadedItems,
            System.Collections.ObjectModel.ObservableCollection<T> resources) where T : BaseResource {

            foreach (var (resourceId, info) in uploadedItems) {
                var resource = resources.FirstOrDefault(r => r.ID == resourceId);
                if (resource != null) {
                    resource.UploadStatus = "Uploaded";
                    resource.LastUploadedAt = DateTime.UtcNow;
                    resource.RemoteUrl = info.CdnUrl;
                    resource.UploadedHash = info.Hash;
                }
            }
        }
    }
}

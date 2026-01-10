using System.IO;
using System.Security.Cryptography;
using AssetProcessor.Data;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.Upload;
using NLog;

namespace AssetProcessor.Services;

/// <summary>
/// Координатор загрузки ассетов на Backblaze B2
/// </summary>
public class AssetUploadCoordinator : IAssetUploadCoordinator {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IB2UploadService _b2Service;
    private readonly IUploadStateService _uploadStateService;

    public event EventHandler<ResourceStatusChangedEventArgs>? ResourceStatusChanged;
    public event EventHandler<UploadProgressEventArgs>? UploadProgressChanged;

    public AssetUploadCoordinator(IB2UploadService b2Service, IUploadStateService uploadStateService) {
        _b2Service = b2Service ?? throw new ArgumentNullException(nameof(b2Service));
        _uploadStateService = uploadStateService ?? throw new ArgumentNullException(nameof(uploadStateService));
    }

    public bool IsAuthorized => _b2Service.IsAuthorized;

    public async Task<bool> InitializeAsync(CancellationToken ct = default) {
        // Initialize upload state database
        try {
            await _uploadStateService.InitializeAsync(ct);
            Logger.Info("Upload state database initialized");
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to initialize upload state database");
            // Continue anyway - persistence is optional
        }

        var keyId = AppSettings.Default.B2KeyId;

        if (string.IsNullOrEmpty(keyId)) {
            Logger.Warn("Backblaze credentials not configured");
            return false;
        }

        if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
            Logger.Warn("Backblaze application key not configured or could not be decrypted");
            return false;
        }

        var settings = new B2UploadSettings {
            KeyId = keyId,
            ApplicationKey = appKey,
            BucketName = AppSettings.Default.B2BucketName,
            BucketId = AppSettings.Default.B2BucketId,
            PathPrefix = AppSettings.Default.B2PathPrefix,
            CdnBaseUrl = AppSettings.Default.CdnBaseUrl,
            MaxConcurrentUploads = AppSettings.Default.B2MaxConcurrentUploads
        };

        return await _b2Service.AuthorizeAsync(settings, ct);
    }

    public async Task<UploadResult> UploadResourceAsync(
        BaseResource resource,
        string projectName,
        string? modelName,
        CancellationToken ct = default) {

        if (resource.Path == null || !File.Exists(resource.Path)) {
            return new UploadResult(false, null, null, null, null, 0, "File not found");
        }

        // Build remote path: project/model/textures/filename or project/filename
        var remotePath = BuildRemotePath(resource.Path, projectName, modelName);

        // Check if needs upload by hash
        var currentHash = ComputeFileHash(resource.Path);
        if (resource.UploadedHash == currentHash) {
            resource.UploadStatus = "Uploaded";
            return new UploadResult(true, null, remotePath, resource.RemoteUrl, currentHash, 0, "Already uploaded (hash match)");
        }

        // Set status
        resource.UploadStatus = "Uploading";
        resource.UploadProgress = 0;
        OnResourceStatusChanged(resource, "Uploading");

        var result = await _b2Service.UploadFileAsync(resource.Path, remotePath, null, ct);

        if (result.Success) {
            resource.UploadStatus = result.Skipped ? "Uploaded" : "Uploaded";
            resource.UploadedHash = result.ContentSha1 ?? currentHash;
            resource.RemoteUrl = result.CdnUrl;
            resource.LastUploadedAt = DateTime.UtcNow;
            resource.UploadProgress = 100;
            OnResourceStatusChanged(resource, "Uploaded");
            Logger.Info($"Uploaded: {resource.Name} -> {remotePath}");

            // Save to persistence
            try {
                await SaveUploadRecordAsync(resource, remotePath, result, projectName, ct);
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to save upload record to database");
            }

            return new UploadResult(true, result.FileId, remotePath, result.CdnUrl, result.ContentSha1, result.ContentLength);
        } else {
            resource.UploadStatus = "Upload Failed";
            resource.UploadProgress = 0;
            OnResourceStatusChanged(resource, "Upload Failed");
            Logger.Error($"Upload failed: {resource.Name} - {result.ErrorMessage}");

            // Save failed record for reference
            try {
                await SaveFailedUploadRecordAsync(resource, remotePath, result.ErrorMessage, projectName, ct);
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to save failed upload record");
            }

            return new UploadResult(false, null, remotePath, null, null, 0, result.ErrorMessage);
        }
    }

    private async Task SaveUploadRecordAsync(
        BaseResource resource,
        string remotePath,
        B2UploadResult result,
        string projectName,
        CancellationToken ct) {

        var record = new UploadRecord {
            LocalPath = resource.Path!,
            RemotePath = remotePath,
            ContentSha1 = result.ContentSha1 ?? resource.UploadedHash ?? "",
            ContentLength = result.ContentLength,
            UploadedAt = DateTime.UtcNow,
            CdnUrl = result.CdnUrl ?? "",
            Status = "Uploaded",
            FileId = result.FileId,
            ProjectName = projectName
        };

        await _uploadStateService.SaveUploadAsync(record, ct);
    }

    private async Task SaveFailedUploadRecordAsync(
        BaseResource resource,
        string remotePath,
        string? errorMessage,
        string projectName,
        CancellationToken ct) {

        var record = new UploadRecord {
            LocalPath = resource.Path!,
            RemotePath = remotePath,
            ContentSha1 = "",
            ContentLength = 0,
            UploadedAt = DateTime.UtcNow,
            CdnUrl = "",
            Status = "Failed",
            ProjectName = projectName,
            ErrorMessage = errorMessage
        };

        await _uploadStateService.SaveUploadAsync(record, ct);
    }

    public async Task<AssetUploadResult> UploadResourcesAsync(
        IEnumerable<BaseResource> resources,
        string projectName,
        string? modelName,
        CancellationToken ct = default) {

        var resourceList = resources.ToList();
        var results = new List<UploadResult>();
        int uploaded = 0, skipped = 0, failed = 0;

        // Mark all as queued first
        foreach (var resource in resourceList) {
            if (string.IsNullOrEmpty(resource.Path) || !File.Exists(resource.Path)) {
                continue;
            }

            // Check if needs upload
            if (!ShouldUpload(resource)) {
                resource.UploadStatus = "Uploaded";
                skipped++;
            } else {
                resource.UploadStatus = "Queued";
                OnResourceStatusChanged(resource, "Queued");
            }
        }

        // Upload queued resources
        int processed = 0;
        foreach (var resource in resourceList) {
            if (ct.IsCancellationRequested) break;

            if (resource.UploadStatus != "Queued") {
                processed++;
                continue;
            }

            OnUploadProgressChanged(processed, resourceList.Count, resource, 0);

            var result = await UploadResourceAsync(resource, projectName, modelName, ct);
            results.Add(result);

            if (result.Success) {
                uploaded++;
            } else {
                failed++;
            }

            processed++;
            OnUploadProgressChanged(processed, resourceList.Count, resource, 100);
        }

        var message = $"Uploaded: {uploaded}, Skipped: {skipped}, Failed: {failed}";
        Logger.Info($"Upload batch complete. {message}");

        return new AssetUploadResult(
            failed == 0,
            uploaded,
            skipped,
            failed,
            message,
            results);
    }

    public async Task<AssetUploadResult> UploadModelExportAsync(
        string exportPath,
        string projectName,
        string modelName,
        CancellationToken ct = default) {

        if (!Directory.Exists(exportPath)) {
            return new AssetUploadResult(false, 0, 0, 1, "Export directory not found", Array.Empty<UploadResult>());
        }

        // Build list of files to upload
        var files = Directory.GetFiles(exportPath, "*", SearchOption.AllDirectories)
            .Where(f => IsUploadableFile(f))
            .Select(f => {
                var relativePath = Path.GetRelativePath(exportPath, f).Replace('\\', '/');
                return (LocalPath: f, RemotePath: $"{projectName}/{modelName}/{relativePath}");
            })
            .ToList();

        Logger.Info($"Uploading {files.Count} files from {exportPath}");

        var progress = new Progress<B2UploadProgress>(p => {
            OnUploadProgressChanged(p.CurrentFileIndex, p.TotalFiles, null, p.PercentComplete);
        });

        var batchResult = await _b2Service.UploadBatchAsync(files, progress, ct);

        var results = batchResult.Results.Select(r => new UploadResult(
            r.Success,
            r.FileId,
            r.RemotePath,
            r.CdnUrl,
            r.ContentSha1,
            r.ContentLength,
            r.ErrorMessage
        )).ToList();

        var message = $"Model export upload complete. Uploaded: {batchResult.SuccessCount}, Skipped: {batchResult.SkippedCount}, Failed: {batchResult.FailedCount}";
        Logger.Info(message);

        return new AssetUploadResult(
            batchResult.Success,
            batchResult.SuccessCount,
            batchResult.SkippedCount,
            batchResult.FailedCount,
            message,
            results);
    }

    public bool ShouldUpload(BaseResource resource) {
        if (string.IsNullOrEmpty(resource.Path) || !File.Exists(resource.Path)) {
            return false;
        }

        if (string.IsNullOrEmpty(resource.UploadedHash)) {
            return true;
        }

        var currentHash = ComputeFileHash(resource.Path);
        return !string.Equals(currentHash, resource.UploadedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет нужна ли загрузка, используя persistence database
    /// </summary>
    public async Task<bool> ShouldUploadAsync(BaseResource resource, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(resource.Path) || !File.Exists(resource.Path)) {
            return false;
        }

        var currentHash = ComputeFileHash(resource.Path);

        // First check in-memory
        if (!string.IsNullOrEmpty(resource.UploadedHash) &&
            string.Equals(currentHash, resource.UploadedHash, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // Then check persistence
        try {
            var isUploaded = await _uploadStateService.IsUploadedAsync(resource.Path, currentHash, ct);
            return !isUploaded;
        } catch (Exception ex) {
            Logger.Warn(ex, "Failed to check upload state from database");
            return true; // Assume needs upload if database check fails
        }
    }

    /// <summary>
    /// Восстанавливает состояние загрузки для ресурса из базы данных
    /// </summary>
    public async Task RestoreUploadStateAsync(BaseResource resource, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(resource.Path)) return;

        try {
            var record = await _uploadStateService.GetByLocalPathAsync(resource.Path, ct);
            if (record != null && record.Status == "Uploaded") {
                resource.UploadedHash = record.ContentSha1;
                resource.RemoteUrl = record.CdnUrl;
                resource.LastUploadedAt = record.UploadedAt;
                resource.UploadStatus = "Uploaded";

                // Check if file has changed since last upload
                var currentHash = ComputeFileHash(resource.Path);
                if (!string.Equals(currentHash, record.ContentSha1, StringComparison.OrdinalIgnoreCase)) {
                    resource.UploadStatus = "Outdated";
                }
            }
        } catch (Exception ex) {
            Logger.Warn(ex, $"Failed to restore upload state for {resource.Path}");
        }
    }

    /// <summary>
    /// Восстанавливает состояние загрузки для коллекции ресурсов
    /// </summary>
    public async Task RestoreUploadStatesAsync(IEnumerable<BaseResource> resources, CancellationToken ct = default) {
        foreach (var resource in resources) {
            await RestoreUploadStateAsync(resource, ct);
        }
    }

    /// <summary>
    /// Получает количество записей в базе
    /// </summary>
    public async Task<int> GetUploadRecordCountAsync(CancellationToken ct = default) {
        try {
            return await _uploadStateService.GetCountAsync(ct);
        } catch (Exception ex) {
            Logger.Debug(ex, "Failed to get upload record count from database");
            return 0;
        }
    }

    /// <summary>
    /// Получает записи с пагинацией для просмотра истории
    /// </summary>
    public async Task<IReadOnlyList<UploadRecord>> GetUploadHistoryAsync(
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default) {
        try {
            return await _uploadStateService.GetPageAsync(offset, limit, ct);
        } catch (Exception ex) {
            Logger.Debug(ex, "Failed to get upload history from database");
            return Array.Empty<UploadRecord>();
        }
    }

    public string ComputeFileHash(string filePath) {
        using var stream = File.OpenRead(filePath);
        var hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildRemotePath(string localPath, string projectName, string? modelName) {
        var fileName = Path.GetFileName(localPath);
        var ext = Path.GetExtension(localPath).ToLowerInvariant();

        // Determine subfolder based on file type
        string subFolder;
        if (ext == ".ktx2" || ext == ".png" || ext == ".jpg" || ext == ".jpeg") {
            subFolder = "textures";
        } else if (ext == ".glb" || ext == ".gltf") {
            subFolder = "models";
        } else if (ext == ".json") {
            subFolder = "materials";
        } else {
            subFolder = "assets";
        }

        if (!string.IsNullOrEmpty(modelName)) {
            return $"{projectName}/{modelName}/{subFolder}/{fileName}";
        }

        return $"{projectName}/{subFolder}/{fileName}";
    }

    private static bool IsUploadableFile(string filePath) {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch {
            ".ktx2" => true,
            ".glb" => true,
            ".gltf" => true,
            ".json" => true,
            ".bin" => true,
            ".png" => true,
            ".jpg" => true,
            ".jpeg" => true,
            _ => false
        };
    }

    private void OnResourceStatusChanged(BaseResource resource, string? status) {
        ResourceStatusChanged?.Invoke(this, new ResourceStatusChangedEventArgs(resource, status));
    }

    private void OnUploadProgressChanged(int completed, int total, BaseResource? resource, double progress) {
        UploadProgressChanged?.Invoke(this, new UploadProgressEventArgs(completed, total, resource, progress));
    }
}

using AssetProcessor.Data;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Upload;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class UploadWorkflowCoordinator : IUploadWorkflowCoordinator {

    public UploadValidationResult ValidateB2Configuration(string? keyId, string? bucketName) {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(bucketName)) {
            return new UploadValidationResult {
                IsValid = false,
                ErrorMessage = "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure."
            };
        }

        return new UploadValidationResult { IsValid = true };
    }

    public List<(TextureResource Texture, string Ktx2Path)> CollectConvertedTextures(IEnumerable<TextureResource> textures) {
        return textures
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .Select(t => {
                var sourceDir = Path.GetDirectoryName(t.Path!)!;
                var fileName = Path.GetFileNameWithoutExtension(t.Path);
                var ktx2Path = Path.Combine(sourceDir, fileName + ".ktx2");
                return (Texture: t, Ktx2Path: ktx2Path);
            })
            .Where(x => File.Exists(x.Ktx2Path))
            .ToList();
    }

    public List<(string LocalPath, string RemotePath)> BuildUploadFilePairs(
        IEnumerable<string> exportedFiles,
        string? serverPath,
        Action<string>? onMissingFile = null) {

        var filePairs = new List<(string LocalPath, string RemotePath)>();

        foreach (var localPath in exportedFiles) {
            if (!File.Exists(localPath)) {
                onMissingFile?.Invoke(localPath);
                continue;
            }

            string remotePath;
            if (!string.IsNullOrEmpty(serverPath) && localPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase)) {
                var relativePath = localPath.Substring(serverPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');

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

    public async Task<int> TryUploadMappingJsonAsync(
        IB2UploadService b2Service,
        IUploadStateService uploadStateService,
        string serverPath,
        string projectName,
        Action<string>? onInfo = null,
        Action<Exception, string>? onWarn = null) {

        var mappingPath = Path.Combine(serverPath, "mapping.json");
        if (!File.Exists(mappingPath)) return 0;

        try {
            var mappingResult = await b2Service.UploadFileAsync(mappingPath, "mapping.json", null);
            if (!mappingResult.Success) return 0;

            onInfo?.Invoke($"Uploaded mapping.json to {projectName}/mapping.json");

            var mappingRecord = new UploadRecord {
                LocalPath = mappingPath,
                RemotePath = "mapping.json",
                ContentSha1 = mappingResult.ContentSha1 ?? string.Empty,
                ContentLength = mappingResult.ContentLength,
                UploadedAt = DateTime.UtcNow,
                CdnUrl = mappingResult.CdnUrl ?? string.Empty,
                Status = "Uploaded",
                FileId = mappingResult.FileId,
                ProjectName = projectName
            };

            await uploadStateService.SaveUploadAsync(mappingRecord);
            return 1;
        } catch (Exception ex) {
            onWarn?.Invoke(ex, "Failed to upload mapping.json");
            return 0;
        }
    }


    public string BuildUploadResultMessage(B2BatchUploadResult result, int mappingUploaded, int maxErrorsToShow = 5) {
        var errorDetails = result.Errors.Count > 0
            ? "\n\nErrors:\n" + string.Join("\n", result.Errors.Take(maxErrorsToShow).Select(e => $"  • {e}"))
              + (result.Errors.Count > maxErrorsToShow ? $"\n  ...and {result.Errors.Count - maxErrorsToShow} more (see log)" : "")
            : string.Empty;

        return
            "Upload completed!\n\n" +
            $"Uploaded: {result.SuccessCount + mappingUploaded}\n" +
            $"Skipped (already exists): {result.SkippedCount}\n" +
            $"Failed: {result.FailedCount}\n" +
            (mappingUploaded > 0 ? "mapping.json: uploaded\n" : "") +
            $"Duration: {result.Duration.TotalSeconds:F1}s" +
            errorDetails;
    }

    public async Task<UploadStatusUpdates> SaveUploadRecordsAsync(
        B2BatchUploadResult uploadResult,
        string serverPath,
        string projectName,
        IUploadStateService uploadStateService,
        Action<string>? onInfo = null,
        Action<Exception, string>? onError = null) {

        var updates = new UploadStatusUpdates();
        var mappingPath = Path.Combine(serverPath, "mapping.json");
        if (!File.Exists(mappingPath)) {
            onInfo?.Invoke($"[SaveUploadRecords] mapping.json not found at: {mappingPath}");
            return updates;
        }

        Export.MappingData? mapping;
        try {
            var json = await File.ReadAllTextAsync(mappingPath);
            mapping = JsonConvert.DeserializeObject<Export.MappingData>(json);
        } catch (Exception ex) {
            onError?.Invoke(ex, "[SaveUploadRecords] Failed to parse mapping.json");
            return updates;
        }

        if (mapping == null) return updates;

        var pathToResource = BuildPathToResourceIndex(mapping);

        int savedCount = 0;
        int matchedCount = 0;

        foreach (var fileResult in uploadResult.Results.Where(r => r.Success || r.Skipped)) {
            var remotePath = fileResult.RemotePath?.Replace('\\', '/') ?? string.Empty;

            var relativePath = remotePath;
            if (relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase)) {
                relativePath = "assets/" + relativePath;
            }

            int? resourceId = null;
            string? resourceType = null;

            if (pathToResource.TryGetValue(relativePath, out var resourceInfo) ||
                pathToResource.TryGetValue(remotePath, out resourceInfo)) {
                resourceId = resourceInfo.ResourceId;
                resourceType = resourceInfo.ResourceType;
                matchedCount++;
            }

            var record = new UploadRecord {
                LocalPath = fileResult.LocalPath ?? string.Empty,
                RemotePath = remotePath,
                ContentSha1 = fileResult.ContentSha1 ?? string.Empty,
                ContentLength = fileResult.ContentLength,
                UploadedAt = DateTime.UtcNow,
                CdnUrl = fileResult.CdnUrl ?? string.Empty,
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
                onError?.Invoke(ex, $"[SaveUploadRecords] Failed to save record for: {remotePath}");
            }

            if (resourceId.HasValue && resourceType != null) {
                updates.GetMap(resourceType)[resourceId.Value] = (fileResult.CdnUrl ?? string.Empty, fileResult.ContentSha1 ?? string.Empty);
            }
        }

        onInfo?.Invoke($"[SaveUploadRecords] Saved {savedCount} records, matched {matchedCount} to resources");
        return updates;
    }

    public void ApplyUploadStatuses<T>(Dictionary<int, (string CdnUrl, string Hash)> uploadedItems, IEnumerable<T> resources) where T : BaseResource {
        var resourceMap = resources.ToDictionary(r => r.ID);
        foreach (var (resourceId, info) in uploadedItems) {
            if (!resourceMap.TryGetValue(resourceId, out var resource)) continue;

            resource.UploadStatus = "Uploaded";
            resource.LastUploadedAt = DateTime.UtcNow;
            resource.RemoteUrl = info.CdnUrl;
            resource.UploadedHash = info.Hash;
        }
    }


    public void ApplyAllUploadStatuses(
        UploadStatusUpdates updates,
        IEnumerable<ModelResource> models,
        IEnumerable<MaterialResource> materials,
        IEnumerable<TextureResource> textures) {

        ApplyUploadStatuses(updates.Models, models);
        ApplyUploadStatuses(updates.Materials, materials);
        ApplyUploadStatuses(updates.Textures, textures);
    }

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

        if (mapping.Textures == null) return index;

        foreach (var (idStr, path) in mapping.Textures) {
            if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(path)) {
                index[Normalize(path)] = (id, "Texture");
            }
        }

        return index;
    }
}

public sealed class UploadStatusUpdates {
    public Dictionary<int, (string CdnUrl, string Hash)> Models { get; } = new();
    public Dictionary<int, (string CdnUrl, string Hash)> Materials { get; } = new();
    public Dictionary<int, (string CdnUrl, string Hash)> Textures { get; } = new();

    public Dictionary<int, (string CdnUrl, string Hash)> GetMap(string resourceType) {
        return resourceType switch {
            "Model" => Models,
            "Material" => Materials,
            "Texture" => Textures,
            _ => throw new ArgumentException($"Unknown resource type: {resourceType}", nameof(resourceType))
        };
    }
}

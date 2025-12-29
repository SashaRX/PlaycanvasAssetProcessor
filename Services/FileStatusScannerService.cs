using AssetProcessor.Resources;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetProcessor.Services;

/// <summary>
/// Реализация сервиса сканирования статусов файлов.
/// </summary>
public class FileStatusScannerService : IFileStatusScannerService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ILogService _logService;

    // Статусы, указывающие что файл должен существовать локально
    private static readonly HashSet<string> LocalStatuses = new(StringComparer.OrdinalIgnoreCase) {
        "Downloaded", "Converted", "Size Mismatch", "Hash ERROR", "Empty File"
    };

    public event EventHandler<FilesDeletedEventArgs>? FilesDeleted;

    public FileStatusScannerService(ILogService logService) {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public bool IsLocalStatus(string? status) {
        return !string.IsNullOrEmpty(status) && LocalStatuses.Contains(status);
    }

    public ScanResult ScanTextures(IEnumerable<TextureResource> textures) {
        int updatedCount = 0;
        int checkedCount = 0;
        int missingFilesCount = 0;
        var updatedNames = new List<string>();

        foreach (var texture in textures) {
            // Пропускаем ORM текстуры - они виртуальные
            if (texture is ORMTextureResource) continue;
            if (string.IsNullOrEmpty(texture.Path)) continue;

            checkedCount++;
            string? currentStatus = texture.Status;
            bool fileExists = File.Exists(texture.Path);
            bool isLocalStatus = IsLocalStatus(currentStatus);

            if (!fileExists) {
                missingFilesCount++;
                logger.Debug($"Missing texture: '{texture.Name}', status='{currentStatus}'");
            }

            // Если статус указывает на локальный файл, но файла нет - обновляем на "On Server"
            if (isLocalStatus && !fileExists) {
                logger.Info($"Updating '{texture.Name}' from '{currentStatus}' to 'On Server'");
                texture.Status = "On Server";
                texture.CompressedSize = 0;
                texture.CompressionFormat = null;
                texture.MipmapCount = 0;
                updatedCount++;
                updatedNames.Add(texture.Name ?? "Unknown");
            }
        }

        return new ScanResult {
            CheckedCount = checkedCount,
            MissingFilesCount = missingFilesCount,
            UpdatedCount = updatedCount,
            UpdatedAssetNames = updatedNames
        };
    }

    public ScanResult ScanModels(IEnumerable<ModelResource> models) {
        int updatedCount = 0;
        int checkedCount = 0;
        int missingFilesCount = 0;
        var updatedNames = new List<string>();

        foreach (var model in models) {
            if (string.IsNullOrEmpty(model.Path)) continue;

            checkedCount++;
            string? currentStatus = model.Status;
            bool fileExists = File.Exists(model.Path);
            bool isLocalStatus = IsLocalStatus(currentStatus);

            if (!fileExists) {
                missingFilesCount++;
                logger.Debug($"Missing model: '{model.Name}', status='{currentStatus}'");
            }

            if (isLocalStatus && !fileExists) {
                logger.Info($"Updating '{model.Name}' from '{currentStatus}' to 'On Server'");
                model.Status = "On Server";
                updatedCount++;
                updatedNames.Add(model.Name ?? "Unknown");
            }
        }

        return new ScanResult {
            CheckedCount = checkedCount,
            MissingFilesCount = missingFilesCount,
            UpdatedCount = updatedCount,
            UpdatedAssetNames = updatedNames
        };
    }

    public ScanResult ScanAll(IEnumerable<TextureResource> textures, IEnumerable<ModelResource> models) {
        logger.Info($"ScanAll: Starting scan");

        var textureResult = ScanTextures(textures);
        var modelResult = ScanModels(models);

        int totalChecked = textureResult.CheckedCount + modelResult.CheckedCount;
        int totalMissing = textureResult.MissingFilesCount + modelResult.MissingFilesCount;
        int totalUpdated = textureResult.UpdatedCount + modelResult.UpdatedCount;

        logger.Info($"ScanAll: Checked {totalChecked} assets, {totalMissing} missing, updated {totalUpdated}");

        if (totalUpdated > 0) {
            _logService.LogInfo($"Updated {totalUpdated} assets to 'On Server' (files deleted)");
        }

        return new ScanResult {
            CheckedCount = totalChecked,
            MissingFilesCount = totalMissing,
            UpdatedCount = totalUpdated,
            UpdatedAssetNames = textureResult.UpdatedAssetNames.Concat(modelResult.UpdatedAssetNames).ToList()
        };
    }

    public int ProcessDeletedPaths(
        IEnumerable<string> deletedPaths,
        IEnumerable<TextureResource> textures,
        IEnumerable<ModelResource> models) {
        var deletedPathsSet = new HashSet<string>(deletedPaths, StringComparer.OrdinalIgnoreCase);
        int updatedCount = 0;

        if (deletedPathsSet.Count == 0) {
            return 0;
        }

        logger.Info($"Processing {deletedPathsSet.Count} deleted paths");

        // Обновляем текстуры
        foreach (var texture in textures) {
            if (texture is ORMTextureResource) continue;
            if (string.IsNullOrEmpty(texture.Path)) continue;

            if (deletedPathsSet.Contains(texture.Path) && IsLocalStatus(texture.Status)) {
                logger.Info($"File deleted: '{texture.Name}' -> 'On Server'");
                texture.Status = "On Server";
                texture.CompressedSize = 0;
                texture.CompressionFormat = null;
                texture.MipmapCount = 0;
                updatedCount++;
            }
        }

        // Обновляем модели
        foreach (var model in models) {
            if (string.IsNullOrEmpty(model.Path)) continue;

            if (deletedPathsSet.Contains(model.Path) && IsLocalStatus(model.Status)) {
                logger.Info($"File deleted: '{model.Name}' -> 'On Server'");
                model.Status = "On Server";
                updatedCount++;
            }
        }

        if (updatedCount > 0) {
            _logService.LogInfo($"Detected {updatedCount} deleted files, updated statuses");
            FilesDeleted?.Invoke(this, new FilesDeletedEventArgs {
                DeletedPaths = deletedPathsSet.ToList(),
                UpdatedAssetsCount = updatedCount
            });
        }

        return updatedCount;
    }
}

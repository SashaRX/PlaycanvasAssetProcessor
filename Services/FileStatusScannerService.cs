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

            if (isLocalStatus && !fileExists) {
                logger.Info($"Updating '{texture.Name}' from '{currentStatus}' to 'On Server'");
                texture.Status = "On Server";
                texture.CompressedSize = 0;
                texture.CompressionFormat = null;
                texture.MipmapCount = 0;
                updatedCount++;
                updatedNames.Add(texture.Name ?? "Unknown");
            } else if (fileExists && string.Equals(currentStatus, "On Server", StringComparison.OrdinalIgnoreCase)) {
                logger.Info($"Updating '{texture.Name}' from '{currentStatus}' to 'Downloaded'");
                texture.Status = "Downloaded";
                if (texture.Size <= 0) {
                    texture.Size = (int)new FileInfo(texture.Path).Length;
                }
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
            } else if (fileExists && string.Equals(currentStatus, "On Server", StringComparison.OrdinalIgnoreCase)) {
                logger.Info($"Updating '{model.Name}' from '{currentStatus}' to 'Downloaded'");
                model.Status = "Downloaded";
                if (model.Size <= 0) {
                    model.Size = (int)new FileInfo(model.Path).Length;
                }
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

    public ScanResult ScanMaterials(IEnumerable<MaterialResource> materials) {
        int updatedCount = 0;
        int checkedCount = 0;
        int missingFilesCount = 0;
        var updatedNames = new List<string>();

        foreach (var material in materials) {
            if (string.IsNullOrEmpty(material.MaterialJsonPath)) continue;

            checkedCount++;
            string? currentStatus = material.Status;
            bool fileExists = File.Exists(material.MaterialJsonPath);
            bool isLocalStatus = IsLocalStatus(currentStatus);

            if (!fileExists) {
                missingFilesCount++;
                logger.Debug($"Missing material: '{material.Name}', path='{material.MaterialJsonPath}', status='{currentStatus}'");
            }

            if (isLocalStatus && !fileExists) {
                logger.Info($"Updating material '{material.Name}' from '{currentStatus}' to 'On Server'");
                material.Status = "On Server";
                updatedCount++;
                updatedNames.Add(material.Name ?? "Unknown");
            } else if (fileExists && string.Equals(currentStatus, "On Server", StringComparison.OrdinalIgnoreCase)) {
                logger.Info($"Updating material '{material.Name}' from '{currentStatus}' to 'Downloaded'");
                material.Status = "Downloaded";
                updatedCount++;
                updatedNames.Add(material.Name ?? "Unknown");
            }
        }

        return new ScanResult {
            CheckedCount = checkedCount,
            MissingFilesCount = missingFilesCount,
            UpdatedCount = updatedCount,
            UpdatedAssetNames = updatedNames
        };
    }

    public ScanResult ScanAll(IEnumerable<TextureResource> textures, IEnumerable<ModelResource> models, IEnumerable<MaterialResource> materials) {
        logger.Info($"ScanAll: Starting scan");

        var textureResult = ScanTextures(textures);
        var modelResult = ScanModels(models);
        var materialResult = ScanMaterials(materials);

        int totalChecked = textureResult.CheckedCount + modelResult.CheckedCount + materialResult.CheckedCount;
        int totalMissing = textureResult.MissingFilesCount + modelResult.MissingFilesCount + materialResult.MissingFilesCount;
        int totalUpdated = textureResult.UpdatedCount + modelResult.UpdatedCount + materialResult.UpdatedCount;

        logger.Info($"ScanAll: Checked {totalChecked} assets, {totalMissing} missing, updated {totalUpdated}");

        if (totalUpdated > 0) {
            _logService.LogInfo($"Updated {totalUpdated} assets statuses based on file presence");
        }

        return new ScanResult {
            CheckedCount = totalChecked,
            MissingFilesCount = totalMissing,
            UpdatedCount = totalUpdated,
            UpdatedAssetNames = textureResult.UpdatedAssetNames
                .Concat(modelResult.UpdatedAssetNames)
                .Concat(materialResult.UpdatedAssetNames)
                .ToList()
        };
    }

    public int ProcessDeletedPaths(
        IEnumerable<string> deletedPaths,
        IEnumerable<TextureResource> textures,
        IEnumerable<ModelResource> models,
        IEnumerable<MaterialResource> materials) {
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

        // Обновляем материалы
        foreach (var material in materials) {
            if (string.IsNullOrEmpty(material.MaterialJsonPath)) continue;

            if (deletedPathsSet.Contains(material.MaterialJsonPath) && IsLocalStatus(material.Status)) {
                logger.Info($"Material file deleted: '{material.Name}' -> 'On Server'");
                material.Status = "On Server";
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

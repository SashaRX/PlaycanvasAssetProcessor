using System.IO;
using System.Text.Json;
using AssetProcessor.Mapping.Models;
using NLog;

namespace AssetProcessor.Mapping;

/// <summary>
/// Валидатор маппинга для проверки целостности всех ссылок
/// </summary>
public class MappingValidator {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Полная валидация маппинга
    /// </summary>
    /// <param name="manifest">Маппинг для валидации</param>
    /// <param name="basePath">Базовый путь для проверки файлов на диске</param>
    /// <param name="checkFilesOnDisk">Проверять ли существование файлов на диске</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат валидации</returns>
    public async Task<MappingValidationResult> ValidateAsync(
        MappingManifest manifest,
        string basePath,
        bool checkFilesOnDisk = true,
        CancellationToken cancellationToken = default) {

        var result = new MappingValidationResult();

        Logger.Info("=== Starting mapping validation ===");

        // 1. Валидация ссылок материалов в моделях
        ValidateModelMaterialReferences(manifest, result);

        // 2. Валидация LOD уровней
        ValidateLodLevels(manifest, result);

        // 3. Валидация текстур в материалах
        await ValidateMaterialTextureReferencesAsync(manifest, basePath, result, cancellationToken);

        // 4. Проверка файлов на диске
        if (checkFilesOnDisk) {
            ValidateFilesOnDisk(manifest, basePath, result);
        }

        // 5. Проверка неиспользуемых ресурсов
        ValidateUnusedResources(manifest, result);

        // 6. Проверка дубликатов
        ValidateDuplicates(manifest, result);

        result.IsValid = result.Errors.Count == 0;

        Logger.Info($"Validation complete: {result.Errors.Count} errors, {result.Warnings.Count} warnings");
        Logger.Info($"Stats: Models={result.Stats.ModelsChecked}, Materials={result.Stats.MaterialsChecked}, " +
                   $"Textures={result.Stats.TexturesChecked}, LOD files={result.Stats.LodFilesChecked}");

        return result;
    }

    /// <summary>
    /// Быстрая валидация только ссылок (без проверки файлов)
    /// </summary>
    public MappingValidationResult ValidateReferencesOnly(MappingManifest manifest) {
        var result = new MappingValidationResult();

        ValidateModelMaterialReferences(manifest, result);
        ValidateLodLevels(manifest, result);
        ValidateUnusedResources(manifest, result);
        ValidateDuplicates(manifest, result);

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    #region Validation Methods

    private void ValidateModelMaterialReferences(MappingManifest manifest, MappingValidationResult result) {
        var usedMaterialIds = new HashSet<string>();

        foreach (var (modelId, model) in manifest.Models) {
            result.Stats.ModelsChecked++;

            foreach (var materialId in model.Materials) {
                var materialIdStr = materialId.ToString();
                usedMaterialIds.Add(materialIdStr);

                if (!manifest.Materials.ContainsKey(materialIdStr)) {
                    result.Errors.Add(new ValidationError {
                        Type = ValidationErrorType.MaterialNotFound,
                        Message = $"Model '{model.Name}' (ID: {modelId}) references material ID {materialId} which is not found in materials",
                        AssetId = materialId
                    });
                    result.Stats.InvalidReferences++;
                } else {
                    result.Stats.ValidReferences++;
                }
            }
        }
    }

    private void ValidateLodLevels(MappingManifest manifest, MappingValidationResult result) {
        foreach (var (modelId, model) in manifest.Models) {
            if (model.Lods.Count == 0) {
                result.Warnings.Add(new ValidationWarning {
                    Type = ValidationWarningType.NoLodLevels,
                    Message = $"Model '{model.Name}' (ID: {modelId}) has no LOD levels",
                    AssetId = int.TryParse(modelId, out var id) ? id : null
                });
                continue;
            }

            // Проверяем последовательность LOD уровней
            var levels = model.Lods.Select(l => l.Level).OrderBy(l => l).ToList();
            for (int i = 0; i < levels.Count - 1; i++) {
                if (levels[i + 1] - levels[i] > 1) {
                    result.Warnings.Add(new ValidationWarning {
                        Type = ValidationWarningType.MissingIntermediateLod,
                        Message = $"Model '{model.Name}' is missing LOD{levels[i] + 1} between LOD{levels[i]} and LOD{levels[i + 1]}",
                        AssetId = int.TryParse(modelId, out var id) ? id : null
                    });
                }
            }

            // Проверяем корректность дистанций
            var distances = model.Lods.OrderBy(l => l.Level).Select(l => l.Distance).ToList();
            for (int i = 0; i < distances.Count - 1; i++) {
                if (distances[i] >= distances[i + 1]) {
                    result.Errors.Add(new ValidationError {
                        Type = ValidationErrorType.InvalidFileFormat,
                        Message = $"Model '{model.Name}' has invalid LOD distances: LOD{i} distance ({distances[i]}) >= LOD{i + 1} distance ({distances[i + 1]})",
                        AssetId = int.TryParse(modelId, out var id) ? id : null
                    });
                }
            }

            result.Stats.LodFilesChecked += model.Lods.Count;
        }
    }

    private async Task ValidateMaterialTextureReferencesAsync(
        MappingManifest manifest,
        string basePath,
        MappingValidationResult result,
        CancellationToken cancellationToken) {

        foreach (var (materialId, materialPath) in manifest.Materials) {
            result.Stats.MaterialsChecked++;

            var fullPath = Path.Combine(basePath, materialPath);
            if (!File.Exists(fullPath)) {
                continue; // Будет обработано в ValidateFilesOnDisk
            }

            try {
                var json = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var materialJson = JsonSerializer.Deserialize<MaterialInstanceJson>(json, JsonOptions);

                if (materialJson?.Textures == null) continue;

                var textureIds = ExtractTextureIds(materialJson.Textures);
                bool hasAnyTexture = false;

                foreach (var textureId in textureIds) {
                    hasAnyTexture = true;
                    var textureIdStr = textureId.ToString();

                    if (!manifest.Textures.ContainsKey(textureIdStr)) {
                        result.Errors.Add(new ValidationError {
                            Type = ValidationErrorType.TextureNotFound,
                            Message = $"Material {materialId} references texture ID {textureId} which is not found in textures",
                            AssetId = textureId
                        });
                        result.Stats.InvalidReferences++;
                    } else {
                        result.Stats.ValidReferences++;
                    }
                }

                if (!hasAnyTexture) {
                    result.Warnings.Add(new ValidationWarning {
                        Type = ValidationWarningType.NoTextures,
                        Message = $"Material {materialId} has no texture references",
                        AssetId = int.TryParse(materialId, out var id) ? id : null
                    });
                }

            } catch (Exception ex) {
                Logger.Warn(ex, $"Failed to parse material JSON: {fullPath}");
            }
        }
    }

    private void ValidateFilesOnDisk(MappingManifest manifest, string basePath, MappingValidationResult result) {
        // Проверяем LOD файлы
        foreach (var (modelId, model) in manifest.Models) {
            foreach (var lod in model.Lods) {
                var lodPath = Path.Combine(basePath, lod.File);
                if (!File.Exists(lodPath)) {
                    result.Errors.Add(new ValidationError {
                        Type = ValidationErrorType.LodFileNotFound,
                        Message = $"LOD file not found: {lod.File}",
                        AssetId = int.TryParse(modelId, out var id) ? id : null,
                        FilePath = lodPath
                    });
                    result.Stats.InvalidReferences++;
                } else {
                    result.Stats.ValidReferences++;
                }
            }
        }

        // Проверяем material JSON файлы
        foreach (var (materialId, materialPath) in manifest.Materials) {
            var fullPath = Path.Combine(basePath, materialPath);
            if (!File.Exists(fullPath)) {
                result.Errors.Add(new ValidationError {
                    Type = ValidationErrorType.MaterialFileNotFound,
                    Message = $"Material JSON file not found: {materialPath}",
                    AssetId = int.TryParse(materialId, out var id) ? id : null,
                    FilePath = fullPath
                });
                result.Stats.InvalidReferences++;
            } else {
                result.Stats.ValidReferences++;
            }
        }

        // Проверяем texture файлы
        foreach (var (textureId, texturePath) in manifest.Textures) {
            result.Stats.TexturesChecked++;
            var fullPath = Path.Combine(basePath, texturePath);
            if (!File.Exists(fullPath)) {
                result.Errors.Add(new ValidationError {
                    Type = ValidationErrorType.TextureFileNotFound,
                    Message = $"Texture file not found: {texturePath}",
                    AssetId = int.TryParse(textureId, out var id) ? id : null,
                    FilePath = fullPath
                });
                result.Stats.InvalidReferences++;
            } else {
                result.Stats.ValidReferences++;
            }
        }
    }

    private void ValidateUnusedResources(MappingManifest manifest, MappingValidationResult result) {
        // Собираем все используемые материалы
        var usedMaterialIds = new HashSet<string>();
        foreach (var model in manifest.Models.Values) {
            foreach (var materialId in model.Materials) {
                usedMaterialIds.Add(materialId.ToString());
            }
        }

        // Находим неиспользуемые материалы
        foreach (var materialId in manifest.Materials.Keys) {
            if (!usedMaterialIds.Contains(materialId)) {
                result.Warnings.Add(new ValidationWarning {
                    Type = ValidationWarningType.UnusedMaterial,
                    Message = $"Material ID {materialId} is not referenced by any model",
                    AssetId = int.TryParse(materialId, out var id) ? id : null
                });
            }
        }

        // Собираем все используемые текстуры (из material JSON файлов - уже проверено выше)
        // Для полной проверки нужно было бы парсить все material JSON
    }

    private void ValidateDuplicates(MappingManifest manifest, MappingValidationResult result) {
        // Проверяем дублирующиеся пути файлов
        var modelPaths = new Dictionary<string, List<string>>();
        foreach (var (modelId, model) in manifest.Models) {
            foreach (var lod in model.Lods) {
                if (!modelPaths.ContainsKey(lod.File)) {
                    modelPaths[lod.File] = new List<string>();
                }
                modelPaths[lod.File].Add(modelId);
            }
        }

        foreach (var (path, ids) in modelPaths) {
            if (ids.Count > 1) {
                result.Errors.Add(new ValidationError {
                    Type = ValidationErrorType.DuplicateId,
                    Message = $"LOD file '{path}' is referenced by multiple models: {string.Join(", ", ids)}"
                });
            }
        }

        // Проверяем дублирующиеся пути текстур
        var texturePaths = new Dictionary<string, List<string>>();
        foreach (var (textureId, path) in manifest.Textures) {
            if (!texturePaths.ContainsKey(path)) {
                texturePaths[path] = new List<string>();
            }
            texturePaths[path].Add(textureId);
        }

        foreach (var (path, ids) in texturePaths) {
            if (ids.Count > 1) {
                result.Warnings.Add(new ValidationWarning {
                    Type = ValidationWarningType.UnusedTexture, // Reusing as "duplicate path"
                    Message = $"Texture path '{path}' is mapped to multiple IDs: {string.Join(", ", ids)}"
                });
            }
        }
    }

    private IEnumerable<int> ExtractTextureIds(MaterialTextures textures) {
        var ids = new List<int>();

        if (textures.DiffuseMap.HasValue) ids.Add(textures.DiffuseMap.Value);
        if (textures.NormalMap.HasValue) ids.Add(textures.NormalMap.Value);
        if (textures.SpecularMap.HasValue) ids.Add(textures.SpecularMap.Value);
        if (textures.GlossMap.HasValue) ids.Add(textures.GlossMap.Value);
        if (textures.MetalnessMap.HasValue) ids.Add(textures.MetalnessMap.Value);
        if (textures.AoMap.HasValue) ids.Add(textures.AoMap.Value);
        if (textures.EmissiveMap.HasValue) ids.Add(textures.EmissiveMap.Value);
        if (textures.OpacityMap.HasValue) ids.Add(textures.OpacityMap.Value);
        if (textures.OgMap != null) ids.Add(textures.OgMap.Asset);
        if (textures.OgmMap != null) ids.Add(textures.OgmMap.Asset);
        if (textures.OgmhMap != null) ids.Add(textures.OgmhMap.Asset);

        return ids;
    }

    #endregion

    /// <summary>
    /// Загружает и валидирует маппинг из файла
    /// </summary>
    public async Task<MappingValidationResult> ValidateFromFileAsync(
        string mappingFilePath,
        CancellationToken cancellationToken = default) {

        if (!File.Exists(mappingFilePath)) {
            return new MappingValidationResult {
                IsValid = false,
                Errors = new List<ValidationError> {
                    new() {
                        Type = ValidationErrorType.InvalidFileFormat,
                        Message = $"Mapping file not found: {mappingFilePath}",
                        FilePath = mappingFilePath
                    }
                }
            };
        }

        try {
            var json = await File.ReadAllTextAsync(mappingFilePath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<MappingManifest>(json, JsonOptions);

            if (manifest == null) {
                return new MappingValidationResult {
                    IsValid = false,
                    Errors = new List<ValidationError> {
                        new() {
                            Type = ValidationErrorType.InvalidFileFormat,
                            Message = "Failed to parse mapping.json",
                            FilePath = mappingFilePath
                        }
                    }
                };
            }

            var basePath = Path.GetDirectoryName(mappingFilePath) ?? string.Empty;
            return await ValidateAsync(manifest, basePath, true, cancellationToken);

        } catch (JsonException ex) {
            return new MappingValidationResult {
                IsValid = false,
                Errors = new List<ValidationError> {
                    new() {
                        Type = ValidationErrorType.InvalidFileFormat,
                        Message = $"JSON parsing error: {ex.Message}",
                        FilePath = mappingFilePath
                    }
                }
            };
        }
    }
}

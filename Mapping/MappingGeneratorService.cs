using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AssetProcessor.Helpers;
using AssetProcessor.Mapping.Models;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using NLog;

namespace AssetProcessor.Mapping;

/// <summary>
/// Реализация сервиса генерации маппинга
/// </summary>
public class MappingGeneratorService : IMappingGeneratorService {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ILogService? _logService;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Словарь соответствия blendType → имя master материала
    /// </summary>
    private static readonly Dictionary<string, string> BlendTypeToMaster = new() {
        { "0", "pbr_opaque" },
        { "1", "pbr_alpha" },
        { "2", "pbr_additive" },
        { "3", "pbr_premul" },
        { "NONE", "pbr_opaque" },
        { "NORMAL", "pbr_alpha" },
        { "ADDITIVE", "pbr_additive" },
        { "PREMULTIPLIED", "pbr_premul" }
    };

    public MappingGeneratorService(ILogService? logService = null) {
        _logService = logService;
    }

    public async Task<MappingGenerationResult> GenerateMappingAsync(
        MappingGenerationOptions options,
        CancellationToken cancellationToken = default) {

        var stopwatch = Stopwatch.StartNew();
        var result = new MappingGenerationResult();

        try {
            LogInfo("=== Starting mapping generation ===");

            // Создаём манифест
            var manifest = new MappingManifest {
                Version = "1.0.0",
                Generated = DateTime.UtcNow.ToString("O"),
                BaseUrl = options.BaseUrl,
                Project = options.Project
            };

            // Генерируем маппинг моделей
            await GenerateModelMappingsAsync(manifest, options, cancellationToken);

            // Генерируем маппинг материалов
            var materialJsonFiles = await GenerateMaterialMappingsAsync(manifest, options, cancellationToken);

            // Генерируем маппинг текстур
            await GenerateTextureMappingsAsync(manifest, options, cancellationToken);

            // Собираем статистику
            manifest.Stats = new MappingStats {
                ModelsCount = manifest.Models.Count,
                MaterialsCount = manifest.Materials.Count,
                TexturesCount = manifest.Textures.Count,
                TotalLodFiles = manifest.Models.Values.Sum(m => m.Lods.Count)
            };

            // Сохраняем маппинг
            if (!string.IsNullOrEmpty(options.OutputPath)) {
                var mappingPath = Path.Combine(options.OutputPath, "mapping.json");
                await SaveMappingAsync(manifest, mappingPath, cancellationToken);
                result.MappingFilePath = mappingPath;
            }

            result.Manifest = manifest;
            result.MaterialJsonFiles = materialJsonFiles;
            result.Success = true;

            // Валидация после генерации
            if (options.ValidateAfterGeneration && !string.IsNullOrEmpty(options.OutputPath)) {
                result.ValidationResult = await ValidateMappingAsync(
                    manifest,
                    options.OutputPath,
                    cancellationToken);

                if (!result.ValidationResult.IsValid) {
                    LogWarning($"Mapping validation found {result.ValidationResult.Errors.Count} errors");
                }
            }

            stopwatch.Stop();
            result.GenerationTime = stopwatch.Elapsed;

            LogInfo($"=== Mapping generation completed in {stopwatch.ElapsedMilliseconds}ms ===");
            LogInfo($"Models: {manifest.Models.Count}, Materials: {manifest.Materials.Count}, Textures: {manifest.Textures.Count}");

        } catch (Exception ex) {
            Logger.Error(ex, "Error generating mapping");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task GenerateModelMappingsAsync(
        MappingManifest manifest,
        MappingGenerationOptions options,
        CancellationToken cancellationToken) {

        LogInfo($"Processing {options.Models.Count()} models...");

        foreach (var model in options.Models) {
            cancellationToken.ThrowIfCancellationRequested();

            var modelEntry = new ModelEntry {
                Name = model.Name ?? string.Empty,
                Path = BuildAssetPath(model, options.FolderPaths)
            };

            // Ищем LOD файлы для модели
            var lodFiles = FindLodFiles(model, options);
            modelEntry.Lods = lodFiles;

            // Ищем связанные материалы (из manifest модели или по имени)
            var materials = FindModelMaterials(model, options);
            modelEntry.Materials = materials;

            manifest.Models[model.ID.ToString()] = modelEntry;
        }

        await Task.CompletedTask;
    }

    private async Task<List<string>> GenerateMaterialMappingsAsync(
        MappingManifest manifest,
        MappingGenerationOptions options,
        CancellationToken cancellationToken) {

        var materialJsonFiles = new List<string>();
        LogInfo($"Processing {options.Materials.Count()} materials...");

        foreach (var material in options.Materials) {
            cancellationToken.ThrowIfCancellationRequested();

            var materialPath = BuildAssetPath(material, options.FolderPaths);
            var jsonFileName = $"{PathSanitizer.SanitizePath(material.Name ?? $"material_{material.ID}")}.json";
            var relativePath = string.IsNullOrEmpty(materialPath)
                ? jsonFileName
                : $"{materialPath}/{jsonFileName}";

            manifest.Materials[material.ID.ToString()] = relativePath;

            // Определяем master материал
            var masterName = DetermineMasterMaterial(material);
            if (!manifest.MasterMaterials.ContainsKey(masterName)) {
                // Присваиваем уникальный ID для master материала
                manifest.MasterMaterials[masterName] = 99999 - manifest.MasterMaterials.Count;
            }

            // Генерируем JSON файл материала
            if (options.GenerateMaterialJsonFiles && !string.IsNullOrEmpty(options.OutputPath)) {
                var materialJson = CreateMaterialInstanceJson(material, masterName);
                var fullPath = Path.Combine(options.OutputPath, relativePath);

                await SaveMaterialJsonAsync(materialJson, fullPath, cancellationToken);
                materialJsonFiles.Add(fullPath);
            }
        }

        return materialJsonFiles;
    }

    private async Task GenerateTextureMappingsAsync(
        MappingManifest manifest,
        MappingGenerationOptions options,
        CancellationToken cancellationToken) {

        LogInfo($"Processing {options.Textures.Count()} textures...");

        // Обычные текстуры
        foreach (var texture in options.Textures) {
            cancellationToken.ThrowIfCancellationRequested();

            var texturePath = BuildAssetPath(texture, options.FolderPaths);
            var ktx2FileName = GetKtx2FileName(texture);
            var relativePath = string.IsNullOrEmpty(texturePath)
                ? ktx2FileName
                : $"{texturePath}/{ktx2FileName}";

            manifest.Textures[texture.ID.ToString()] = relativePath;
        }

        // ORM текстуры (packed)
        foreach (var ormTexture in options.OrmTextures) {
            cancellationToken.ThrowIfCancellationRequested();

            // ORM текстуры могут не иметь PlayCanvas ID, генерируем свой
            var textureId = ormTexture.ID != 0
                ? ormTexture.ID.ToString()
                : $"orm_{ormTexture.Name}";

            var relativePath = GetOrmTexturePath(ormTexture, options);
            manifest.Textures[textureId] = relativePath;
        }

        await Task.CompletedTask;
    }

    public async Task<MappingValidationResult> ValidateMappingAsync(
        MappingManifest manifest,
        string basePath,
        CancellationToken cancellationToken = default) {

        var result = new MappingValidationResult();

        LogInfo("=== Validating mapping ===");

        // Собираем все используемые ID материалов из моделей
        var usedMaterialIds = new HashSet<string>();
        foreach (var model in manifest.Models.Values) {
            foreach (var materialId in model.Materials) {
                usedMaterialIds.Add(materialId.ToString());
            }
        }

        // Проверяем, что все материалы в моделях существуют
        foreach (var materialId in usedMaterialIds) {
            result.Stats.MaterialsChecked++;
            if (!manifest.Materials.ContainsKey(materialId)) {
                result.Errors.Add(new ValidationError {
                    Type = ValidationErrorType.MaterialNotFound,
                    Message = $"Material ID {materialId} referenced in model but not found in materials",
                    AssetId = int.TryParse(materialId, out var id) ? id : null
                });
                result.Stats.InvalidReferences++;
            } else {
                result.Stats.ValidReferences++;
            }
        }

        // Проверяем LOD файлы
        foreach (var (modelId, model) in manifest.Models) {
            result.Stats.ModelsChecked++;
            foreach (var lod in model.Lods) {
                result.Stats.LodFilesChecked++;
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

            // Предупреждение если нет LOD уровней
            if (model.Lods.Count == 0) {
                result.Warnings.Add(new ValidationWarning {
                    Type = ValidationWarningType.NoLodLevels,
                    Message = $"Model {model.Name} has no LOD levels",
                    AssetId = int.TryParse(modelId, out var id) ? id : null
                });
            }
        }

        // Проверяем файлы материалов
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

                // Загружаем JSON и проверяем ссылки на текстуры
                await ValidateMaterialTexturesAsync(manifest, fullPath, materialId, result, cancellationToken);
            }
        }

        // Проверяем файлы текстур
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

        // Проверяем неиспользуемые материалы
        var unusedMaterials = manifest.Materials.Keys.Except(usedMaterialIds);
        foreach (var materialId in unusedMaterials) {
            result.Warnings.Add(new ValidationWarning {
                Type = ValidationWarningType.UnusedMaterial,
                Message = $"Material ID {materialId} is not used by any model",
                AssetId = int.TryParse(materialId, out var id) ? id : null
            });
        }

        result.IsValid = result.Errors.Count == 0;

        LogInfo($"Validation complete: {result.Errors.Count} errors, {result.Warnings.Count} warnings");

        return result;
    }

    private async Task ValidateMaterialTexturesAsync(
        MappingManifest manifest,
        string materialJsonPath,
        string materialId,
        MappingValidationResult result,
        CancellationToken cancellationToken) {

        try {
            var json = await File.ReadAllTextAsync(materialJsonPath, cancellationToken);
            var materialJson = JsonSerializer.Deserialize<MaterialInstanceJson>(json, JsonOptions);

            if (materialJson?.Textures == null) return;

            var textureIds = new List<int?> {
                materialJson.Textures.DiffuseMap,
                materialJson.Textures.NormalMap,
                materialJson.Textures.SpecularMap,
                materialJson.Textures.GlossMap,
                materialJson.Textures.MetalnessMap,
                materialJson.Textures.AoMap,
                materialJson.Textures.EmissiveMap,
                materialJson.Textures.OpacityMap,
                materialJson.Textures.OgMap?.Asset,
                materialJson.Textures.OgmMap?.Asset,
                materialJson.Textures.OgmhMap?.Asset
            };

            foreach (var textureId in textureIds.Where(id => id.HasValue)) {
                var idStr = textureId!.Value.ToString();
                if (!manifest.Textures.ContainsKey(idStr)) {
                    result.Errors.Add(new ValidationError {
                        Type = ValidationErrorType.TextureNotFound,
                        Message = $"Texture ID {idStr} referenced in material {materialId} but not found in textures",
                        AssetId = textureId.Value
                    });
                    result.Stats.InvalidReferences++;
                } else {
                    result.Stats.ValidReferences++;
                }
            }
        } catch (Exception ex) {
            Logger.Warn(ex, $"Failed to validate material textures in {materialJsonPath}");
        }
    }

    public async Task SaveMappingAsync(
        MappingManifest manifest,
        string outputPath,
        CancellationToken cancellationToken = default) {

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        LogInfo($"Mapping saved to: {outputPath}");
    }

    public async Task<MappingManifest?> LoadMappingAsync(
        string inputPath,
        CancellationToken cancellationToken = default) {

        if (!File.Exists(inputPath)) {
            LogWarning($"Mapping file not found: {inputPath}");
            return null;
        }

        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);
        return JsonSerializer.Deserialize<MappingManifest>(json, JsonOptions);
    }

    #region Helper Methods

    private string BuildAssetPath(BaseResource asset, IReadOnlyDictionary<int, string> folderPaths) {
        // Если у ассета есть parent (folder ID), строим путь
        if (asset.Parent.HasValue && asset.Parent.Value != 0 &&
            folderPaths.TryGetValue(asset.Parent.Value, out var folderPath)) {
            return PathSanitizer.SanitizePath(folderPath);
        }

        // Иначе возвращаем пустой путь (корень)
        return string.Empty;
    }

    private List<LodEntry> FindLodFiles(ModelResource model, MappingGenerationOptions options) {
        var lodEntries = new List<LodEntry>();
        var modelName = PathSanitizer.SanitizePath(model.Name ?? $"model_{model.ID}");

        // Ищем LOD файлы по паттерну {modelName}_lod{N}.glb
        var searchPath = options.ProcessedModelsPath ?? options.OutputPath;
        if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) {
            return lodEntries;
        }

        // Также проверяем подпапку с путём модели
        var modelPath = BuildAssetPath(model, options.FolderPaths);
        var fullSearchPath = string.IsNullOrEmpty(modelPath)
            ? searchPath
            : Path.Combine(searchPath, modelPath);

        if (!Directory.Exists(fullSearchPath)) {
            // Пробуем найти в корневой папке
            fullSearchPath = searchPath;
        }

        // LOD дистанции по умолчанию
        var lodDistances = new Dictionary<int, float> {
            { 0, 0f },
            { 1, 20f },
            { 2, 50f },
            { 3, 100f }
        };

        for (int lodLevel = 0; lodLevel <= 3; lodLevel++) {
            var lodFileName = $"{modelName}_lod{lodLevel}.glb";
            var possiblePaths = new[] {
                Path.Combine(fullSearchPath, lodFileName),
                Path.Combine(searchPath, lodFileName),
                Path.Combine(searchPath, modelPath, lodFileName)
            };

            foreach (var lodPath in possiblePaths) {
                if (File.Exists(lodPath)) {
                    var relativePath = GetRelativePath(options.OutputPath, lodPath);
                    var entry = new LodEntry {
                        Level = lodLevel,
                        File = relativePath,
                        Distance = lodDistances.GetValueOrDefault(lodLevel, lodLevel * 25f)
                    };

                    if (options.IncludeFileSizes) {
                        entry.Size = new FileInfo(lodPath).Length;
                    }

                    lodEntries.Add(entry);
                    break;
                }
            }
        }

        return lodEntries;
    }

    private List<int> FindModelMaterials(ModelResource model, MappingGenerationOptions options) {
        var materials = new List<int>();

        // Проверяем, есть ли manifest для модели с материалами
        var modelName = PathSanitizer.SanitizePath(model.Name ?? $"model_{model.ID}");
        var manifestPath = Path.Combine(
            options.ProcessedModelsPath ?? options.OutputPath ?? string.Empty,
            $"{modelName}_manifest.json");

        if (File.Exists(manifestPath)) {
            try {
                var json = File.ReadAllText(manifestPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("lods", out var lods)) {
                    foreach (var lod in lods.EnumerateArray()) {
                        if (lod.TryGetProperty("materials", out var mats)) {
                            foreach (var mat in mats.EnumerateArray()) {
                                var matName = mat.GetString();
                                // Пытаемся найти ID материала по имени
                                var materialResource = options.Materials
                                    .FirstOrDefault(m => m.Name == matName);
                                if (materialResource != null && !materials.Contains(materialResource.ID)) {
                                    materials.Add(materialResource.ID);
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Warn(ex, $"Failed to parse model manifest: {manifestPath}");
            }
        }

        return materials;
    }

    private string DetermineMasterMaterial(MaterialResource material) {
        // Определяем по blendType
        if (!string.IsNullOrEmpty(material.BlendType)) {
            if (BlendTypeToMaster.TryGetValue(material.BlendType.ToUpperInvariant(), out var master)) {
                return master;
            }
            if (BlendTypeToMaster.TryGetValue(material.BlendType, out master)) {
                return master;
            }
        }

        // По умолчанию - opaque
        return "pbr_opaque";
    }

    private MaterialInstanceJson CreateMaterialInstanceJson(MaterialResource material, string masterName) {
        var instance = new MaterialInstanceJson {
            Master = masterName,
            Params = new MaterialParams {
                Diffuse = material.Diffuse?.ToArray(),
                Metalness = material.UseMetalness ? material.Metalness : null,
                Gloss = material.Glossiness ?? material.Shininess,
                Emissive = material.Emissive?.ToArray(),
                EmissiveIntensity = material.EmissiveIntensity,
                Opacity = material.Opacity,
                AlphaTest = material.AlphaTest,
                Bumpiness = material.BumpMapFactor,
                AoColor = material.AOTint ? material.AOColor?.ToArray() : null,
                Specular = material.SpecularTint ? material.Specular?.ToArray() : null,
                Reflectivity = material.Reflectivity,
                UseMetalness = material.UseMetalness ? true : null
            },
            Textures = new MaterialTextures {
                DiffuseMap = material.DiffuseMapId,
                NormalMap = material.NormalMapId,
                SpecularMap = material.SpecularMapId,
                GlossMap = material.GlossMapId,
                MetalnessMap = material.MetalnessMapId,
                AoMap = material.AOMapId,
                EmissiveMap = material.EmissiveMapId,
                OpacityMap = material.OpacityMapId
            }
        };

        return instance;
    }

    private async Task SaveMaterialJsonAsync(
        MaterialInstanceJson materialJson,
        string outputPath,
        CancellationToken cancellationToken) {

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(materialJson, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    private string GetKtx2FileName(TextureResource texture) {
        var baseName = Path.GetFileNameWithoutExtension(texture.Name ?? $"texture_{texture.ID}");
        return $"{PathSanitizer.SanitizePath(baseName)}.ktx2";
    }

    private string GetOrmTexturePath(ORMTextureResource ormTexture, MappingGenerationOptions options) {
        // ORM текстуры сохраняются с суффиксами _og, _ogm, _ogmh
        var fileName = Path.GetFileName(ormTexture.Path ?? ormTexture.Name ?? "unknown");

        if (!string.IsNullOrEmpty(options.ProcessedTexturesPath)) {
            var relativePath = GetRelativePath(options.OutputPath,
                Path.Combine(options.ProcessedTexturesPath, fileName));
            return relativePath;
        }

        return fileName;
    }

    private string GetRelativePath(string? basePath, string fullPath) {
        if (string.IsNullOrEmpty(basePath)) {
            return fullPath;
        }

        try {
            var baseUri = new Uri(Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(Path.GetFullPath(fullPath));
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('\\', '/');
        } catch {
            return Path.GetFileName(fullPath);
        }
    }

    private void LogInfo(string message) {
        Logger.Info(message);
        _logService?.LogInfo(message);
    }

    private void LogWarning(string message) {
        Logger.Warn(message);
        _logService?.LogWarn(message);
    }

    #endregion
}

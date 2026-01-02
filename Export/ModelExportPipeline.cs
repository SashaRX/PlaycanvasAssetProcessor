using System.IO;
using System.Text.Json;
using AssetProcessor.Mapping;
using AssetProcessor.Mapping.Models;
using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.Export;

/// <summary>
/// Пайплайн экспорта модели со всеми связанными ресурсами
/// Model → Materials → Textures → ORM packed → JSON → GLB
/// </summary>
public class ModelExportPipeline {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly MaterialJsonGenerator _materialJsonGenerator;
    private readonly string _projectName;
    private readonly string _outputBasePath;

    /// <summary>
    /// Событие прогресса экспорта
    /// </summary>
    public event Action<ExportProgress>? ProgressChanged;

    public ModelExportPipeline(string projectName, string outputBasePath) {
        _projectName = projectName;
        _outputBasePath = outputBasePath;
        _materialJsonGenerator = new MaterialJsonGenerator();
    }

    /// <summary>
    /// Получить базовый путь для экспорта: [project]/server/assets/content
    /// </summary>
    public string GetContentBasePath() {
        return Path.Combine(_outputBasePath, _projectName, "server", "assets", "content");
    }

    /// <summary>
    /// Экспортирует модель со всеми связанными ресурсами
    /// </summary>
    public async Task<ModelExportResult> ExportModelAsync(
        ModelResource model,
        IEnumerable<MaterialResource> allMaterials,
        IEnumerable<TextureResource> allTextures,
        IReadOnlyDictionary<int, string> folderPaths,
        ExportOptions options,
        CancellationToken cancellationToken = default) {

        var result = new ModelExportResult {
            ModelId = model.ID,
            ModelName = model.Name ?? $"model_{model.ID}"
        };

        try {
            ReportProgress($"Starting export: {model.Name}", 0);

            // 1. Определяем путь экспорта из иерархии PlayCanvas
            var modelFolderPath = GetResourceFolderPath(model, folderPaths);
            var exportPath = Path.Combine(GetContentBasePath(), modelFolderPath);
            result.ExportPath = exportPath;

            Directory.CreateDirectory(exportPath);

            // 2. Находим материалы, принадлежащие этой модели
            ReportProgress("Finding materials...", 10);
            var modelMaterials = FindMaterialsForModel(model, allMaterials);
            result.MaterialCount = modelMaterials.Count;

            // 3. Собираем все текстуры из материалов
            ReportProgress("Collecting textures...", 20);
            var textureIds = CollectTextureIds(modelMaterials);
            var modelTextures = allTextures.Where(t => textureIds.Contains(t.ID)).ToList();
            result.TextureCount = modelTextures.Count;

            // 4. Группируем текстуры для создания ORM packed
            ReportProgress("Analyzing textures for ORM packing...", 30);
            var textureGroups = GroupTexturesForORM(modelTextures, modelMaterials);

            // 5. Создаём директории
            var materialsDir = Path.Combine(exportPath, "materials");
            var texturesDir = Path.Combine(exportPath, "textures");
            Directory.CreateDirectory(materialsDir);
            Directory.CreateDirectory(texturesDir);

            // 6. Генерируем ORM packed текстуры (если есть что паковать)
            if (options.GenerateORMTextures && textureGroups.Any()) {
                ReportProgress("Generating ORM packed textures...", 40);
                var ormResults = await GenerateORMTexturesAsync(
                    textureGroups, texturesDir, options, cancellationToken);
                result.GeneratedORMTextures.AddRange(ormResults);
            }

            // 7. Конвертируем обычные текстуры в KTX2
            if (options.ConvertTextures) {
                ReportProgress("Converting textures to KTX2...", 50);
                var ktxResults = await ConvertTexturesToKTX2Async(
                    modelTextures, texturesDir, options, cancellationToken);
                result.ConvertedTextures.AddRange(ktxResults);
            }

            // 8. Генерируем material instance JSON
            ReportProgress("Generating material instances...", 70);
            foreach (var material in modelMaterials) {
                cancellationToken.ThrowIfCancellationRequested();

                var materialJson = _materialJsonGenerator.GenerateMaterialJson(
                    material, null, new MaterialJsonOptions { UsePackedTextures = options.UsePackedTextures });

                // Заменяем texture IDs на относительные пути
                UpdateTextureReferences(materialJson, modelTextures, result.GeneratedORMTextures);

                var materialFileName = GetSafeFileName(material.Name ?? $"mat_{material.ID}") + ".json";
                var materialPath = Path.Combine(materialsDir, materialFileName);

                var json = JsonSerializer.Serialize(materialJson, new JsonSerializerOptions {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await File.WriteAllTextAsync(materialPath, json, cancellationToken);

                result.GeneratedMaterialJsons.Add(materialPath);
            }

            // 9. Конвертируем модель в GLB + LOD (если включено)
            if (options.ConvertModel) {
                ReportProgress("Converting model to GLB...", 85);
                var glbResult = await ConvertModelToGLBAsync(model, exportPath, options, cancellationToken);
                result.ConvertedModelPath = glbResult.MainModelPath;
                result.LODPaths.AddRange(glbResult.LODPaths);
            }

            result.Success = true;
            ReportProgress($"Export completed: {model.Name}", 100);

        } catch (OperationCanceledException) {
            result.Success = false;
            result.ErrorMessage = "Export cancelled";
            Logger.Info($"Export cancelled for model {model.Name}");
        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Logger.Error(ex, $"Failed to export model {model.Name}");
        }

        return result;
    }

    #region Private Methods

    private string GetResourceFolderPath(BaseResource resource, IReadOnlyDictionary<int, string> folderPaths) {
        // Строим путь из иерархии папок PlayCanvas
        if (resource.Parent.HasValue && resource.Parent.Value != 0) {
            if (folderPaths.TryGetValue(resource.Parent.Value, out var path)) {
                return SanitizePath(path);
            }
        }

        // Если нет parent, используем имя ресурса
        return SanitizePath(resource.Name ?? $"unknown_{resource.ID}");
    }

    private List<MaterialResource> FindMaterialsForModel(
        ModelResource model, IEnumerable<MaterialResource> allMaterials) {

        // Находим материалы по Parent ID (материалы в той же папке что и модель)
        // или материалы с таким же базовым именем
        var materials = new List<MaterialResource>();
        var modelBaseName = ExtractBaseName(model.Name);

        foreach (var material in allMaterials) {
            // По Parent ID
            if (model.Parent.HasValue && material.Parent == model.Parent) {
                materials.Add(material);
                continue;
            }

            // По имени (например, model "chair" -> material "chair_mat")
            if (!string.IsNullOrEmpty(modelBaseName) && !string.IsNullOrEmpty(material.Name)) {
                var materialBaseName = ExtractBaseName(material.Name);
                if (materialBaseName.StartsWith(modelBaseName, StringComparison.OrdinalIgnoreCase)) {
                    materials.Add(material);
                }
            }
        }

        return materials;
    }

    private HashSet<int> CollectTextureIds(IEnumerable<MaterialResource> materials) {
        var ids = new HashSet<int>();

        foreach (var mat in materials) {
            if (mat.DiffuseMapId.HasValue) ids.Add(mat.DiffuseMapId.Value);
            if (mat.NormalMapId.HasValue) ids.Add(mat.NormalMapId.Value);
            if (mat.SpecularMapId.HasValue) ids.Add(mat.SpecularMapId.Value);
            if (mat.GlossMapId.HasValue) ids.Add(mat.GlossMapId.Value);
            if (mat.MetalnessMapId.HasValue) ids.Add(mat.MetalnessMapId.Value);
            if (mat.AOMapId.HasValue) ids.Add(mat.AOMapId.Value);
            if (mat.EmissiveMapId.HasValue) ids.Add(mat.EmissiveMapId.Value);
            if (mat.OpacityMapId.HasValue) ids.Add(mat.OpacityMapId.Value);
        }

        return ids;
    }

    private List<ORMTextureGroup> GroupTexturesForORM(
        IEnumerable<TextureResource> textures, IEnumerable<MaterialResource> materials) {

        var groups = new List<ORMTextureGroup>();
        var textureDict = textures.ToDictionary(t => t.ID);

        foreach (var material in materials) {
            var group = new ORMTextureGroup {
                MaterialId = material.ID,
                MaterialName = material.Name
            };

            // AO
            if (material.AOMapId.HasValue && textureDict.TryGetValue(material.AOMapId.Value, out var aoTex)) {
                group.AOTexture = aoTex;
            }

            // Gloss
            if (material.GlossMapId.HasValue && textureDict.TryGetValue(material.GlossMapId.Value, out var glossTex)) {
                group.GlossTexture = glossTex;
            }

            // Metalness
            if (material.MetalnessMapId.HasValue && textureDict.TryGetValue(material.MetalnessMapId.Value, out var metalTex)) {
                group.MetalnessTexture = metalTex;
            }

            // Определяем режим пакинга
            group.PackingMode = DeterminePackingMode(group);

            if (group.PackingMode != ChannelPackingMode.None) {
                groups.Add(group);
            }
        }

        return groups;
    }

    private ChannelPackingMode DeterminePackingMode(ORMTextureGroup group) {
        bool hasAO = group.AOTexture != null;
        bool hasGloss = group.GlossTexture != null;
        bool hasMetalness = group.MetalnessTexture != null;

        if (hasAO && hasGloss && hasMetalness) return ChannelPackingMode.OGM;
        if (hasAO && hasGloss) return ChannelPackingMode.OG;
        // Можно добавить OGMH если будет height map

        return ChannelPackingMode.None;
    }

    private async Task<List<string>> GenerateORMTexturesAsync(
        List<ORMTextureGroup> groups,
        string outputDir,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var results = new List<string>();

        foreach (var group in groups) {
            cancellationToken.ThrowIfCancellationRequested();

            // TODO: Интеграция с существующим ORM генератором
            // Пока создаём placeholder
            var ormFileName = GetSafeFileName(group.MaterialName ?? $"mat_{group.MaterialId}");
            var suffix = group.PackingMode switch {
                ChannelPackingMode.OG => "_og",
                ChannelPackingMode.OGM => "_ogm",
                ChannelPackingMode.OGMH => "_ogmh",
                _ => ""
            };

            var outputPath = Path.Combine(outputDir, $"{ormFileName}{suffix}.ktx2");
            results.Add(outputPath);

            Logger.Debug($"ORM texture will be generated: {outputPath}");
        }

        return results;
    }

    private async Task<List<string>> ConvertTexturesToKTX2Async(
        List<TextureResource> textures,
        string outputDir,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var results = new List<string>();

        foreach (var texture in textures) {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(texture.Path) || !File.Exists(texture.Path)) {
                Logger.Warn($"Texture file not found: {texture.Name}");
                continue;
            }

            // TODO: Интеграция с существующим TextureConversionPipeline
            var outputFileName = GetSafeFileName(texture.Name ?? $"tex_{texture.ID}") + ".ktx2";
            var outputPath = Path.Combine(outputDir, outputFileName);
            results.Add(outputPath);

            Logger.Debug($"Texture will be converted: {texture.Path} -> {outputPath}");
        }

        return results;
    }

    private async Task<GLBConversionResult> ConvertModelToGLBAsync(
        ModelResource model,
        string outputDir,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var result = new GLBConversionResult();

        // TODO: Интеграция с FBX2glTF и gltfpack
        var modelFileName = GetSafeFileName(model.Name ?? $"model_{model.ID}");
        result.MainModelPath = Path.Combine(outputDir, $"{modelFileName}.glb");

        if (options.GenerateLODs) {
            for (int i = 1; i <= options.LODLevels; i++) {
                result.LODPaths.Add(Path.Combine(outputDir, $"{modelFileName}_lod{i}.glb"));
            }
        }

        Logger.Debug($"Model will be converted: {model.Path} -> {result.MainModelPath}");

        return result;
    }

    private void UpdateTextureReferences(
        MaterialInstanceJson materialJson,
        List<TextureResource> textures,
        List<string> ormTextures) {

        // TODO: Обновить ссылки на текстуры с ID на относительные пути
        // Это будет сделано когда определимся с финальным форматом
    }

    private string ExtractBaseName(string? name) {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        // Убираем расширение
        var baseName = Path.GetFileNameWithoutExtension(name);

        // Убираем суффиксы типа _mat, _material
        var suffixes = new[] { "_mat", "_material", "_mtl" };
        foreach (var suffix in suffixes) {
            if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                return baseName.Substring(0, baseName.Length - suffix.Length);
            }
        }

        return baseName;
    }

    private string GetSafeFileName(string name) {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    private string SanitizePath(string path) {
        var invalid = Path.GetInvalidPathChars();
        return string.Join("_", path.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    private void ReportProgress(string message, int percentage) {
        ProgressChanged?.Invoke(new ExportProgress {
            Message = message,
            Percentage = percentage
        });
    }

    #endregion
}

#region Supporting Classes

public class ExportOptions {
    public bool ConvertModel { get; set; } = true;
    public bool ConvertTextures { get; set; } = true;
    public bool GenerateORMTextures { get; set; } = true;
    public bool UsePackedTextures { get; set; } = true;
    public bool GenerateLODs { get; set; } = true;
    public int LODLevels { get; set; } = 2;
}

public class ExportProgress {
    public string Message { get; set; } = "";
    public int Percentage { get; set; }
}

public class ModelExportResult {
    public int ModelId { get; set; }
    public string ModelName { get; set; } = "";
    public string? ExportPath { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public int MaterialCount { get; set; }
    public int TextureCount { get; set; }

    public string? ConvertedModelPath { get; set; }
    public List<string> LODPaths { get; set; } = new();
    public List<string> GeneratedMaterialJsons { get; set; } = new();
    public List<string> ConvertedTextures { get; set; } = new();
    public List<string> GeneratedORMTextures { get; set; } = new();
}

public class GLBConversionResult {
    public string? MainModelPath { get; set; }
    public List<string> LODPaths { get; set; } = new();
}

public class ORMTextureGroup {
    public int MaterialId { get; set; }
    public string? MaterialName { get; set; }
    public TextureResource? AOTexture { get; set; }
    public TextureResource? GlossTexture { get; set; }
    public TextureResource? MetalnessTexture { get; set; }
    public TextureResource? HeightTexture { get; set; }
    public ChannelPackingMode PackingMode { get; set; }
}

#endregion

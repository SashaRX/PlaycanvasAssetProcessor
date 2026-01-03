using System.IO;
using System.Text.Json;
using AssetProcessor.Mapping;
using AssetProcessor.Mapping.Models;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;
using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.Export;

/// <summary>
/// Пайплайн экспорта модели со всеми связанными ресурсами
/// Model → Materials → Textures → ORM packed → JSON → GLB
/// </summary>
public class ModelExportPipeline {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly string _projectName;
    private readonly string _outputBasePath;
    private readonly ModelConversionPipeline _modelConversionPipeline;
    private readonly TextureConversionPipeline _textureConversionPipeline;
    private readonly ChannelPackingPipeline _channelPackingPipeline;
    private readonly MaterialJsonGenerator _materialJsonGenerator;

    /// <summary>
    /// Событие прогресса экспорта
    /// </summary>
    public event Action<ExportProgress>? ProgressChanged;

    public ModelExportPipeline(
        string projectName,
        string outputBasePath,
        string? fbx2glTFPath = null,
        string? gltfPackPath = null,
        string? ktxPath = null) {

        _projectName = projectName;
        _outputBasePath = outputBasePath;
        _modelConversionPipeline = new ModelConversionPipeline(fbx2glTFPath, gltfPackPath);
        _textureConversionPipeline = new TextureConversionPipeline(ktxPath);
        _channelPackingPipeline = new ChannelPackingPipeline();
        _materialJsonGenerator = new MaterialJsonGenerator();
    }

    /// <summary>
    /// Получить базовый путь для экспорта: [project]/server/assets/content
    /// </summary>
    public string GetContentBasePath() {
        return Path.Combine(_outputBasePath, _projectName, "server", "assets", "content");
    }

    /// <summary>
    /// Получить путь к server папке: [project]/server/
    /// </summary>
    public string GetServerPath() {
        return Path.Combine(_outputBasePath, _projectName, "server");
    }

    /// <summary>
    /// Получить путь к mapping.json: [project]/server/mapping.json
    /// </summary>
    public string GetMappingPath() {
        return Path.Combine(GetServerPath(), "mapping.json");
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
            Logger.Info($"Export path: {exportPath}");

            // 2. Находим материалы, принадлежащие этой модели
            ReportProgress("Finding materials...", 10);
            var modelMaterials = FindMaterialsForModel(model, allMaterials, folderPaths);
            result.MaterialCount = modelMaterials.Count;
            Logger.Info($"Found {modelMaterials.Count} materials for model {model.Name} (Parent={model.Parent})");

            // Логируем найденные материалы
            foreach (var mat in modelMaterials) {
                Logger.Info($"  Material [{mat.ID}] {mat.Name}: Diffuse={mat.DiffuseMapId}, Normal={mat.NormalMapId}, AO={mat.AOMapId}, Gloss={mat.GlossMapId}, Metal={mat.MetalnessMapId}");
            }

            // 3. Собираем все текстуры из материалов
            ReportProgress("Collecting textures...", 20);
            var textureIds = CollectTextureIds(modelMaterials);
            Logger.Info($"Texture IDs from materials: [{string.Join(", ", textureIds)}]");

            var modelTextures = allTextures.Where(t => textureIds.Contains(t.ID)).ToList();
            result.TextureCount = modelTextures.Count;
            Logger.Info($"Found {modelTextures.Count} textures matching IDs");

            // Логируем статус текстур
            foreach (var tex in modelTextures) {
                var hasFile = !string.IsNullOrEmpty(tex.Path) && File.Exists(tex.Path);
                Logger.Info($"  Texture [{tex.ID}] {tex.Name}: Path={tex.Path ?? "null"}, Exists={hasFile}");
            }

            // 4. Создаём директории (только textures, без materials)
            var texturesDir = Path.Combine(exportPath, "textures");
            Directory.CreateDirectory(texturesDir);

            // 5. Генерируем ORM packed текстуры
            var ormResults = new Dictionary<int, ORMExportResult>(); // MaterialId -> ORM result
            var packedTextureIds = new HashSet<int>(); // Текстуры, включённые в ORM
            if (options.GenerateORMTextures) {
                ReportProgress("Generating ORM packed textures...", 30);
                (ormResults, packedTextureIds) = await GenerateORMTexturesAsync(
                    modelMaterials, modelTextures, texturesDir, options, cancellationToken);
                result.GeneratedORMTextures.AddRange(ormResults.Values.Select(r => r.OutputPath));
                Logger.Info($"ORM packed {packedTextureIds.Count} textures into {ormResults.Count} ORM files");
            }

            // 6. Конвертируем обычные текстуры в KTX2 (пропускаем те, что уже в ORM)
            var texturePathMap = new Dictionary<int, string>(); // TextureId -> relative path
            if (options.ConvertTextures) {
                ReportProgress("Converting textures to KTX2...", 50);
                var texturesToConvert = modelTextures.Where(t => !packedTextureIds.Contains(t.ID)).ToList();
                Logger.Info($"Converting {texturesToConvert.Count} textures (skipping {packedTextureIds.Count} packed in ORM)");
                texturePathMap = await ConvertTexturesToKTX2Async(
                    texturesToConvert, texturesDir, exportPath, options, cancellationToken);
                result.ConvertedTextures.AddRange(texturePathMap.Values);
            }

            // 7. Генерируем material JSON файлы и обновляем глобальный mapping.json
            ReportProgress("Generating material files...", 70);
            var modelFileName = GetSafeFileName(model.Name ?? $"model_{model.ID}");
            var materialIds = new List<int>();

            // Базовый путь относительно server/ для mapping.json
            var assetsContentPath = "assets/content";
            var modelRelativePath = $"{assetsContentPath}/{modelFolderPath}";

            // Генерируем отдельный JSON для каждого материала
            foreach (var material in modelMaterials) {
                cancellationToken.ThrowIfCancellationRequested();

                var materialJson = GenerateMaterialJsonWithIds(material, ormResults, options);
                var matFileName = GetSafeFileName(material.Name ?? $"mat_{material.ID}") + ".json";
                var matPath = Path.Combine(exportPath, matFileName);

                var json = JsonSerializer.Serialize(materialJson, new JsonSerializerOptions {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await File.WriteAllTextAsync(matPath, json, cancellationToken);

                materialIds.Add(material.ID);
                result.GeneratedMaterialJsons.Add(matPath);
                Logger.Info($"Generated material JSON: {matPath}");
            }

            // Обновляем глобальный mapping.json в папке server/
            await UpdateGlobalMappingAsync(
                model, modelMaterials, modelFileName, modelFolderPath,
                texturePathMap, ormResults, options, cancellationToken);
            Logger.Info($"Updated global mapping: {GetMappingPath()}");

            // 8. Конвертируем модель в GLB + LOD
            if (options.ConvertModel && !string.IsNullOrEmpty(model.Path) && File.Exists(model.Path)) {
                ReportProgress("Converting model to GLB...", 85);
                var glbResult = await ConvertModelToGLBAsync(model, exportPath, options, cancellationToken);

                if (string.IsNullOrEmpty(glbResult.MainModelPath)) {
                    result.Success = false;
                    result.ErrorMessage = !string.IsNullOrEmpty(glbResult.ErrorMessage)
                        ? $"Model conversion failed: {glbResult.ErrorMessage}"
                        : "Model conversion failed - no GLB file produced. Check FBX2glTF and gltfpack paths in Settings.";
                    Logger.Error($"Model conversion produced no output for {model.Name}: {glbResult.ErrorMessage}");
                    return result;
                }

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
                var sanitized = SanitizePath(path);
                // Убираем префикс content/ если он есть (уже добавлен в GetContentBasePath)
                if (sanitized.StartsWith("content/", StringComparison.OrdinalIgnoreCase)) {
                    sanitized = sanitized.Substring(8);
                } else if (sanitized.StartsWith("content\\", StringComparison.OrdinalIgnoreCase)) {
                    sanitized = sanitized.Substring(8);
                }
                return sanitized;
            }
        }

        // Если нет parent, используем имя ресурса как папку
        return Path.Combine("models", SanitizePath(resource.Name ?? $"unknown_{resource.ID}"));
    }

    private List<MaterialResource> FindMaterialsForModel(
        ModelResource model,
        IEnumerable<MaterialResource> allMaterials,
        IReadOnlyDictionary<int, string> folderPaths) {

        var materials = new List<MaterialResource>();
        var modelBaseName = ExtractBaseName(model.Name);

        // Получаем путь папки модели
        string? modelFolderPath = null;
        if (model.Parent.HasValue && model.Parent.Value != 0) {
            folderPaths.TryGetValue(model.Parent.Value, out modelFolderPath);
        }

        foreach (var material in allMaterials) {
            // По Parent ID - материалы в той же папке
            if (model.Parent.HasValue && material.Parent == model.Parent) {
                materials.Add(material);
                continue;
            }

            // По пути папки
            if (!string.IsNullOrEmpty(modelFolderPath) && material.Parent.HasValue) {
                if (folderPaths.TryGetValue(material.Parent.Value, out var materialFolderPath)) {
                    if (materialFolderPath.StartsWith(modelFolderPath, StringComparison.OrdinalIgnoreCase)) {
                        materials.Add(material);
                        continue;
                    }
                }
            }

            // По имени (например, model "chair" -> material "chair_mat")
            if (!string.IsNullOrEmpty(modelBaseName) && !string.IsNullOrEmpty(material.Name)) {
                var materialBaseName = ExtractBaseName(material.Name);
                if (materialBaseName.StartsWith(modelBaseName, StringComparison.OrdinalIgnoreCase) ||
                    modelBaseName.StartsWith(materialBaseName, StringComparison.OrdinalIgnoreCase)) {
                    materials.Add(material);
                }
            }
        }

        return materials.Distinct().ToList();
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

    private async Task<(Dictionary<int, ORMExportResult>, HashSet<int>)> GenerateORMTexturesAsync(
        List<MaterialResource> materials,
        List<TextureResource> textures,
        string outputDir,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var results = new Dictionary<int, ORMExportResult>();
        var packedTextureIds = new HashSet<int>(); // Текстуры, которые были упакованы
        var textureDict = textures.ToDictionary(t => t.ID);

        foreach (var material in materials) {
            cancellationToken.ThrowIfCancellationRequested();

            // Проверяем какие текстуры есть для ORM
            TextureResource? aoTexture = null;
            TextureResource? glossTexture = null;
            TextureResource? metallicTexture = null;

            if (material.AOMapId.HasValue && textureDict.TryGetValue(material.AOMapId.Value, out var ao))
                aoTexture = ao;
            if (material.GlossMapId.HasValue && textureDict.TryGetValue(material.GlossMapId.Value, out var gloss))
                glossTexture = gloss;
            if (material.MetalnessMapId.HasValue && textureDict.TryGetValue(material.MetalnessMapId.Value, out var metal))
                metallicTexture = metal;

            // Определяем режим пакинга
            var packingMode = DeterminePackingMode(aoTexture, glossTexture, metallicTexture);
            if (packingMode == ChannelPackingMode.None) continue;

            var ormFileName = GetSafeFileName(material.Name ?? $"mat_{material.ID}");
            var suffix = packingMode switch {
                ChannelPackingMode.OG => "_og",
                ChannelPackingMode.OGM => "_ogm",
                ChannelPackingMode.OGMH => "_ogmh",
                _ => ""
            };

            var outputPath = Path.Combine(outputDir, $"{ormFileName}{suffix}.ktx2");

            // Создаём настройки для ChannelPackingPipeline (используем настройки материала если есть)
            var packingSettings = CreatePackingSettings(
                packingMode, aoTexture, glossTexture, metallicTexture, options, material);

            try {
                // Генерируем packed текстуру
                var packedMipmaps = await _channelPackingPipeline.PackChannelsAsync(packingSettings);

                // Сохраняем временные PNG для ktx create
                var tempDir = Path.Combine(Path.GetTempPath(), $"orm_export_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                var mipPaths = new List<string>();
                for (int i = 0; i < packedMipmaps.Count; i++) {
                    var mipPath = Path.Combine(tempDir, $"mip{i}.png");
                    await packedMipmaps[i].SaveAsPngAsync(mipPath);
                    mipPaths.Add(mipPath);
                }

                // Конвертируем в KTX2
                var ktxResult = await _textureConversionPipeline.ConvertTextureAsync(
                    mipPaths[0], // Используем первый мипмап как источник
                    outputPath,
                    MipGenerationProfile.CreateDefault(TextureType.Generic),
                    new CompressionSettings {
                        CompressionFormat = CompressionFormat.ETC1S,
                        QualityLevel = options.TextureQuality,
                        GenerateMipmaps = true,
                        UseCustomMipmaps = false
                    });

                // Очищаем временные файлы
                foreach (var mip in packedMipmaps) mip.Dispose();
                try { Directory.Delete(tempDir, true); } catch { }

                if (ktxResult.Success) {
                    results[material.ID] = new ORMExportResult {
                        MaterialId = material.ID,
                        PackingMode = packingMode,
                        OutputPath = outputPath,
                        RelativePath = $"textures/{ormFileName}{suffix}.ktx2"
                    };

                    // Добавляем ID текстур, которые были упакованы в ORM
                    if (aoTexture != null) packedTextureIds.Add(aoTexture.ID);
                    if (glossTexture != null) packedTextureIds.Add(glossTexture.ID);
                    if (metallicTexture != null) packedTextureIds.Add(metallicTexture.ID);

                    Logger.Info($"Generated ORM texture: {outputPath}");
                }

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to generate ORM for material {material.Name}");
            }
        }

        return (results, packedTextureIds);
    }

    private ChannelPackingSettings CreatePackingSettings(
        ChannelPackingMode mode,
        TextureResource? aoTexture,
        TextureResource? glossTexture,
        TextureResource? metallicTexture,
        ExportOptions options,
        MaterialResource? material = null) {

        // Если у материала есть ORM настройки, используем их
        if (material?.ORMSettings != null && material.ORMSettings.Enabled) {
            return material.ORMSettings.ToChannelPackingSettings(
                aoTexture?.Path,
                glossTexture?.Path,
                metallicTexture?.Path
            );
        }

        // Иначе используем настройки по умолчанию
        var settings = new ChannelPackingSettings { Mode = mode };

        if (aoTexture?.Path != null) {
            settings.RedChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.AmbientOcclusion,
                SourcePath = aoTexture.Path,
                DefaultValue = 1.0f
            };
        }

        if (glossTexture?.Path != null) {
            settings.GreenChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Gloss,
                SourcePath = glossTexture.Path,
                DefaultValue = 0.5f,
                ApplyToksvig = options.ApplyToksvig,
                AOProcessingMode = AOProcessingMode.None // Gloss не поддерживает AO processing
            };
        }

        if (metallicTexture?.Path != null) {
            settings.BlueChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Metallic,
                SourcePath = metallicTexture.Path,
                DefaultValue = 0.0f
            };
        }

        return settings;
    }

    private ChannelPackingMode DeterminePackingMode(
        TextureResource? ao, TextureResource? gloss, TextureResource? metallic) {

        bool hasAO = ao?.Path != null && File.Exists(ao.Path);
        bool hasGloss = gloss?.Path != null && File.Exists(gloss.Path);
        bool hasMetallic = metallic?.Path != null && File.Exists(metallic.Path);

        if (hasAO && hasGloss && hasMetallic) return ChannelPackingMode.OGM;
        if (hasAO && hasGloss) return ChannelPackingMode.OG;

        return ChannelPackingMode.None;
    }

    private async Task<Dictionary<int, string>> ConvertTexturesToKTX2Async(
        List<TextureResource> textures,
        string texturesDir,
        string exportPath,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var results = new Dictionary<int, string>();

        foreach (var texture in textures) {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(texture.Path) || !File.Exists(texture.Path)) {
                Logger.Warn($"Texture file not found: {texture.Name} ({texture.Path})");
                continue;
            }

            var outputFileName = GetSafeFileName(texture.Name ?? $"tex_{texture.ID}") + ".ktx2";
            var outputPath = Path.Combine(texturesDir, outputFileName);

            try {
                // Определяем тип текстуры для выбора профиля
                var textureType = DetermineTextureType(texture);
                var profile = MipGenerationProfile.CreateDefault(textureType);
                var compressionSettings = CreateCompressionSettings(textureType, options);

                var ktxResult = await _textureConversionPipeline.ConvertTextureAsync(
                    texture.Path,
                    outputPath,
                    profile,
                    compressionSettings);

                if (ktxResult.Success) {
                    // Сохраняем относительный путь
                    var relativePath = Path.GetRelativePath(exportPath, outputPath).Replace('\\', '/');
                    results[texture.ID] = relativePath;
                    Logger.Info($"Converted texture: {texture.Name} -> {outputPath}");
                } else {
                    Logger.Error($"Failed to convert texture {texture.Name}: {ktxResult.Error}");
                }

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to convert texture {texture.Name}");
            }
        }

        return results;
    }

    private TextureType DetermineTextureType(TextureResource texture) {
        var type = texture.TextureType?.ToLowerInvariant() ?? "";
        var name = texture.Name?.ToLowerInvariant() ?? "";

        if (type == "normal" || name.Contains("normal") || name.Contains("_n."))
            return TextureType.Normal;
        if (type == "albedo" || name.Contains("albedo") || name.Contains("diffuse") || name.Contains("_d."))
            return TextureType.Albedo;
        if (type == "gloss" || name.Contains("gloss") || name.Contains("roughness"))
            return TextureType.Gloss;
        if (type == "metallic" || name.Contains("metallic") || name.Contains("metal"))
            return TextureType.Metallic;
        if (type == "ao" || name.Contains("_ao") || name.Contains("occlusion"))
            return TextureType.AmbientOcclusion;
        if (name.Contains("emissive") || name.Contains("emission"))
            return TextureType.Emissive;

        return TextureType.Generic;
    }

    private CompressionSettings CreateCompressionSettings(TextureType textureType, ExportOptions options) {
        var settings = new CompressionSettings {
            QualityLevel = options.TextureQuality,
            GenerateMipmaps = true
        };

        // Normal maps используют UASTC для лучшего качества
        if (textureType == TextureType.Normal) {
            settings.CompressionFormat = CompressionFormat.UASTC;
            settings.UseCustomMipmaps = false; // Для --normal-mode
        } else {
            settings.CompressionFormat = CompressionFormat.ETC1S;
            settings.UseCustomMipmaps = true;
        }

        return settings;
    }

    private async Task<GLBConversionResult> ConvertModelToGLBAsync(
        ModelResource model,
        string outputDir,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var result = new GLBConversionResult();
        var modelFileName = GetSafeFileName(model.Name ?? $"model_{model.ID}");

        try {
            var conversionSettings = new ModelConversionSettings {
                GenerateLods = options.GenerateLODs,
                ExcludeTextures = true, // Текстуры обрабатываем отдельно
                CompressionMode = CompressionMode.MeshOpt,
                CleanupIntermediateFiles = true,
                GenerateManifest = false, // Не генерируем отдельный манифест
                GenerateQAReport = false  // Не генерируем отчёты для каждого LOD
            };

            if (options.GenerateLODs) {
                conversionSettings.LodChain = new List<LodSettings> {
                    new LodSettings { Level = LodLevel.LOD0, SimplificationRatio = 1.0f },
                    new LodSettings { Level = LodLevel.LOD1, SimplificationRatio = 0.5f },
                    new LodSettings { Level = LodLevel.LOD2, SimplificationRatio = 0.25f }
                };
            }

            var conversionResult = await _modelConversionPipeline.ConvertAsync(
                model.Path!, outputDir, conversionSettings);

            if (conversionResult.Success) {
                result.MainModelPath = Path.Combine(outputDir, $"{modelFileName}_lod0.glb");
                foreach (var (level, path) in conversionResult.LodFiles) {
                    if (level != LodLevel.LOD0) {
                        result.LODPaths.Add(path);
                    }
                }
                Logger.Info($"Model converted: {model.Name}");
            } else {
                result.ErrorMessage = string.Join("; ", conversionResult.Errors);
                Logger.Error($"Model conversion failed: {result.ErrorMessage}");
            }

        } catch (Exception ex) {
            result.ErrorMessage = ex.Message;
            Logger.Error(ex, $"Failed to convert model {model.Name}");
        }

        return result;
    }

    private MaterialInstanceJson GenerateMaterialInstanceJson(
        MaterialResource material,
        Dictionary<int, string> texturePathMap,
        Dictionary<int, ORMExportResult> ormResults,
        ExportOptions options) {

        // Определяем master материал по blendType
        var masterName = material.BlendType switch {
            "0" or "NONE" => "pbr_opaque",
            "1" or "NORMAL" => "pbr_alpha",
            "2" or "ADDITIVE" => "pbr_additive",
            "3" or "PREMULTIPLIED" => "pbr_premul",
            _ => "pbr_opaque"
        };

        var instance = new MaterialInstanceJson {
            Master = masterName,
            Params = new MaterialParams {
                Diffuse = NormalizeColor(material.Diffuse),
                Metalness = material.UseMetalness ? material.Metalness : null,
                Gloss = material.Glossiness ?? material.Shininess,
                Emissive = NormalizeColor(material.Emissive),
                EmissiveIntensity = material.EmissiveIntensity,
                Opacity = material.Opacity,
                AlphaTest = material.AlphaTest,
                Bumpiness = material.BumpMapFactor,
                UseMetalness = material.UseMetalness ? true : null
            },
            Textures = new MaterialTextures()
        };

        // Заполняем текстуры с относительными путями
        if (material.DiffuseMapId.HasValue && texturePathMap.TryGetValue(material.DiffuseMapId.Value, out var diffusePath))
            instance.Textures.DiffuseMapPath = diffusePath;

        if (material.NormalMapId.HasValue && texturePathMap.TryGetValue(material.NormalMapId.Value, out var normalPath))
            instance.Textures.NormalMapPath = normalPath;

        if (material.EmissiveMapId.HasValue && texturePathMap.TryGetValue(material.EmissiveMapId.Value, out var emissivePath))
            instance.Textures.EmissiveMapPath = emissivePath;

        if (material.OpacityMapId.HasValue && texturePathMap.TryGetValue(material.OpacityMapId.Value, out var opacityPath))
            instance.Textures.OpacityMapPath = opacityPath;

        // ORM packed текстуры
        if (options.UsePackedTextures && ormResults.TryGetValue(material.ID, out var ormResult)) {
            switch (ormResult.PackingMode) {
                case ChannelPackingMode.OG:
                    instance.Textures.OgMapPath = ormResult.RelativePath;
                    break;
                case ChannelPackingMode.OGM:
                    instance.Textures.OgmMapPath = ormResult.RelativePath;
                    break;
                case ChannelPackingMode.OGMH:
                    instance.Textures.OgmhMapPath = ormResult.RelativePath;
                    break;
            }
        } else {
            // Отдельные текстуры
            if (material.AOMapId.HasValue && texturePathMap.TryGetValue(material.AOMapId.Value, out var aoPath))
                instance.Textures.AoMapPath = aoPath;
            if (material.GlossMapId.HasValue && texturePathMap.TryGetValue(material.GlossMapId.Value, out var glossPath))
                instance.Textures.GlossMapPath = glossPath;
            if (material.MetalnessMapId.HasValue && texturePathMap.TryGetValue(material.MetalnessMapId.Value, out var metalPath))
                instance.Textures.MetalnessMapPath = metalPath;
        }

        return instance;
    }

    /// <summary>
    /// Генерирует материал JSON с ID текстур (для mapping.json архитектуры)
    /// </summary>
    private object GenerateMaterialJsonWithIds(
        MaterialResource material,
        Dictionary<int, ORMExportResult> ormResults,
        ExportOptions options) {

        // Определяем master материал по blendType
        var masterName = material.BlendType switch {
            "0" or "NONE" => "pbr_opaque",
            "1" or "NORMAL" => "pbr_alpha",
            "2" or "ADDITIVE" => "pbr_additive",
            "3" or "PREMULTIPLIED" => "pbr_premul",
            _ => "pbr_opaque"
        };

        var textures = new Dictionary<string, object>();

        // Стандартные текстуры - просто ID
        if (material.DiffuseMapId.HasValue)
            textures["diffuseMap"] = material.DiffuseMapId.Value;

        if (material.NormalMapId.HasValue)
            textures["normalMap"] = material.NormalMapId.Value;

        if (material.EmissiveMapId.HasValue)
            textures["emissiveMap"] = material.EmissiveMapId.Value;

        if (material.OpacityMapId.HasValue)
            textures["opacityMap"] = material.OpacityMapId.Value;

        // ORM packed текстуры - формат { "asset": id }
        if (options.UsePackedTextures && ormResults.TryGetValue(material.ID, out var ormResult)) {
            var ormTextureId = -material.ID; // Отрицательный ID для сгенерированных ORM
            var ormKey = ormResult.PackingMode switch {
                ChannelPackingMode.OG => "_og",
                ChannelPackingMode.OGM => "_ogm",
                ChannelPackingMode.OGMH => "_ogmh",
                _ => "_ogm"
            };
            textures[ormKey] = new { asset = ormTextureId };
        } else {
            // Отдельные текстуры - формат { "asset": id }
            if (material.AOMapId.HasValue)
                textures["aoMap"] = new { asset = material.AOMapId.Value };

            if (material.GlossMapId.HasValue)
                textures["glossMap"] = new { asset = material.GlossMapId.Value };

            if (material.MetalnessMapId.HasValue)
                textures["metalnessMap"] = new { asset = material.MetalnessMapId.Value };
        }

        return new {
            master = masterName,
            @params = new {
                diffuse = NormalizeColor(material.Diffuse),
                metalness = material.UseMetalness ? material.Metalness : (float?)null,
                gloss = material.Glossiness ?? material.Shininess,
                emissive = NormalizeColor(material.Emissive),
                emissiveIntensity = material.EmissiveIntensity,
                opacity = material.Opacity,
                alphaTest = material.AlphaTest,
                bumpiness = material.BumpMapFactor,
                useMetalness = material.UseMetalness ? true : (bool?)null
            },
            textures = textures.Count > 0 ? textures : null
        };
    }

    private float[]? NormalizeColor(List<float>? color) {
        if (color == null || color.Count == 0) return null;
        var result = new float[3];
        for (int i = 0; i < Math.Min(3, color.Count); i++) {
            result[i] = color[i];
        }
        return result;
    }

    private string ExtractBaseName(string? name) {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(name);
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
        var sanitized = string.Join("_", path.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Replace('\\', '/');
    }

    /// <summary>
    /// Обновляет глобальный mapping.json в папке server/
    /// </summary>
    private async Task UpdateGlobalMappingAsync(
        ModelResource model,
        List<MaterialResource> modelMaterials,
        string modelFileName,
        string modelFolderPath,
        Dictionary<int, string> texturePathMap,
        Dictionary<int, ORMExportResult> ormResults,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var mappingPath = GetMappingPath();
        Directory.CreateDirectory(GetServerPath());

        // Загружаем существующий mapping.json или создаём новый
        var existingMapping = new MappingData();
        if (File.Exists(mappingPath)) {
            try {
                var existingJson = await File.ReadAllTextAsync(mappingPath, cancellationToken);
                existingMapping = JsonSerializer.Deserialize<MappingData>(existingJson, new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }) ?? new MappingData();
            } catch (Exception ex) {
                Logger.Warn($"Failed to load existing mapping.json, creating new: {ex.Message}");
            }
        }

        // Путь относительно server/ папки
        var contentBasePath = "assets/content";
        var modelPath = $"{contentBasePath}/{modelFolderPath}".Replace('\\', '/');

        // Добавляем модель
        var lodDistances = new[] { 0, 25, 60 };
        var lods = new List<LodEntry> {
            new LodEntry { Level = 0, File = $"{modelPath}/{modelFileName}_lod0.glb", Distance = lodDistances[0] }
        };
        if (options.GenerateLODs) {
            lods.Add(new LodEntry { Level = 1, File = $"{modelPath}/{modelFileName}_lod1.glb", Distance = lodDistances[1] });
            lods.Add(new LodEntry { Level = 2, File = $"{modelPath}/{modelFileName}_lod2.glb", Distance = lodDistances[2] });
        }

        existingMapping.Models[model.ID.ToString()] = new ModelEntry {
            Name = model.Name ?? modelFileName,
            Path = modelPath,
            Materials = modelMaterials.Select(m => m.ID).ToList(),
            Lods = lods
        };

        // Добавляем материалы
        foreach (var material in modelMaterials) {
            var matFileName = GetSafeFileName(material.Name ?? $"mat_{material.ID}") + ".json";
            existingMapping.Materials[material.ID.ToString()] = $"{modelPath}/{matFileName}";
        }

        // Добавляем текстуры
        foreach (var (textureId, relativePath) in texturePathMap) {
            existingMapping.Textures[textureId.ToString()] = $"{modelPath}/{relativePath}".Replace('\\', '/');
        }

        // Добавляем ORM текстуры
        foreach (var (materialId, ormResult) in ormResults) {
            var ormTextureId = -materialId;
            existingMapping.Textures[ormTextureId.ToString()] = $"{modelPath}/{ormResult.RelativePath}".Replace('\\', '/');
        }

        // Сохраняем обновлённый mapping.json
        var mappingJson = JsonSerializer.Serialize(existingMapping, new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(mappingPath, mappingJson, cancellationToken);
    }

    private void ReportProgress(string message, int percentage) {
        Logger.Info($"[{percentage}%] {message}");
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
    public int TextureQuality { get; set; } = 128;
    public bool ApplyToksvig { get; set; } = true;
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
    public string? ErrorMessage { get; set; }
}

public class ORMExportResult {
    public int MaterialId { get; set; }
    public ChannelPackingMode PackingMode { get; set; }
    public string OutputPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
}

/// <summary>
/// Структура глобального mapping.json
/// </summary>
public class MappingData {
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, ModelEntry> Models { get; set; } = new();
    public Dictionary<string, string> Materials { get; set; } = new();
    public Dictionary<string, string> Textures { get; set; } = new();
}

public class ModelEntry {
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<int> Materials { get; set; } = new();
    public List<LodEntry> Lods { get; set; } = new();
}

public class LodEntry {
    public int Level { get; set; }
    public string File { get; set; } = "";
    public int Distance { get; set; }
}

#endregion

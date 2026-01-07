using System.IO;
using System.Text.Json;
using AssetProcessor.Mapping;
using AssetProcessor.Mapping.Models;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;
using AssetProcessor.TextureConversion.Settings;
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

            // Генерируем JSON файл модели с LODs и материалами
            var modelJson = GenerateModelJson(model, modelMaterials, modelFileName, options);
            var modelJsonPath = Path.Combine(exportPath, $"{modelFileName}.json");
            var modelJsonStr = JsonSerializer.Serialize(modelJson, new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await File.WriteAllTextAsync(modelJsonPath, modelJsonStr, cancellationToken);
            result.GeneratedModelJson = modelJsonPath;
            Logger.Info($"Generated model JSON: {modelJsonPath}");

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

        Logger.Info($"=== GenerateORMTexturesAsync START ===");
        Logger.Info($"  Materials count: {materials.Count}");
        Logger.Info($"  Textures count: {textures.Count}");
        Logger.Info($"  Output dir: {outputDir}");

        var results = new Dictionary<int, ORMExportResult>();
        var packedTextureIds = new HashSet<int>(); // Текстуры, которые были упакованы
        var textureDict = textures.ToDictionary(t => t.ID);

        foreach (var material in materials) {
            cancellationToken.ThrowIfCancellationRequested();

            Logger.Info($"--- Processing material: {material.Name} (ID={material.ID}) ---");
            Logger.Info($"  AOMapId: {material.AOMapId}, GlossMapId: {material.GlossMapId}, MetalnessMapId: {material.MetalnessMapId}");

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

            // Логируем найденные текстуры для ORM
            Logger.Info($"ORM check for material [{material.ID}] {material.Name}:");
            Logger.Info($"  AOMapId={material.AOMapId}, Found={aoTexture != null}, Path={aoTexture?.Path ?? "null"}, Exists={aoTexture?.Path != null && File.Exists(aoTexture.Path)}");
            Logger.Info($"  GlossMapId={material.GlossMapId}, Found={glossTexture != null}, Path={glossTexture?.Path ?? "null"}, Exists={glossTexture?.Path != null && File.Exists(glossTexture.Path)}");
            Logger.Info($"  MetalnessMapId={material.MetalnessMapId}, Found={metallicTexture != null}, Path={metallicTexture?.Path ?? "null"}, Exists={metallicTexture?.Path != null && File.Exists(metallicTexture.Path)}");

            // Определяем режим пакинга
            var packingMode = DeterminePackingMode(aoTexture, glossTexture, metallicTexture);
            Logger.Info($"  -> PackingMode = {packingMode}");
            if (packingMode == ChannelPackingMode.None) {
                Logger.Warn($"  -> SKIPPING ORM: insufficient textures with valid files");
                continue;
            }

            // Определяем имя ORM по GroupName текстур (как в UI)
            var groupName = aoTexture?.GroupName ?? glossTexture?.GroupName ?? metallicTexture?.GroupName;
            string baseName;
            if (!string.IsNullOrEmpty(groupName)) {
                // Убираем суффикс _mat из имени группы
                baseName = groupName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)
                    ? groupName[..^4]
                    : groupName;
            } else {
                // Fallback: используем имя материала без _mat
                baseName = material.Name ?? $"mat_{material.ID}";
                if (baseName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)) {
                    baseName = baseName[..^4];
                }
            }
            var ormFileName = GetSafeFileName(baseName);
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

                // Читаем сохранённые ORM настройки из ResourceSettingsService
                var ormName = ormFileName + suffix; // e.g., "oldMailBox_ogm"
                var ormCompressionSettings = BuildORMCompressionSettings(options, groupName, ormName, packingMode);

                // Конвертируем в KTX2
                var ktxResult = await _textureConversionPipeline.ConvertTextureAsync(
                    mipPaths[0], // Используем первый мипмап как источник
                    outputPath,
                    MipGenerationProfile.CreateDefault(TextureType.Generic),
                    ormCompressionSettings);

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

        // Используем настройки по умолчанию
        var settings = new ChannelPackingSettings { Mode = mode };

        // Настройка каналов зависит от режима упаковки:
        // OG:   RGB = AO, A = Gloss
        // OGM:  R = AO, G = Gloss, B = Metallic

        if (mode == ChannelPackingMode.OG) {
            // OG режим: RGB = AO, Alpha = Gloss
            if (aoTexture?.Path != null) {
                settings.RedChannel = new ChannelSourceSettings {
                    ChannelType = ChannelType.AmbientOcclusion,
                    SourcePath = aoTexture.Path
                };
            }

            if (glossTexture?.Path != null) {
                settings.AlphaChannel = new ChannelSourceSettings {
                    ChannelType = ChannelType.Gloss,
                    SourcePath = glossTexture.Path,
                    ApplyToksvig = options.ApplyToksvig
                };
            }
        } else {
            // OGM режим: R = AO, G = Gloss, B = Metallic
            if (aoTexture?.Path != null) {
                settings.RedChannel = new ChannelSourceSettings {
                    ChannelType = ChannelType.AmbientOcclusion,
                    SourcePath = aoTexture.Path
                };
            }

            if (glossTexture?.Path != null) {
                settings.GreenChannel = new ChannelSourceSettings {
                    ChannelType = ChannelType.Gloss,
                    SourcePath = glossTexture.Path,
                    ApplyToksvig = options.ApplyToksvig
                };
            }

            if (metallicTexture?.Path != null) {
                settings.BlueChannel = new ChannelSourceSettings {
                    ChannelType = ChannelType.Metallic,
                    SourcePath = metallicTexture.Path
                };
            }
        }

        return settings;
    }

    private ChannelPackingMode DeterminePackingMode(
        TextureResource? ao, TextureResource? gloss, TextureResource? metallic) {

        // Проверяем наличие текстуры И существование файла на диске
        bool hasAO = ao?.Path != null && File.Exists(ao.Path);
        bool hasGloss = gloss?.Path != null && File.Exists(gloss.Path);
        bool hasMetallic = metallic?.Path != null && File.Exists(metallic.Path);

        int channelCount = (hasAO ? 1 : 0) + (hasGloss ? 1 : 0) + (hasMetallic ? 1 : 0);

        // Минимум 2 канала для упаковки, иначе оставляем отдельно
        if (channelCount < 2) return ChannelPackingMode.None;

        // OGM: есть все 3 или любые 2 включая Metallic
        if (hasMetallic) return ChannelPackingMode.OGM;

        // OG: AO + Gloss (без Metallic)
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
                // Пробуем загрузить сохранённые настройки текстуры
                MipGenerationProfile profile;
                CompressionSettings compressionSettings;

                var savedSettings = options.UseSavedTextureSettings && options.ProjectId > 0
                    ? ResourceSettingsService.Instance.GetTextureSettings(options.ProjectId, texture.ID)
                    : null;

                if (savedSettings != null) {
                    // Используем сохранённые настройки
                    Logger.Info($"Using saved settings for texture {texture.Name} (ID={texture.ID})");
                    (profile, compressionSettings) = BuildSettingsFromSaved(savedSettings);
                } else {
                    // Fallback: ищем подходящий пресет по имени файла
                    var presetManager = new PresetManager();
                    var matchingPreset = presetManager.FindPresetByFileName(texture.Name ?? "");

                    if (matchingPreset != null) {
                        // Нашли пресет по постфиксу имени файла
                        var textureType = DetermineTextureType(texture);
                        profile = matchingPreset.ToMipGenerationProfile(textureType);
                        compressionSettings = matchingPreset.ToCompressionSettings();
                        Logger.Info($"Using preset '{matchingPreset.Name}' for texture {texture.Name}: Format={compressionSettings.CompressionFormat}");
                    } else {
                        // Не нашли - используем Default ETC1S
                        var defaultPreset = TextureConversionPreset.CreateDefaultETC1S();
                        var textureType = DetermineTextureType(texture);
                        profile = defaultPreset.ToMipGenerationProfile(textureType);
                        compressionSettings = defaultPreset.ToCompressionSettings();
                        Logger.Info($"Using default preset for texture {texture.Name}: Type={textureType}, Format={compressionSettings.CompressionFormat}");
                    }
                }

                var ktxResult = await _textureConversionPipeline.ConvertTextureAsync(
                    texture.Path,
                    outputPath,
                    profile,
                    compressionSettings);

                if (ktxResult.Success) {
                    // Сохраняем относительный путь
                    var relativePath = Path.GetRelativePath(exportPath, outputPath).Replace('\\', '/');
                    results[texture.ID] = relativePath;
                    Logger.Info($"Converted texture: {texture.Name} -> {outputPath} (Format={compressionSettings.CompressionFormat})");
                } else {
                    Logger.Error($"Failed to convert texture {texture.Name}: {ktxResult.Error}");
                }

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to convert texture {texture.Name}");
            }
        }

        return results;
    }

    /// <summary>
    /// Строит MipGenerationProfile и CompressionSettings из сохранённых настроек
    /// </summary>
    private (MipGenerationProfile profile, CompressionSettings compression) BuildSettingsFromSaved(TextureSettings saved) {
        // Parse texture type
        var textureType = Enum.TryParse<TextureType>(saved.TextureType, true, out var tt) ? tt : TextureType.Generic;

        // Parse filter type
        var filterType = Enum.TryParse<FilterType>(saved.FilterType, true, out var ft) ? ft : FilterType.Kaiser;

        // Build MipGenerationProfile
        var profile = new MipGenerationProfile {
            TextureType = textureType,
            Filter = filterType,
            ApplyGammaCorrection = saved.ApplyGammaCorrection,
            Gamma = saved.Gamma,
            BlurRadius = saved.BlurRadius,
            MinMipSize = saved.MinMipSize,
            NormalizeNormals = saved.NormalizeNormals,
            UseEnergyPreserving = saved.UseEnergyPreserving
        };

        // Parse compression format
        var compressionFormat = Enum.TryParse<CompressionFormat>(saved.CompressionFormat, true, out var cf)
            ? cf : CompressionFormat.ETC1S;

        // Parse color space
        var colorSpace = Enum.TryParse<ColorSpace>(saved.ColorSpace, true, out var cs)
            ? cs : ColorSpace.Auto;

        // Parse supercompression
        var supercompression = Enum.TryParse<KTX2SupercompressionType>(saved.KTX2Supercompression, true, out var sc)
            ? sc : KTX2SupercompressionType.Zstandard;

        // Parse wrap mode
        var wrapMode = Enum.TryParse<WrapMode>(saved.WrapMode, true, out var wm)
            ? wm : WrapMode.Clamp;

        // Build CompressionSettings
        var compression = new CompressionSettings {
            CompressionFormat = compressionFormat,
            ColorSpace = colorSpace,
            GenerateMipmaps = saved.GenerateMipmaps,
            UseCustomMipmaps = saved.UseCustomMipmaps,

            // ETC1S settings
            CompressionLevel = saved.CompressionLevel,
            QualityLevel = saved.QualityLevel,
            UseETC1SRDO = saved.UseETC1SRDO,
            ETC1SRDOLambda = saved.ETC1SRDOLambda,

            // UASTC settings
            UASTCQuality = saved.UASTCQuality,
            UseUASTCRDO = saved.UseUASTCRDO,
            UASTCRDOQuality = saved.UASTCRDOQuality,

            // Supercompression
            KTX2Supercompression = supercompression,
            KTX2ZstdLevel = saved.KTX2ZstdLevel,

            // Normal map
            ConvertToNormalMap = saved.ConvertToNormalMap,
            NormalizeVectors = saved.NormalizeVectors,

            // Other
            PerceptualMode = saved.PerceptualMode,
            SeparateAlpha = saved.SeparateAlpha,
            ForceAlphaChannel = saved.ForceAlphaChannel,
            RemoveAlphaChannel = saved.RemoveAlphaChannel,
            WrapMode = wrapMode
        };

        // Histogram settings
        if (saved.HistogramEnabled) {
            var histogramMode = Enum.TryParse<HistogramMode>(saved.HistogramMode, true, out var hm)
                ? hm : HistogramMode.Off;
            var histogramQuality = Enum.TryParse<HistogramQuality>(saved.HistogramQuality, true, out var hq)
                ? hq : HistogramQuality.HighQuality;
            var histogramChannelMode = Enum.TryParse<HistogramChannelMode>(saved.HistogramChannelMode, true, out var hcm)
                ? hcm : HistogramChannelMode.PerChannel;

            compression.HistogramAnalysis = new HistogramSettings {
                Mode = histogramMode,
                Quality = histogramQuality,
                ChannelMode = histogramChannelMode,
                PercentileLow = saved.HistogramPercentileLow,
                PercentileHigh = saved.HistogramPercentileHigh,
                KneeWidth = saved.HistogramKneeWidth
            };
        }

        return (profile, compression);
    }

    /// <summary>
    /// Создаёт CompressionSettings для ORM текстуры из сохранённых настроек
    /// </summary>
    private CompressionSettings BuildORMCompressionSettings(ExportOptions options, string? groupName, string ormName, ChannelPackingMode packingMode) {
        // Пытаемся получить сохранённые ORM настройки
        Services.ORMTextureSettings? savedOrm = null;
        if (options.ProjectId > 0 && !string.IsNullOrEmpty(groupName)) {
            // Ключ в формате из MainWindow.Api.cs: orm_{groupName}_{ormName}
            var keysToTry = new[] {
                $"orm_{groupName}_{ormName}",           // Точный формат из UI (orm_oldMailBox_mat_oldMailBox_ogm)
                $"orm_{ormName}_{ormName}",             // Альтернатива без _mat в groupName
            };

            foreach (var key in keysToTry) {
                savedOrm = Services.ResourceSettingsService.Instance.GetORMTextureSettings(options.ProjectId, key);
                if (savedOrm != null) {
                    Logger.Info($"Found ORM settings with key '{key}': Format={savedOrm.CompressionFormat}, Quality={savedOrm.QualityLevel}");
                    break;
                }
            }
        }

        if (savedOrm != null) {
            // Используем сохранённые настройки
            var format = Enum.TryParse<CompressionFormat>(savedOrm.CompressionFormat, true, out var cf)
                ? cf : CompressionFormat.ETC1S;

            var supercompression = savedOrm.EnableSupercompression
                ? KTX2SupercompressionType.Zstandard
                : KTX2SupercompressionType.None;

            return new CompressionSettings {
                CompressionFormat = format,
                ColorSpace = ColorSpace.Linear, // ORM всегда linear
                CompressionLevel = savedOrm.CompressLevel,
                QualityLevel = savedOrm.QualityLevel,
                UASTCQuality = savedOrm.UASTCQuality,
                UseUASTCRDO = format == CompressionFormat.UASTC && savedOrm.EnableRDO,
                UASTCRDOQuality = savedOrm.RDOLambda,
                UseETC1SRDO = format == CompressionFormat.ETC1S && savedOrm.EnableRDO,
                ETC1SRDOLambda = savedOrm.RDOLambda,
                PerceptualMode = savedOrm.Perceptual,
                KTX2Supercompression = supercompression,
                KTX2ZstdLevel = savedOrm.SupercompressionLevel,
                GenerateMipmaps = true,
                UseCustomMipmaps = false // ORM использует внешнюю генерацию мипмапов через ChannelPackingPipeline
            };
        }

        // Fallback: используем default настройки
        Logger.Info($"No saved ORM settings for '{ormName}', using defaults: ETC1S, Quality={options.TextureQuality}");
        return new CompressionSettings {
            CompressionFormat = CompressionFormat.ETC1S,
            ColorSpace = ColorSpace.Linear,
            QualityLevel = options.TextureQuality,
            GenerateMipmaps = true,
            UseCustomMipmaps = false
        };
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
    /// Генерирует материал JSON с ID текстур и путями для ORM
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

        // ORM packed текстуры - путь (т.к. создан нами, не PlayCanvas)
        if (options.UsePackedTextures && ormResults.TryGetValue(material.ID, out var ormResult)) {
            var ormKey = ormResult.PackingMode switch {
                ChannelPackingMode.OG => "_og",
                ChannelPackingMode.OGM => "_ogm",
                ChannelPackingMode.OGMH => "_ogmh",
                _ => "_ogm"
            };
            // Используем относительный путь вместо ID
            textures[ormKey] = ormResult.RelativePath;
        } else {
            // Отдельные текстуры - формат { "asset": id }
            if (material.AOMapId.HasValue)
                textures["aoMap"] = new { asset = material.AOMapId.Value };

            if (material.GlossMapId.HasValue)
                textures["glossMap"] = new { asset = material.GlossMapId.Value };

            if (material.MetalnessMapId.HasValue)
                textures["metalnessMap"] = new { asset = material.MetalnessMapId.Value };
        }

        // Собираем параметры материала
        var matParams = new Dictionary<string, object?>();

        // Базовые цвета
        if (material.Diffuse != null && material.Diffuse.Count >= 3)
            matParams["diffuse"] = NormalizeColor(material.Diffuse);

        if (material.Emissive != null && material.Emissive.Count >= 3)
            matParams["emissive"] = NormalizeColor(material.Emissive);

        if (material.Specular != null && material.Specular.Count >= 3)
            matParams["specular"] = NormalizeColor(material.Specular);

        // Tint флаги и значения
        if (material.DiffuseTint) {
            matParams["diffuseTint"] = true;
            if (material.Diffuse != null)
                matParams["diffuseMapTintColor"] = NormalizeColor(material.Diffuse);
        }

        if (material.SpecularTint) {
            matParams["specularTint"] = true;
            if (material.Specular != null)
                matParams["specularMapTintColor"] = NormalizeColor(material.Specular);
        }

        // Числовые параметры
        if (material.UseMetalness) {
            matParams["useMetalness"] = true;
            if (material.Metalness.HasValue)
                matParams["metalness"] = material.Metalness.Value;
        }

        if (material.Glossiness.HasValue)
            matParams["gloss"] = material.Glossiness.Value;
        else if (material.Shininess.HasValue)
            matParams["gloss"] = material.Shininess.Value;

        if (material.EmissiveIntensity.HasValue && material.EmissiveIntensity.Value != 1.0f)
            matParams["emissiveIntensity"] = material.EmissiveIntensity.Value;

        if (material.Opacity.HasValue && material.Opacity.Value != 1.0f)
            matParams["opacity"] = material.Opacity.Value;

        if (material.AlphaTest.HasValue && material.AlphaTest.Value > 0)
            matParams["alphaTest"] = material.AlphaTest.Value;

        if (material.BumpMapFactor.HasValue && material.BumpMapFactor.Value != 1.0f)
            matParams["bumpiness"] = material.BumpMapFactor.Value;

        // Дополнительные параметры
        if (material.AOTint)
            matParams["aoTint"] = true;

        if (material.Reflectivity.HasValue && material.Reflectivity.Value != 1.0f)
            matParams["reflectivity"] = material.Reflectivity.Value;

        if (material.RefractionIndex.HasValue && material.RefractionIndex.Value != 0)
            matParams["refraction"] = material.RefractionIndex.Value;

        if (!string.IsNullOrEmpty(material.FresnelModel))
            matParams["fresnelModel"] = material.FresnelModel;

        // Cull mode
        if (!string.IsNullOrEmpty(material.Cull) && material.Cull != "1")
            matParams["cull"] = material.Cull;

        // Two sided lighting
        if (material.TwoSidedLighting)
            matParams["twoSidedLighting"] = true;

        return new {
            master = masterName,
            @params = matParams.Count > 0 ? matParams : null,
            textures = textures.Count > 0 ? textures : null
        };
    }

    /// <summary>
    /// Генерирует JSON файл модели с LODs и материалами
    /// </summary>
    private object GenerateModelJson(
        ModelResource model,
        List<MaterialResource> modelMaterials,
        string modelFileName,
        ExportOptions options) {

        var lods = new List<object>();

        // LOD0 всегда есть
        lods.Add(new {
            path = $"{modelFileName}_lod0.glb",
            maxDistance = 50
        });

        if (options.GenerateLODs) {
            lods.Add(new {
                path = $"{modelFileName}_lod1.glb",
                maxDistance = 150
            });
            lods.Add(new {
                path = $"{modelFileName}_lod2.glb",
                maxDistance = (int?)null // null = loadFirst (последний LOD, загружается первым)
            });
        }

        return new {
            name = model.Name ?? modelFileName,
            materials = modelMaterials.Select(m => m.ID).ToList(),
            lods = lods
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
    /// <summary>
    /// ID проекта PlayCanvas для загрузки сохранённых настроек ресурсов
    /// </summary>
    public int ProjectId { get; set; }

    public bool ConvertModel { get; set; } = true;
    public bool ConvertTextures { get; set; } = true;
    public bool GenerateORMTextures { get; set; } = true;
    public bool UsePackedTextures { get; set; } = true;
    public bool GenerateLODs { get; set; } = true;
    public int LODLevels { get; set; } = 2;
    public int TextureQuality { get; set; } = 128;
    public bool ApplyToksvig { get; set; } = true;

    /// <summary>
    /// Использовать сохранённые настройки текстур из ResourceSettingsService
    /// Если false - используются автоматические настройки на основе типа текстуры
    /// </summary>
    public bool UseSavedTextureSettings { get; set; } = true;
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
    public string? GeneratedModelJson { get; set; }
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

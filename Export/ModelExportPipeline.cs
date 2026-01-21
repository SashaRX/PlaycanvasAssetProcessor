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
            ReportProgress("Export", "Starting", 0, model.Name);

            // 1. Определяем путь экспорта из иерархии PlayCanvas
            var modelFolderPath = GetResourceFolderPath(model, folderPaths);
            var exportPath = Path.Combine(GetContentBasePath(), modelFolderPath);
            result.ExportPath = exportPath;

            Directory.CreateDirectory(exportPath);
            Logger.Info($"Export path: {exportPath}");

            // 2. Находим материалы, принадлежащие этой модели
            ReportProgress("Search", "Finding materials", 10, model.Name);
            var modelMaterials = FindMaterialsForModel(model, allMaterials, folderPaths);
            result.MaterialCount = modelMaterials.Count;
            Logger.Info($"Found {modelMaterials.Count} materials for model {model.Name} (Parent={model.Parent})");

            // Логируем найденные материалы
            foreach (var mat in modelMaterials) {
                Logger.Info($"  Material [{mat.ID}] {mat.Name}: Diffuse={mat.DiffuseMapId}, Normal={mat.NormalMapId}, AO={mat.AOMapId}, Gloss={mat.GlossMapId}, Metal={mat.MetalnessMapId}");
            }

            // 3. Собираем все текстуры из материалов
            ReportProgress("Search", "Collecting textures", 15, model.Name);
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
            if (options.GenerateORMTextures && modelMaterials.Count > 0) {
                ReportProgress("ORM", "Generating packed textures", 20, null, 0, modelMaterials.Count);
                (ormResults, packedTextureIds) = await GenerateORMTexturesAsync(
                    modelMaterials, modelTextures, texturesDir, options, cancellationToken, 20, 25);
                result.GeneratedORMTextures.AddRange(ormResults.Values.Select(r => r.OutputPath));
                Logger.Info($"ORM packed {packedTextureIds.Count} textures into {ormResults.Count} ORM files");
            }

            // 6. Конвертируем обычные текстуры в KTX2 (пропускаем те, что уже в ORM)
            var texturePathMap = new Dictionary<int, string>(); // TextureId -> relative path
            if (options.ConvertTextures) {
                var texturesToConvert = modelTextures.Where(t => !packedTextureIds.Contains(t.ID)).ToList();
                if (texturesToConvert.Count > 0) {
                    ReportProgress("KTX2", "Converting textures", 45, null, 0, texturesToConvert.Count);
                    Logger.Info($"Converting {texturesToConvert.Count} textures (skipping {packedTextureIds.Count} packed in ORM)");
                    texturePathMap = await ConvertTexturesToKTX2Async(
                        texturesToConvert, texturesDir, exportPath, options, cancellationToken, 45, 25);
                    result.ConvertedTextures.AddRange(texturePathMap.Values);
                }
            }

            // 7. Генерируем material JSON файлы и обновляем глобальный mapping.json
            ReportProgress("JSON", "Generating material files", 70, model.Name);
            var modelFileName = GetSafeFileName(model.Name ?? $"model_{model.ID}");
            var materialIds = new List<int>();

            // Базовый путь относительно server/ для mapping.json
            var assetsContentPath = "assets/content";
            var modelRelativePath = $"{assetsContentPath}/{modelFolderPath}";

            // Генерируем отдельный JSON для каждого материала
            foreach (var material in modelMaterials) {
                cancellationToken.ThrowIfCancellationRequested();

                var materialJson = GenerateMaterialJsonWithPaths(material, texturePathMap, ormResults, options);
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

            // 7.5 Генерируем consolidated chunks файлы для master materials с chunks
            if (options.MasterMaterialsConfig != null && !string.IsNullOrEmpty(options.ProjectFolderPath)) {
                var chunksFiles = await GenerateChunksFilesAsync(modelMaterials, exportPath, options, cancellationToken);
                result.GeneratedChunksFiles.AddRange(chunksFiles);
            }

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
                ReportProgress("GLB", "Converting FBX to GLB", 85, model.Name);
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

                if (options.GenerateLODs && glbResult.LODPaths.Count > 0) {
                    ReportProgress("LOD", "Generated LODs", 95, model.Name, glbResult.LODPaths.Count, glbResult.LODPaths.Count);
                }
            }

            // Сохраняем IDs обработанных ресурсов для обновления статусов
            result.ProcessedMaterialIds.AddRange(modelMaterials.Select(m => m.ID));
            result.ProcessedTextureIds.AddRange(modelTextures.Select(t => t.ID));

            result.Success = true;
            ReportProgress("Done", "Export completed", 100, model.Name);

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
        // Используем ТОЛЬКО путь из resource.Path - та же структура что и оригинал
        if (!string.IsNullOrEmpty(resource.Path)) {
            var dir = Path.GetDirectoryName(resource.Path)!;
            // Нормализуем к forward slash для поиска
            var normalizedDir = dir.Replace('\\', '/');

            string result = "";

            // Ищем /assets/content/ в пути -> оставляем только models/props/...
            var assetsContentIndex = normalizedDir.IndexOf("/assets/content/", StringComparison.OrdinalIgnoreCase);
            if (assetsContentIndex >= 0) {
                result = normalizedDir.Substring(assetsContentIndex + 16); // +16 = "/assets/content/"
            } else {
                // Fallback: ищем /server/content/ (старая структура экспорта)
                var serverContentIndex = normalizedDir.IndexOf("/server/content/", StringComparison.OrdinalIgnoreCase);
                if (serverContentIndex >= 0) {
                    result = normalizedDir.Substring(serverContentIndex + 16); // +16 = "/server/content/"
                } else {
                    // Fallback: ищем /content/ (общий случай)
                    var contentIndex = normalizedDir.IndexOf("/content/", StringComparison.OrdinalIgnoreCase);
                    if (contentIndex >= 0) {
                        result = normalizedDir.Substring(contentIndex + 9); // +9 = "/content/"
                    } else {
                        // Fallback: ищем /assets/
                        var assetsIndex = normalizedDir.IndexOf("/assets/", StringComparison.OrdinalIgnoreCase);
                        if (assetsIndex >= 0) {
                            result = normalizedDir.Substring(assetsIndex + 8);
                        }
                    }
                }
            }

            // Нормализуем к платформенному разделителю для Path.Combine
            return result.Replace('/', Path.DirectorySeparatorChar);
        }

        return "";
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
        CancellationToken cancellationToken,
        int basePercentage = 30,
        int percentageRange = 20) {

        Logger.Info($"=== GenerateORMTexturesAsync START ===");
        Logger.Info($"  Materials count: {materials.Count}");
        Logger.Info($"  Textures count: {textures.Count}");
        Logger.Info($"  Output dir: {outputDir}");

        var results = new Dictionary<int, ORMExportResult>();
        var packedTextureIds = new HashSet<int>(); // Текстуры, которые были упакованы
        var textureDict = textures.ToDictionary(t => t.ID);
        var totalMaterials = materials.Count;

        for (int matIndex = 0; matIndex < materials.Count; matIndex++) {
            var material = materials[matIndex];
            cancellationToken.ThrowIfCancellationRequested();

            // Report progress
            var subProgress = basePercentage + (int)((double)matIndex / totalMaterials * percentageRange);
            ReportProgress("ORM", "Packing channels", subProgress, material.Name, matIndex + 1, totalMaterials);

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
        CancellationToken cancellationToken,
        int basePercentage = 50,
        int percentageRange = 20) {

        var results = new Dictionary<int, string>();
        var totalTextures = textures.Count;

        for (int i = 0; i < textures.Count; i++) {
            var texture = textures[i];
            cancellationToken.ThrowIfCancellationRequested();

            // Report progress for this texture
            var subProgress = basePercentage + (int)((double)i / totalTextures * percentageRange);
            ReportProgress("KTX2", "Converting texture", subProgress, texture.Name, i + 1, totalTextures);

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
    /// Генерирует материал JSON с путями текстур
    /// </summary>
    private object GenerateMaterialJsonWithPaths(
        MaterialResource material,
        Dictionary<int, string> texturePathMap,
        Dictionary<int, ORMExportResult> ormResults,
        ExportOptions options) {

        // Приоритет определения master материала:
        // 1. MasterMaterialName из UI выбора (индивидуально для материала)
        // 2. DefaultMasterMaterial из options (глобальный default)
        // 3. Fallback по blendType
        string masterName;
        if (!string.IsNullOrEmpty(material.MasterMaterialName)) {
            masterName = material.MasterMaterialName;
        } else if (!string.IsNullOrEmpty(options.DefaultMasterMaterial)) {
            masterName = options.DefaultMasterMaterial;
        } else {
            // Определяем master материал по blendType
            masterName = material.BlendType switch {
                "0" or "NONE" => "pbr_opaque",
                "1" or "NORMAL" => "pbr_alpha",
                "2" or "ADDITIVE" => "pbr_additive",
                "3" or "PREMULTIPLIED" => "pbr_premul",
                _ => "pbr_opaque"
            };
        }

        // Проверяем, есть ли chunks для этого master
        string? chunksFile = null;
        if (options.MasterMaterialsConfig != null) {
            var master = options.MasterMaterialsConfig.Masters
                .FirstOrDefault(m => m.Name == masterName);
            if (master != null && master.Chunks.Count > 0) {
                // Reference the master's chunks folder
                chunksFile = $"{masterName}/chunks";
            }
        }

        var textures = new Dictionary<string, object>();

        // Стандартные текстуры - пути
        if (material.DiffuseMapId.HasValue && texturePathMap.TryGetValue(material.DiffuseMapId.Value, out var diffusePath))
            textures["diffuseMap"] = diffusePath;

        if (material.NormalMapId.HasValue && texturePathMap.TryGetValue(material.NormalMapId.Value, out var normalPath))
            textures["normalMap"] = normalPath;

        if (material.EmissiveMapId.HasValue && texturePathMap.TryGetValue(material.EmissiveMapId.Value, out var emissivePath))
            textures["emissiveMap"] = emissivePath;

        if (material.OpacityMapId.HasValue && texturePathMap.TryGetValue(material.OpacityMapId.Value, out var opacityPath))
            textures["opacityMap"] = opacityPath;

        // ORM packed текстуры - путь
        if (options.UsePackedTextures && ormResults.TryGetValue(material.ID, out var ormResult)) {
            var ormKey = ormResult.PackingMode switch {
                ChannelPackingMode.OG => "ogMap",
                ChannelPackingMode.OGM => "ogmMap",
                ChannelPackingMode.OGMH => "ogmhMap",
                _ => "ogmMap"
            };
            textures[ormKey] = ormResult.RelativePath;
        } else {
            // Отдельные текстуры - пути
            if (material.AOMapId.HasValue && texturePathMap.TryGetValue(material.AOMapId.Value, out var aoPath))
                textures["aoMap"] = aoPath;

            if (material.GlossMapId.HasValue && texturePathMap.TryGetValue(material.GlossMapId.Value, out var glossPath))
                textures["glossMap"] = glossPath;

            if (material.MetalnessMapId.HasValue && texturePathMap.TryGetValue(material.MetalnessMapId.Value, out var metalPath))
                textures["metalnessMap"] = metalPath;
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

        // Формируем результат с опциональным chunksFile
        var result = new Dictionary<string, object?> {
            ["master"] = masterName,
            ["params"] = matParams.Count > 0 ? matParams : null,
            ["textures"] = textures.Count > 0 ? textures : null
        };

        if (!string.IsNullOrEmpty(chunksFile)) {
            result["chunksFile"] = chunksFile;
        }

        return result;
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

    /// <summary>
    /// Генерирует consolidated chunks файлы для master materials, используемых материалами
    /// </summary>
    /// <returns>Список путей к сгенерированным chunks файлам</returns>
    private async Task<List<string>> GenerateChunksFilesAsync(
        List<MaterialResource> materials,
        string exportPath,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var generatedFiles = new List<string>();

        if (options.MasterMaterialsConfig == null || string.IsNullOrEmpty(options.ProjectFolderPath)) {
            return generatedFiles;
        }

        // Собираем уникальные master names с chunks
        var masterNamesWithChunks = new HashSet<string>();
        foreach (var material in materials) {
            var masterName = material.MasterMaterialName;
            if (string.IsNullOrEmpty(masterName)) {
                // Используем DefaultMasterMaterial из options если задан
                if (!string.IsNullOrEmpty(options.DefaultMasterMaterial)) {
                    masterName = options.DefaultMasterMaterial;
                } else {
                    // Fallback to blendType
                    masterName = material.BlendType switch {
                        "0" or "NONE" => "pbr_opaque",
                        "1" or "NORMAL" => "pbr_alpha",
                        "2" or "ADDITIVE" => "pbr_additive",
                        "3" or "PREMULTIPLIED" => "pbr_premul",
                        _ => "pbr_opaque"
                    };
                }
            }

            var master = options.MasterMaterialsConfig.Masters
                .FirstOrDefault(m => m.Name == masterName);
            if (master != null && master.Chunks.Count > 0) {
                masterNamesWithChunks.Add(masterName);
            }
        }

        if (masterNamesWithChunks.Count == 0) {
            return generatedFiles;
        }

        // Создаём папку chunks
        var chunksDir = Path.Combine(exportPath, "chunks");
        Directory.CreateDirectory(chunksDir);

        // Генерируем consolidated MJS для каждого master
        var masterMaterialService = new Services.MasterMaterialService();
        foreach (var masterName in masterNamesWithChunks) {
            cancellationToken.ThrowIfCancellationRequested();

            var master = options.MasterMaterialsConfig.Masters.First(m => m.Name == masterName);
            var consolidatedMjs = await masterMaterialService.GenerateConsolidatedChunksAsync(
                options.ProjectFolderPath, master, cancellationToken);

            var outputPath = Path.Combine(chunksDir, $"{masterName}_chunks.mjs");
            await File.WriteAllTextAsync(outputPath, consolidatedMjs, cancellationToken);
            generatedFiles.Add(outputPath);
            Logger.Info($"Generated chunks file: {outputPath}");
        }

        return generatedFiles;
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

    #endregion

    #region Standalone Export Methods

    /// <summary>
    /// Экспортирует материалы без привязки к модели
    /// </summary>
    public async Task<MaterialExportResult> ExportMaterialAsync(
        MaterialResource material,
        IEnumerable<TextureResource> allTextures,
        IReadOnlyDictionary<int, string> folderPaths,
        ExportOptions options,
        CancellationToken cancellationToken = default) {

        var result = new MaterialExportResult {
            MaterialId = material.ID,
            MaterialName = material.Name ?? $"material_{material.ID}"
        };

        try {
            ReportProgress("Material", "Exporting", 0, material.Name);

            // Определяем путь экспорта - та же структура что и в оригинале
            var materialFolderPath = GetResourceFolderPath(material, folderPaths);
            var exportPath = Path.Combine(GetContentBasePath(), materialFolderPath);
            result.ExportPath = exportPath;

            Directory.CreateDirectory(exportPath);
            Logger.Info($"Material export path: {exportPath}");

            var ormResults = new Dictionary<int, ORMExportResult>();
            var texturePathMap = new Dictionary<int, string>();

            // Если MaterialJsonOnly - пропускаем все текстуры, экспортируем только JSON
            if (!options.MaterialJsonOnly) {
                // Собираем текстуры материала
                ReportProgress("Search", "Collecting textures", 20, material.Name);
                var textureIds = CollectTextureIds(new[] { material });
                var materialTextures = allTextures.Where(t => textureIds.Contains(t.ID)).ToList();
                result.TextureCount = materialTextures.Count;

                // Создаём директорию textures
                var texturesDir = Path.Combine(exportPath, "textures");
                Directory.CreateDirectory(texturesDir);

                // Генерируем ORM packed текстуры
                var packedTextureIds = new HashSet<int>();
                if (options.GenerateORMTextures) {
                    ReportProgress("ORM", "Generating packed textures", 30, material.Name);
                    (ormResults, packedTextureIds) = await GenerateORMTexturesAsync(
                        new List<MaterialResource> { material }, materialTextures, texturesDir, options, cancellationToken);
                    result.GeneratedORMTextures.AddRange(ormResults.Values.Select(r => r.OutputPath));
                }

                // Конвертируем текстуры в KTX2
                if (options.ConvertTextures) {
                    ReportProgress("KTX2", "Converting textures", 50, material.Name);
                    var texturesToConvert = materialTextures.Where(t => !packedTextureIds.Contains(t.ID)).ToList();
                    texturePathMap = await ConvertTexturesToKTX2Async(
                        texturesToConvert, texturesDir, exportPath, options, cancellationToken);
                    result.ConvertedTextures.AddRange(texturePathMap.Values);
                }
            } else {
                Logger.Info($"MaterialJsonOnly mode - skipping textures for {material.Name}");
            }

            // Генерируем material JSON
            ReportProgress("JSON", "Generating material file", 70, material.Name);
            var materialJson = GenerateMaterialJsonWithPaths(material, texturePathMap, ormResults, options);
            var matFileName = GetSafeFileName(material.Name ?? $"mat_{material.ID}") + ".json";
            var matPath = Path.Combine(exportPath, matFileName);

            var json = JsonSerializer.Serialize(materialJson, new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await File.WriteAllTextAsync(matPath, json, cancellationToken);
            result.GeneratedMaterialJson = matPath;
            Logger.Info($"Generated material JSON: {matPath}");

            // Обновляем mapping.json
            await UpdateMappingForMaterialAsync(material, materialFolderPath, texturePathMap, ormResults, options, cancellationToken);
            Logger.Info($"Updated global mapping: {GetMappingPath()}");

            result.Success = true;
            ReportProgress($"Material export completed: {material.Name}", 100);

        } catch (OperationCanceledException) {
            result.Success = false;
            result.ErrorMessage = "Export cancelled";
        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Logger.Error(ex, $"Failed to export material {material.Name}");
        }

        return result;
    }

    /// <summary>
    /// Экспортирует текстуры без привязки к материалу или модели
    /// </summary>
    public async Task<TextureExportResult> ExportTextureAsync(
        TextureResource texture,
        IReadOnlyDictionary<int, string> folderPaths,
        ExportOptions options,
        CancellationToken cancellationToken = default) {

        var result = new TextureExportResult {
            TextureId = texture.ID,
            TextureName = texture.Name ?? $"texture_{texture.ID}"
        };

        try {
            ReportProgress($"Exporting texture: {texture.Name}", 0);

            // Определяем путь экспорта
            var textureFolderPath = GetResourceFolderPath(texture, folderPaths);
            var exportPath = Path.Combine(GetContentBasePath(), textureFolderPath);
            var texturesDir = Path.Combine(exportPath, "textures");
            Directory.CreateDirectory(texturesDir);
            result.ExportPath = texturesDir;

            Logger.Info($"Texture export path: {texturesDir}");

            // Конвертируем текстуру
            if (options.ConvertTextures && !string.IsNullOrEmpty(texture.Path) && File.Exists(texture.Path)) {
                ReportProgress("Converting texture to KTX2...", 50);
                var texturePathMap = await ConvertTexturesToKTX2Async(
                    new List<TextureResource> { texture }, texturesDir, exportPath, options, cancellationToken);

                if (texturePathMap.TryGetValue(texture.ID, out var outputPath)) {
                    result.ConvertedTexturePath = outputPath;
                }

                // Обновляем mapping.json
                await UpdateMappingForTextureAsync(texture, textureFolderPath, texturePathMap, cancellationToken);
            }

            result.Success = true;
            ReportProgress($"Texture export completed: {texture.Name}", 100);

        } catch (OperationCanceledException) {
            result.Success = false;
            result.ErrorMessage = "Export cancelled";
        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Logger.Error(ex, $"Failed to export texture {texture.Name}");
        }

        return result;
    }

    private async Task UpdateMappingForMaterialAsync(
        MaterialResource material,
        string materialFolderPath,
        Dictionary<int, string> texturePathMap,
        Dictionary<int, ORMExportResult> ormResults,
        ExportOptions options,
        CancellationToken cancellationToken) {

        var mappingPath = GetMappingPath();
        Directory.CreateDirectory(Path.GetDirectoryName(mappingPath)!);

        var existingMapping = await LoadOrCreateMappingAsync(mappingPath, cancellationToken);
        var assetsContentPath = "assets/content";
        var matRelativePath = $"{assetsContentPath}/{materialFolderPath}";

        // Добавляем материал
        var matFileName = GetSafeFileName(material.Name ?? $"mat_{material.ID}") + ".json";
        existingMapping.Materials[material.ID.ToString()] = $"{matRelativePath}/{matFileName}";

        // Добавляем текстуры
        foreach (var (texId, relPath) in texturePathMap) {
            existingMapping.Textures[texId.ToString()] = $"{matRelativePath}/{relPath}";
        }

        // Добавляем ORM текстуры
        foreach (var (matId, orm) in ormResults) {
            var ormKey = (-matId).ToString();
            existingMapping.Textures[ormKey] = $"{matRelativePath}/textures/{Path.GetFileName(orm.OutputPath)}";
        }

        var mappingJson = JsonSerializer.Serialize(existingMapping, new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(mappingPath, mappingJson, cancellationToken);
    }

    private async Task UpdateMappingForTextureAsync(
        TextureResource texture,
        string textureFolderPath,
        Dictionary<int, string> texturePathMap,
        CancellationToken cancellationToken) {

        var mappingPath = GetMappingPath();
        Directory.CreateDirectory(Path.GetDirectoryName(mappingPath)!);

        var existingMapping = await LoadOrCreateMappingAsync(mappingPath, cancellationToken);
        var assetsContentPath = "assets/content";

        foreach (var (texId, relPath) in texturePathMap) {
            existingMapping.Textures[texId.ToString()] = $"{assetsContentPath}/{textureFolderPath}/{relPath}";
        }

        var mappingJson = JsonSerializer.Serialize(existingMapping, new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(mappingPath, mappingJson, cancellationToken);
    }

    private async Task<MappingData> LoadOrCreateMappingAsync(string mappingPath, CancellationToken cancellationToken) {
        if (File.Exists(mappingPath)) {
            var existingJson = await File.ReadAllTextAsync(mappingPath, cancellationToken);
            return JsonSerializer.Deserialize<MappingData>(existingJson, new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? new MappingData();
        }
        return new MappingData();
    }

    #endregion

    private void ReportProgress(string message, int percentage) {
        Logger.Info($"[{percentage}%] {message}");
        ProgressChanged?.Invoke(new ExportProgress {
            Message = message,
            Percentage = percentage
        });
    }

    private void ReportProgress(string phase, string message, int percentage, string? currentItem = null, int currentIndex = 0, int totalItems = 0) {
        var fullMessage = string.IsNullOrEmpty(currentItem)
            ? $"{phase}: {message}"
            : totalItems > 0
                ? $"{phase}: {message} - {currentItem} ({currentIndex}/{totalItems})"
                : $"{phase}: {message} - {currentItem}";

        Logger.Info($"[{percentage}%] {fullMessage}");
        ProgressChanged?.Invoke(new ExportProgress {
            Message = message,
            Percentage = percentage,
            Phase = phase,
            CurrentItem = currentItem,
            CurrentIndex = currentIndex,
            TotalItems = totalItems
        });
    }
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

    /// <summary>
    /// Экспортировать только JSON материала без связанных текстур.
    /// Используется когда пользователь выбрал только материалы без "Select Related".
    /// </summary>
    public bool MaterialJsonOnly { get; set; } = false;
    public int LODLevels { get; set; } = 2;
    public int TextureQuality { get; set; } = 128;
    public bool ApplyToksvig { get; set; } = true;

    /// <summary>
    /// Использовать сохранённые настройки текстур из ResourceSettingsService
    /// Если false - используются автоматические настройки на основе типа текстуры
    /// </summary>
    public bool UseSavedTextureSettings { get; set; } = true;

    /// <summary>
    /// Конфигурация Master Materials для экспорта chunks
    /// Если null, chunks не экспортируются
    /// </summary>
    public MasterMaterials.Models.MasterMaterialsConfig? MasterMaterialsConfig { get; set; }

    /// <summary>
    /// Путь к папке проекта для загрузки chunks файлов
    /// </summary>
    public string? ProjectFolderPath { get; set; }

    /// <summary>
    /// Default master material для материалов без явного назначения
    /// Используется если MasterMaterialName не задан
    /// </summary>
    public string? DefaultMasterMaterial { get; set; }
}

public class ExportProgress {
    public string Message { get; set; } = "";
    public int Percentage { get; set; }

    /// <summary>
    /// Фаза обработки (Finding, Converting, Generating, Uploading, etc.)
    /// </summary>
    public string Phase { get; set; } = "";

    /// <summary>
    /// Текущий обрабатываемый файл/ресурс
    /// </summary>
    public string? CurrentItem { get; set; }

    /// <summary>
    /// Номер текущего элемента в пакете
    /// </summary>
    public int CurrentIndex { get; set; }

    /// <summary>
    /// Общее количество элементов в пакете
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Краткое описание для UI
    /// </summary>
    public string ShortStatus => string.IsNullOrEmpty(CurrentItem)
        ? Message
        : TotalItems > 0
            ? $"{Phase}: {CurrentItem} ({CurrentIndex}/{TotalItems})"
            : $"{Phase}: {CurrentItem}";
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
    public List<string> GeneratedChunksFiles { get; set; } = new();

    /// <summary>
    /// IDs материалов, которые были обработаны при экспорте
    /// </summary>
    public List<int> ProcessedMaterialIds { get; set; } = new();

    /// <summary>
    /// IDs текстур, которые были обработаны при экспорте
    /// </summary>
    public List<int> ProcessedTextureIds { get; set; } = new();
}

public class MaterialExportResult {
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public string? ExportPath { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public int TextureCount { get; set; }
    public string? GeneratedMaterialJson { get; set; }
    public List<string> ConvertedTextures { get; set; } = new();
    public List<string> GeneratedORMTextures { get; set; } = new();
}

public class TextureExportResult {
    public int TextureId { get; set; }
    public string TextureName { get; set; } = "";
    public string? ExportPath { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ConvertedTexturePath { get; set; }
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

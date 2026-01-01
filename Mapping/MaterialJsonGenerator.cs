using System.Text.Json;
using AssetProcessor.Helpers;
using AssetProcessor.Mapping.Models;
using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.Mapping;

/// <summary>
/// Генератор JSON файлов для instance материалов
/// Поддерживает работу с packed ORM текстурами
/// </summary>
public class MaterialJsonGenerator {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Словарь соответствия blendType → имя master материала
    /// </summary>
    private static readonly Dictionary<string, string> BlendTypeToMaster = new(StringComparer.OrdinalIgnoreCase) {
        { "0", "pbr_opaque" },
        { "1", "pbr_alpha" },
        { "2", "pbr_additive" },
        { "3", "pbr_premul" },
        { "NONE", "pbr_opaque" },
        { "NORMAL", "pbr_alpha" },
        { "ADDITIVE", "pbr_additive" },
        { "PREMULTIPLIED", "pbr_premul" }
    };

    /// <summary>
    /// Генерирует MaterialInstanceJson для материала
    /// </summary>
    /// <param name="material">Исходный MaterialResource</param>
    /// <param name="ormTextures">Доступные ORM текстуры для поиска packed версий</param>
    /// <param name="options">Опции генерации</param>
    /// <returns>Структура MaterialInstanceJson</returns>
    public MaterialInstanceJson GenerateMaterialJson(
        MaterialResource material,
        IEnumerable<ORMTextureResource>? ormTextures = null,
        MaterialJsonOptions? options = null) {

        options ??= new MaterialJsonOptions();
        var masterName = DetermineMasterMaterial(material, options);

        var instance = new MaterialInstanceJson {
            Master = masterName,
            Params = ExtractMaterialParams(material),
            Textures = ExtractMaterialTextures(material, ormTextures, options)
        };

        return instance;
    }

    /// <summary>
    /// Генерирует и сохраняет JSON файл материала
    /// </summary>
    public async Task<string> GenerateAndSaveAsync(
        MaterialResource material,
        string outputDirectory,
        IEnumerable<ORMTextureResource>? ormTextures = null,
        MaterialJsonOptions? options = null,
        CancellationToken cancellationToken = default) {

        var materialJson = GenerateMaterialJson(material, ormTextures, options);
        var fileName = GetMaterialFileName(material);
        var outputPath = Path.Combine(outputDirectory, fileName);

        await SaveMaterialJsonAsync(materialJson, outputPath, cancellationToken);

        return outputPath;
    }

    /// <summary>
    /// Пакетная генерация JSON файлов для всех материалов
    /// </summary>
    public async Task<List<string>> GenerateBatchAsync(
        IEnumerable<MaterialResource> materials,
        string outputDirectory,
        IReadOnlyDictionary<int, string> folderPaths,
        IEnumerable<ORMTextureResource>? ormTextures = null,
        MaterialJsonOptions? options = null,
        CancellationToken cancellationToken = default) {

        var generatedFiles = new List<string>();
        var ormList = ormTextures?.ToList();

        foreach (var material in materials) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                // Вычисляем путь с учётом иерархии папок
                var folderPath = GetMaterialFolderPath(material, folderPaths);
                var fullOutputDir = string.IsNullOrEmpty(folderPath)
                    ? outputDirectory
                    : Path.Combine(outputDirectory, folderPath);

                var filePath = await GenerateAndSaveAsync(
                    material, fullOutputDir, ormList, options, cancellationToken);

                generatedFiles.Add(filePath);
                Logger.Debug($"Generated material JSON: {filePath}");

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to generate material JSON for {material.Name}");
            }
        }

        Logger.Info($"Generated {generatedFiles.Count} material JSON files");
        return generatedFiles;
    }

    #region Private Methods

    private string DetermineMasterMaterial(MaterialResource material, MaterialJsonOptions options) {
        // Проверяем кастомный маппинг по имени
        if (options.CustomMasterMappings != null && !string.IsNullOrEmpty(material.Name)) {
            foreach (var (pattern, master) in options.CustomMasterMappings) {
                if (material.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)) {
                    return master;
                }
            }
        }

        // Определяем по blendType
        if (!string.IsNullOrEmpty(material.BlendType)) {
            if (BlendTypeToMaster.TryGetValue(material.BlendType, out var master)) {
                return master;
            }
        }

        // По умолчанию - opaque
        return "pbr_opaque";
    }

    private MaterialParams ExtractMaterialParams(MaterialResource material) {
        return new MaterialParams {
            Diffuse = NormalizeColor(material.Diffuse),
            Metalness = material.UseMetalness ? material.Metalness : null,
            Gloss = material.Glossiness ?? material.Shininess,
            Emissive = NormalizeColor(material.Emissive),
            EmissiveIntensity = material.EmissiveIntensity,
            Opacity = material.Opacity,
            AlphaTest = material.AlphaTest,
            Bumpiness = material.BumpMapFactor,
            AoColor = material.AOTint ? NormalizeColor(material.AOColor) : null,
            Specular = material.SpecularTint ? NormalizeColor(material.Specular) : null,
            Reflectivity = material.Reflectivity,
            UseMetalness = material.UseMetalness ? true : null
        };
    }

    private MaterialTextures ExtractMaterialTextures(
        MaterialResource material,
        IEnumerable<ORMTextureResource>? ormTextures,
        MaterialJsonOptions options) {

        var textures = new MaterialTextures {
            DiffuseMap = material.DiffuseMapId,
            NormalMap = material.NormalMapId,
            SpecularMap = material.SpecularMapId,
            EmissiveMap = material.EmissiveMapId,
            OpacityMap = material.OpacityMapId
        };

        // Проверяем, есть ли packed ORM текстура для этого материала
        if (ormTextures != null && options.UsePackedTextures) {
            var packedTexture = FindPackedTexture(material, ormTextures);
            if (packedTexture != null) {
                // Используем packed текстуру вместо отдельных
                switch (packedTexture.PackingMode) {
                    case ChannelPackingMode.OG:
                        textures.OgMap = new TextureReference { Asset = packedTexture.ID };
                        // Не устанавливаем отдельные AO и Gloss
                        break;

                    case ChannelPackingMode.OGM:
                        textures.OgmMap = new TextureReference { Asset = packedTexture.ID };
                        // Не устанавливаем отдельные AO, Gloss, Metalness
                        break;

                    case ChannelPackingMode.OGMH:
                        textures.OgmhMap = new TextureReference { Asset = packedTexture.ID };
                        // Не устанавливаем отдельные AO, Gloss, Metalness (Height обычно нет)
                        break;
                }
            } else {
                // Используем отдельные текстуры
                textures.GlossMap = material.GlossMapId;
                textures.MetalnessMap = material.MetalnessMapId;
                textures.AoMap = material.AOMapId;
            }
        } else {
            // Используем отдельные текстуры
            textures.GlossMap = material.GlossMapId;
            textures.MetalnessMap = material.MetalnessMapId;
            textures.AoMap = material.AOMapId;
        }

        return textures;
    }

    private ORMTextureResource? FindPackedTexture(
        MaterialResource material,
        IEnumerable<ORMTextureResource> ormTextures) {

        // Ищем ORM текстуру, которая была создана из текстур этого материала
        foreach (var orm in ormTextures) {
            // Проверяем по source текстурам
            if (orm.AOSource != null && orm.AOSource.ID == material.AOMapId) {
                return orm;
            }
            if (orm.GlossSource != null && orm.GlossSource.ID == material.GlossMapId) {
                return orm;
            }
            if (orm.MetallicSource != null && orm.MetallicSource.ID == material.MetalnessMapId) {
                return orm;
            }

            // Проверяем по имени (например, material_ogm.ktx2)
            if (!string.IsNullOrEmpty(material.Name) && !string.IsNullOrEmpty(orm.Name)) {
                var materialBaseName = ExtractBaseName(material.Name);
                var ormBaseName = ExtractBaseName(orm.Name);
                if (string.Equals(materialBaseName, ormBaseName, StringComparison.OrdinalIgnoreCase)) {
                    return orm;
                }
            }
        }

        return null;
    }

    private string ExtractBaseName(string name) {
        // Удаляем суффиксы _og, _ogm, _ogmh
        var suffixes = new[] { "_ogmh", "_ogm", "_og" };
        foreach (var suffix in suffixes) {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                return name.Substring(0, name.Length - suffix.Length);
            }
        }
        return name;
    }

    private string GetMaterialFileName(MaterialResource material) {
        var baseName = material.Name ?? $"material_{material.ID}";
        return $"{PathSanitizer.SanitizePath(baseName)}.json";
    }

    private string GetMaterialFolderPath(MaterialResource material, IReadOnlyDictionary<int, string> folderPaths) {
        if (material.Parent.HasValue && material.Parent.Value != 0 &&
            folderPaths.TryGetValue(material.Parent.Value, out var path)) {
            return PathSanitizer.SanitizePath(path);
        }
        return string.Empty;
    }

    private float[]? NormalizeColor(List<float>? color) {
        if (color == null || color.Count == 0) return null;

        // Обеспечиваем 3 компонента
        var result = new float[3];
        for (int i = 0; i < Math.Min(3, color.Count); i++) {
            result[i] = color[i];
        }
        return result;
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

    #endregion
}

/// <summary>
/// Опции генерации material JSON
/// </summary>
public class MaterialJsonOptions {
    /// <summary>
    /// Использовать packed ORM текстуры вместо отдельных
    /// </summary>
    public bool UsePackedTextures { get; set; } = true;

    /// <summary>
    /// Кастомный маппинг паттернов имён → master материалы
    /// Например: { "glass" → "pbr_alpha", "metal" → "pbr_metallic" }
    /// </summary>
    public Dictionary<string, string>? CustomMasterMappings { get; set; }

    /// <summary>
    /// Включать дефолтные значения параметров (null если не установлены)
    /// </summary>
    public bool IncludeDefaults { get; set; } = false;
}

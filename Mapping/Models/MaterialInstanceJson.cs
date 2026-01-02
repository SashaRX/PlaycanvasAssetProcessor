using System.Text.Json.Serialization;

namespace AssetProcessor.Mapping.Models;

/// <summary>
/// Структура JSON файла для instance материала
/// Файл &lt;path&gt;/&lt;name&gt;.json
/// </summary>
public class MaterialInstanceJson {
    /// <summary>
    /// Имя master материала для клонирования
    /// Определяется по blendType:
    ///   0 (NONE) → "pbr_opaque"
    ///   1 (NORMAL) → "pbr_alpha"
    ///   2 (ADDITIVE) → "pbr_additive"
    ///   3 (PREMULTIPLIED) → "pbr_premul"
    /// </summary>
    [JsonPropertyName("master")]
    public string Master { get; set; } = "pbr_opaque";

    /// <summary>
    /// Параметры материала (числовые значения и цвета)
    /// </summary>
    [JsonPropertyName("params")]
    public MaterialParams Params { get; set; } = new();

    /// <summary>
    /// Ссылки на текстуры
    /// </summary>
    [JsonPropertyName("textures")]
    public MaterialTextures Textures { get; set; } = new();
}

/// <summary>
/// Параметры материала
/// </summary>
public class MaterialParams {
    /// <summary>
    /// Diffuse цвет [R, G, B] (0-1)
    /// </summary>
    [JsonPropertyName("diffuse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float[]? Diffuse { get; set; }

    /// <summary>
    /// Metalness (0-1)
    /// </summary>
    [JsonPropertyName("metalness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Metalness { get; set; }

    /// <summary>
    /// Gloss/Shininess (0-1)
    /// </summary>
    [JsonPropertyName("gloss")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Gloss { get; set; }

    /// <summary>
    /// Emissive цвет [R, G, B] (0-1)
    /// </summary>
    [JsonPropertyName("emissive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float[]? Emissive { get; set; }

    /// <summary>
    /// Интенсивность emission
    /// </summary>
    [JsonPropertyName("emissiveIntensity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? EmissiveIntensity { get; set; }

    /// <summary>
    /// Непрозрачность (0-1)
    /// </summary>
    [JsonPropertyName("opacity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Opacity { get; set; }

    /// <summary>
    /// Порог alpha test
    /// </summary>
    [JsonPropertyName("alphaTest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? AlphaTest { get; set; }

    /// <summary>
    /// Bump map factor (нормали)
    /// </summary>
    [JsonPropertyName("bumpiness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Bumpiness { get; set; }

    /// <summary>
    /// AO цвет tint [R, G, B]
    /// </summary>
    [JsonPropertyName("aoColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float[]? AoColor { get; set; }

    /// <summary>
    /// Specular цвет [R, G, B]
    /// </summary>
    [JsonPropertyName("specular")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float[]? Specular { get; set; }

    /// <summary>
    /// Reflectivity (0-1)
    /// </summary>
    [JsonPropertyName("reflectivity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Reflectivity { get; set; }

    /// <summary>
    /// Использовать metalness workflow
    /// </summary>
    [JsonPropertyName("useMetalness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseMetalness { get; set; }
}

/// <summary>
/// Ссылки на текстуры материала
/// Использует относительные пути для CDN (например: "textures/albedo.ktx2")
/// </summary>
public class MaterialTextures {
    /// <summary>
    /// Diffuse/Albedo текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("diffuseMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DiffuseMapPath { get; set; }

    /// <summary>
    /// Normal map текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("normalMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalMapPath { get; set; }

    /// <summary>
    /// Specular текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("specularMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpecularMapPath { get; set; }

    /// <summary>
    /// Gloss/Roughness текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("glossMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GlossMapPath { get; set; }

    /// <summary>
    /// Metalness текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("metalnessMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MetalnessMapPath { get; set; }

    /// <summary>
    /// Ambient Occlusion текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("aoMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AoMapPath { get; set; }

    /// <summary>
    /// Emissive текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("emissiveMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmissiveMapPath { get; set; }

    /// <summary>
    /// Opacity текстура (относительный путь)
    /// </summary>
    [JsonPropertyName("opacityMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OpacityMapPath { get; set; }

    /// <summary>
    /// Packed OG текстура (Occlusion + Gloss) - относительный путь
    /// </summary>
    [JsonPropertyName("ogMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OgMapPath { get; set; }

    /// <summary>
    /// Packed OGM текстура (Occlusion + Gloss + Metalness) - относительный путь
    /// </summary>
    [JsonPropertyName("ogmMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OgmMapPath { get; set; }

    /// <summary>
    /// Packed OGMH текстура (Occlusion + Gloss + Metalness + Height) - относительный путь
    /// </summary>
    [JsonPropertyName("ogmhMap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OgmhMapPath { get; set; }
}

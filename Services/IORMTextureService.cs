using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using System.Collections.Generic;

namespace AssetProcessor.Services;

/// <summary>
/// Сервис для создания и управления ORM (Occlusion-Roughness-Metallic) текстурами.
/// </summary>
public interface IORMTextureService {
    /// <summary>
    /// Определяет режим упаковки на основе доступных текстур.
    /// </summary>
    /// <param name="ao">AO текстура.</param>
    /// <param name="gloss">Gloss текстура.</param>
    /// <param name="metallic">Metallic текстура.</param>
    /// <returns>Режим упаковки (OG/OGM/OGMH/None).</returns>
    ChannelPackingMode DetectPackingMode(TextureResource? ao, TextureResource? gloss, TextureResource? metallic);

    /// <summary>
    /// Находит текстуру по ID материала.
    /// </summary>
    /// <param name="mapId">ID текстуры из материала.</param>
    /// <param name="textures">Коллекция текстур для поиска.</param>
    /// <returns>Найденная текстура или null.</returns>
    TextureResource? FindTextureById(int? mapId, IEnumerable<TextureResource> textures);

    /// <summary>
    /// Определяет workflow (Metalness/Specular) и находит соответствующую текстуру.
    /// </summary>
    /// <param name="material">Материал для анализа.</param>
    /// <param name="textures">Коллекция текстур.</param>
    /// <returns>Результат определения workflow.</returns>
    WorkflowResult DetectWorkflow(MaterialResource material, IEnumerable<TextureResource> textures);

    /// <summary>
    /// Создаёт ORM текстуру из материала.
    /// </summary>
    /// <param name="material">Исходный материал.</param>
    /// <param name="textures">Коллекция текстур.</param>
    /// <returns>Результат создания ORM.</returns>
    ORMCreationResult CreateORMFromMaterial(MaterialResource material, IEnumerable<TextureResource> textures);

    /// <summary>
    /// Создаёт пустую ORM текстуру с уникальным именем.
    /// </summary>
    /// <param name="existingTextures">Существующие текстуры для подсчёта.</param>
    /// <returns>Новая ORM текстура.</returns>
    ORMTextureResource CreateEmptyORM(IEnumerable<TextureResource> existingTextures);

    /// <summary>
    /// Генерирует имя ORM текстуры на основе материала и режима.
    /// </summary>
    /// <param name="materialName">Имя материала.</param>
    /// <param name="mode">Режим упаковки.</param>
    /// <returns>Сгенерированное имя.</returns>
    string GenerateORMName(string? materialName, ChannelPackingMode mode);

    /// <summary>
    /// Получает базовое имя материала (без суффикса _mat).
    /// </summary>
    /// <param name="materialName">Полное имя материала.</param>
    /// <returns>Базовое имя.</returns>
    string GetBaseMaterialName(string? materialName);
}

/// <summary>
/// Результат определения workflow.
/// </summary>
public class WorkflowResult {
    public bool IsMetalnessWorkflow { get; init; }
    public string WorkflowInfo { get; init; } = string.Empty;
    public string MapTypeLabel { get; init; } = string.Empty;
    public TextureResource? MetalnessOrSpecularTexture { get; init; }
    public TextureResource? AOTexture { get; init; }
    public TextureResource? GlossTexture { get; init; }
}

/// <summary>
/// Результат создания ORM текстуры.
/// </summary>
public class ORMCreationResult {
    public bool Success { get; init; }
    public ORMTextureResource? ORMTexture { get; init; }
    public string? ErrorMessage { get; init; }
    public ChannelPackingMode PackingMode { get; init; }
    public bool AlreadyExists { get; init; }
    public string? ExistingName { get; init; }
}

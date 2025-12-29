using AssetProcessor.Resources;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

/// <summary>
/// Сервис для парсинга JSON ассетов из PlayCanvas.
/// Обрабатывает загрузку, парсинг и создание ресурсов из JSON данных.
/// </summary>
public interface IAssetJsonParserService {
    /// <summary>
    /// Событие при обработке каждого ассета (для обновления прогресса).
    /// </summary>
    event EventHandler<AssetProcessedEventArgs>? AssetProcessed;

    /// <summary>
    /// Загружает и парсит ассеты из локального JSON файла.
    /// </summary>
    /// <param name="projectFolderPath">Путь к папке проекта.</param>
    /// <param name="projectName">Имя проекта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат парсинга с коллекциями ресурсов.</returns>
    Task<AssetParsingResult> LoadAndParseAssetsAsync(
        string projectFolderPath,
        string projectName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Парсит массив ассетов из JSON.
    /// </summary>
    /// <param name="assetsJson">JSON массив ассетов.</param>
    /// <param name="projectFolderPath">Путь к папке проекта.</param>
    /// <param name="projectName">Имя проекта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат парсинга.</returns>
    Task<AssetParsingResult> ParseAssetsAsync(
        JArray assetsJson,
        string projectFolderPath,
        string projectName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Детектирует локальные ORM текстуры в папке проекта.
    /// </summary>
    /// <param name="projectFolderPath">Путь к папке проекта.</param>
    /// <param name="existingTextures">Существующие текстуры для поиска source.</param>
    /// <returns>Найденные ORM текстуры.</returns>
    Task<IReadOnlyList<ORMTextureResource>> DetectORMTexturesAsync(
        string projectFolderPath,
        IEnumerable<TextureResource> existingTextures);

    /// <summary>
    /// Строит иерархию папок из JSON ассетов.
    /// </summary>
    /// <param name="assetsJson">JSON массив ассетов.</param>
    /// <returns>Словарь путей папок (ID → путь).</returns>
    Dictionary<int, string> BuildFolderHierarchy(JArray assetsJson);
}

/// <summary>
/// Результат парсинга ассетов.
/// </summary>
public class AssetParsingResult {
    public IReadOnlyList<TextureResource> Textures { get; init; } = Array.Empty<TextureResource>();
    public IReadOnlyList<ModelResource> Models { get; init; } = Array.Empty<ModelResource>();
    public IReadOnlyList<MaterialResource> Materials { get; init; } = Array.Empty<MaterialResource>();
    public Dictionary<int, string> FolderPaths { get; init; } = new();
    public int TotalCount => Textures.Count + Models.Count + Materials.Count;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Аргументы события обработки ассета.
/// </summary>
public class AssetProcessedEventArgs : EventArgs {
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }
    public string? AssetName { get; init; }
}

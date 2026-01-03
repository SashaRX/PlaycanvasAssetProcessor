using AssetProcessor.Mapping.Models;
using AssetProcessor.Resources;

namespace AssetProcessor.Mapping;

/// <summary>
/// Сервис для генерации mapping.json и связанных файлов
/// </summary>
public interface IMappingGeneratorService {
    /// <summary>
    /// Генерирует полный маппинг для проекта
    /// </summary>
    /// <param name="options">Опции генерации</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат генерации маппинга</returns>
    Task<MappingGenerationResult> GenerateMappingAsync(
        MappingGenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Валидирует существующий маппинг
    /// </summary>
    /// <param name="manifest">Маппинг для валидации</param>
    /// <param name="basePath">Базовый путь для проверки файлов</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат валидации</returns>
    Task<MappingValidationResult> ValidateMappingAsync(
        MappingManifest manifest,
        string basePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет маппинг в JSON файл
    /// </summary>
    /// <param name="manifest">Маппинг для сохранения</param>
    /// <param name="outputPath">Путь к выходному файлу</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task SaveMappingAsync(
        MappingManifest manifest,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает маппинг из JSON файла
    /// </summary>
    /// <param name="inputPath">Путь к файлу маппинга</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Загруженный маппинг или null</returns>
    Task<MappingManifest?> LoadMappingAsync(
        string inputPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Опции генерации маппинга
/// </summary>
public class MappingGenerationOptions {
    /// <summary>
    /// Базовый URL для CDN
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Информация о проекте PlayCanvas
    /// </summary>
    public ProjectInfo? Project { get; set; }

    /// <summary>
    /// Путь к папке проекта на диске
    /// </summary>
    public string ProjectFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Имя проекта
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Словарь путей папок: folder ID → путь
    /// </summary>
    public IReadOnlyDictionary<int, string> FolderPaths { get; set; } = new Dictionary<int, string>();

    /// <summary>
    /// Коллекция моделей для маппинга
    /// </summary>
    public IEnumerable<ModelResource> Models { get; set; } = Array.Empty<ModelResource>();

    /// <summary>
    /// Коллекция материалов для маппинга
    /// </summary>
    public IEnumerable<MaterialResource> Materials { get; set; } = Array.Empty<MaterialResource>();

    /// <summary>
    /// Коллекция текстур для маппинга
    /// </summary>
    public IEnumerable<TextureResource> Textures { get; set; } = Array.Empty<TextureResource>();

    /// <summary>
    /// Коллекция ORM текстур для маппинга
    /// </summary>
    public IEnumerable<ORMTextureResource> OrmTextures { get; set; } = Array.Empty<ORMTextureResource>();

    /// <summary>
    /// Путь к папке с обработанными моделями (LOD файлы)
    /// </summary>
    public string? ProcessedModelsPath { get; set; }

    /// <summary>
    /// Путь к папке с обработанными текстурами (KTX2 файлы)
    /// </summary>
    public string? ProcessedTexturesPath { get; set; }

    /// <summary>
    /// Путь к выходной папке для маппинга и material JSON файлов
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Генерировать отдельные JSON файлы для каждого материала
    /// </summary>
    public bool GenerateMaterialJsonFiles { get; set; } = true;

    /// <summary>
    /// Валидировать маппинг после генерации
    /// </summary>
    public bool ValidateAfterGeneration { get; set; } = true;

    /// <summary>
    /// Включать размеры файлов в маппинг
    /// </summary>
    public bool IncludeFileSizes { get; set; } = true;
}

/// <summary>
/// Результат генерации маппинга
/// </summary>
public class MappingGenerationResult {
    /// <summary>
    /// Генерация прошла успешно
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Сообщение об ошибке (если есть)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Сгенерированный маппинг
    /// </summary>
    public MappingManifest? Manifest { get; set; }

    /// <summary>
    /// Результат валидации (если была запрошена)
    /// </summary>
    public MappingValidationResult? ValidationResult { get; set; }

    /// <summary>
    /// Путь к сохранённому mapping.json
    /// </summary>
    public string? MappingFilePath { get; set; }

    /// <summary>
    /// Список путей к сгенерированным material JSON файлам
    /// </summary>
    public List<string> MaterialJsonFiles { get; set; } = new();

    /// <summary>
    /// Время генерации
    /// </summary>
    public TimeSpan GenerationTime { get; set; }
}

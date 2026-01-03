namespace AssetProcessor.Mapping.Models;

/// <summary>
/// Результат валидации маппинга
/// </summary>
public class MappingValidationResult {
    /// <summary>
    /// Валидация прошла успешно
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// Список ошибок валидации
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>
    /// Список предупреждений
    /// </summary>
    public List<ValidationWarning> Warnings { get; set; } = new();

    /// <summary>
    /// Статистика валидации
    /// </summary>
    public ValidationStats Stats { get; set; } = new();
}

/// <summary>
/// Ошибка валидации
/// </summary>
public class ValidationError {
    /// <summary>
    /// Тип ошибки
    /// </summary>
    public ValidationErrorType Type { get; set; }

    /// <summary>
    /// Описание ошибки
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// ID ассета, связанного с ошибкой
    /// </summary>
    public int? AssetId { get; set; }

    /// <summary>
    /// Путь к файлу, связанному с ошибкой
    /// </summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// Типы ошибок валидации
/// </summary>
public enum ValidationErrorType {
    /// <summary>
    /// Материал в модели не найден в словаре materials
    /// </summary>
    MaterialNotFound,

    /// <summary>
    /// Текстура в материале не найдена в словаре textures
    /// </summary>
    TextureNotFound,

    /// <summary>
    /// LOD файл не существует на диске
    /// </summary>
    LodFileNotFound,

    /// <summary>
    /// Файл материала JSON не существует на диске
    /// </summary>
    MaterialFileNotFound,

    /// <summary>
    /// Файл текстуры не существует на диске
    /// </summary>
    TextureFileNotFound,

    /// <summary>
    /// Циклическая ссылка
    /// </summary>
    CircularReference,

    /// <summary>
    /// Дублирующийся ID
    /// </summary>
    DuplicateId,

    /// <summary>
    /// Некорректный формат файла
    /// </summary>
    InvalidFileFormat
}

/// <summary>
/// Предупреждение валидации
/// </summary>
public class ValidationWarning {
    /// <summary>
    /// Тип предупреждения
    /// </summary>
    public ValidationWarningType Type { get; set; }

    /// <summary>
    /// Описание предупреждения
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// ID ассета, связанного с предупреждением
    /// </summary>
    public int? AssetId { get; set; }
}

/// <summary>
/// Типы предупреждений валидации
/// </summary>
public enum ValidationWarningType {
    /// <summary>
    /// Материал не используется ни одной моделью
    /// </summary>
    UnusedMaterial,

    /// <summary>
    /// Текстура не используется ни одним материалом
    /// </summary>
    UnusedTexture,

    /// <summary>
    /// Модель без LOD уровней
    /// </summary>
    NoLodLevels,

    /// <summary>
    /// Материал без текстур
    /// </summary>
    NoTextures,

    /// <summary>
    /// Пропущен промежуточный LOD уровень
    /// </summary>
    MissingIntermediateLod
}

/// <summary>
/// Статистика валидации
/// </summary>
public class ValidationStats {
    /// <summary>
    /// Количество проверенных моделей
    /// </summary>
    public int ModelsChecked { get; set; }

    /// <summary>
    /// Количество проверенных материалов
    /// </summary>
    public int MaterialsChecked { get; set; }

    /// <summary>
    /// Количество проверенных текстур
    /// </summary>
    public int TexturesChecked { get; set; }

    /// <summary>
    /// Количество проверенных LOD файлов
    /// </summary>
    public int LodFilesChecked { get; set; }

    /// <summary>
    /// Количество валидных ссылок
    /// </summary>
    public int ValidReferences { get; set; }

    /// <summary>
    /// Количество невалидных ссылок
    /// </summary>
    public int InvalidReferences { get; set; }
}

namespace AssetProcessor.Upload;

/// <summary>
/// Настройки для загрузки на Backblaze B2
/// </summary>
public class B2UploadSettings {
    /// <summary>
    /// Application Key ID (keyID)
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Application Key (applicationKey)
    /// </summary>
    public string ApplicationKey { get; set; } = string.Empty;

    /// <summary>
    /// Имя bucket
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// ID bucket (опционально, можно получить автоматически)
    /// </summary>
    public string? BucketId { get; set; }

    /// <summary>
    /// Префикс пути в bucket (например: "projects/my-game")
    /// Все файлы будут загружаться в {BucketName}/{PathPrefix}/{relativePath}
    /// </summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Базовый URL для CDN доступа к файлам
    /// Например: "https://cdn.example.com" или "https://f000.backblazeb2.com/file/{bucketName}"
    /// </summary>
    public string CdnBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Максимальное количество параллельных загрузок
    /// </summary>
    public int MaxConcurrentUploads { get; set; } = 4;

    /// <summary>
    /// Таймаут операции в секундах
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Количество попыток при ошибке
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Проверять хеш файла перед загрузкой (skip если файл уже существует с тем же хешем)
    /// </summary>
    public bool SkipExistingFiles { get; set; } = true;

    /// <summary>
    /// Валидация настроек
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrEmpty(KeyId) &&
        !string.IsNullOrEmpty(ApplicationKey) &&
        !string.IsNullOrEmpty(BucketName);

    /// <summary>
    /// Построить полный путь для файла в bucket
    /// </summary>
    public string BuildFullPath(string relativePath) {
        var normalizedPath = relativePath.TrimStart('/');
        if (string.IsNullOrEmpty(PathPrefix)) {
            return normalizedPath;
        }
        var normalizedPrefix = PathPrefix.TrimEnd('/');
        // Не добавляем prefix если путь уже начинается с него (избегаем content/content/...)
        if (normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) {
            return normalizedPath;
        }
        return $"{normalizedPrefix}/{normalizedPath}";
    }

    /// <summary>
    /// Построить CDN URL для файла
    /// </summary>
    public string BuildCdnUrl(string relativePath) {
        var fullPath = BuildFullPath(relativePath);
        if (string.IsNullOrEmpty(CdnBaseUrl)) {
            return fullPath;
        }
        return $"{CdnBaseUrl.TrimEnd('/')}/{fullPath}";
    }
}

/// <summary>
/// Результат загрузки файла
/// </summary>
public class B2UploadResult {
    /// <summary>
    /// Загрузка успешна
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Локальный путь к файлу
    /// </summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// Путь в B2 bucket
    /// </summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>
    /// CDN URL для доступа к файлу
    /// </summary>
    public string? CdnUrl { get; set; }

    /// <summary>
    /// ID файла в B2
    /// </summary>
    public string? FileId { get; set; }

    /// <summary>
    /// SHA1 хеш файла
    /// </summary>
    public string? ContentSha1 { get; set; }

    /// <summary>
    /// Размер файла в байтах
    /// </summary>
    public long ContentLength { get; set; }

    /// <summary>
    /// Сообщение об ошибке (если есть)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Файл был пропущен (уже существует)
    /// </summary>
    public bool Skipped { get; set; }
}

/// <summary>
/// Результат пакетной загрузки
/// </summary>
public class B2BatchUploadResult {
    /// <summary>
    /// Все загрузки успешны
    /// </summary>
    public bool Success => FailedCount == 0;

    /// <summary>
    /// Количество успешно загруженных файлов
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Количество пропущенных файлов (уже существуют)
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Количество неудачных загрузок
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Общий размер загруженных файлов
    /// </summary>
    public long TotalBytesUploaded { get; set; }

    /// <summary>
    /// Время загрузки
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Результаты для каждого файла
    /// </summary>
    public List<B2UploadResult> Results { get; set; } = new();

    /// <summary>
    /// Список ошибок
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

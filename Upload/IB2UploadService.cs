namespace AssetProcessor.Upload;

/// <summary>
/// Сервис для загрузки файлов на Backblaze B2
/// </summary>
public interface IB2UploadService {
    /// <summary>
    /// Авторизация в B2 API
    /// </summary>
    /// <param name="settings">Настройки B2</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>True если авторизация успешна</returns>
    Task<bool> AuthorizeAsync(B2UploadSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загрузка одного файла
    /// </summary>
    /// <param name="localPath">Локальный путь к файлу</param>
    /// <param name="remotePath">Относительный путь в bucket</param>
    /// <param name="contentType">MIME тип (опционально, определяется автоматически)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат загрузки</returns>
    Task<B2UploadResult> UploadFileAsync(
        string localPath,
        string remotePath,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пакетная загрузка файлов
    /// </summary>
    /// <param name="files">Список файлов (локальный путь → относительный путь в bucket)</param>
    /// <param name="progress">Прогресс загрузки</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат пакетной загрузки</returns>
    Task<B2BatchUploadResult> UploadBatchAsync(
        IEnumerable<(string LocalPath, string RemotePath)> files,
        IProgress<B2UploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загрузка директории со всеми файлами
    /// </summary>
    /// <param name="localDirectory">Локальная директория</param>
    /// <param name="remotePrefix">Префикс пути в bucket</param>
    /// <param name="searchPattern">Паттерн поиска файлов (по умолчанию "*")</param>
    /// <param name="recursive">Рекурсивный поиск</param>
    /// <param name="progress">Прогресс загрузки</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат пакетной загрузки</returns>
    Task<B2BatchUploadResult> UploadDirectoryAsync(
        string localDirectory,
        string remotePrefix,
        string searchPattern = "*",
        bool recursive = true,
        IProgress<B2UploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверка существования файла в bucket
    /// </summary>
    /// <param name="remotePath">Путь к файлу в bucket</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>True если файл существует</returns>
    Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получение информации о файле
    /// </summary>
    /// <param name="remotePath">Путь к файлу в bucket</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Информация о файле или null</returns>
    Task<B2FileInfo?> GetFileInfoAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаление файла
    /// </summary>
    /// <param name="remotePath">Путь к файлу в bucket</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>True если удаление успешно</returns>
    Task<bool> DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получение списка файлов в директории
    /// </summary>
    /// <param name="prefix">Префикс пути</param>
    /// <param name="maxCount">Максимальное количество файлов</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Список файлов</returns>
    Task<IReadOnlyList<B2FileInfo>> ListFilesAsync(
        string prefix,
        int maxCount = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Текущие настройки
    /// </summary>
    B2UploadSettings? Settings { get; }

    /// <summary>
    /// Авторизован ли сервис
    /// </summary>
    bool IsAuthorized { get; }
}

/// <summary>
/// Прогресс загрузки
/// </summary>
public class B2UploadProgress {
    /// <summary>
    /// Текущий файл
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Номер текущего файла
    /// </summary>
    public int CurrentFileIndex { get; set; }

    /// <summary>
    /// Общее количество файлов
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Процент выполнения (0-100)
    /// </summary>
    public double PercentComplete => TotalFiles > 0 ? (double)CurrentFileIndex / TotalFiles * 100 : 0;

    /// <summary>
    /// Байт загружено
    /// </summary>
    public long BytesUploaded { get; set; }

    /// <summary>
    /// Общий размер в байтах
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Скорость загрузки (байт/сек)
    /// </summary>
    public double BytesPerSecond { get; set; }

    /// <summary>
    /// Статус текущей операции
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Информация о файле в B2
/// </summary>
public class B2FileInfo {
    /// <summary>
    /// ID файла
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// Имя файла (полный путь в bucket)
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Размер файла в байтах
    /// </summary>
    public long ContentLength { get; set; }

    /// <summary>
    /// MIME тип
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// SHA1 хеш содержимого
    /// </summary>
    public string ContentSha1 { get; set; } = string.Empty;

    /// <summary>
    /// Время загрузки (Unix timestamp в миллисекундах)
    /// </summary>
    public long UploadTimestamp { get; set; }

    /// <summary>
    /// Время загрузки как DateTime
    /// </summary>
    public DateTime UploadTime => DateTimeOffset.FromUnixTimeMilliseconds(UploadTimestamp).UtcDateTime;
}

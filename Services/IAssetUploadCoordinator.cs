using AssetProcessor.Data;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Upload;

namespace AssetProcessor.Services;

/// <summary>
/// Координатор загрузки ассетов на Backblaze B2
/// </summary>
public interface IAssetUploadCoordinator {
    /// <summary>
    /// Событие изменения статуса ресурса
    /// </summary>
    event EventHandler<ResourceStatusChangedEventArgs>? ResourceStatusChanged;

    /// <summary>
    /// Событие изменения общего прогресса загрузки
    /// </summary>
    event EventHandler<UploadProgressEventArgs>? UploadProgressChanged;

    /// <summary>
    /// Авторизован ли сервис
    /// </summary>
    bool IsAuthorized { get; }

    /// <summary>
    /// Инициализация и авторизация
    /// </summary>
    Task<bool> InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Загрузка одного ресурса
    /// </summary>
    Task<UploadResult> UploadResourceAsync(
        BaseResource resource,
        string projectName,
        string? modelName,
        CancellationToken ct = default);

    /// <summary>
    /// Загрузка нескольких ресурсов
    /// </summary>
    Task<AssetUploadResult> UploadResourcesAsync(
        IEnumerable<BaseResource> resources,
        string projectName,
        string? modelName,
        CancellationToken ct = default);

    /// <summary>
    /// Загрузка результатов экспорта модели
    /// </summary>
    Task<AssetUploadResult> UploadModelExportAsync(
        string exportPath,
        string projectName,
        string modelName,
        CancellationToken ct = default);

    /// <summary>
    /// Проверка нужна ли загрузка (по хешу) - синхронная версия
    /// </summary>
    bool ShouldUpload(BaseResource resource);

    /// <summary>
    /// Проверка нужна ли загрузка с проверкой в базе данных
    /// </summary>
    Task<bool> ShouldUploadAsync(BaseResource resource, CancellationToken ct = default);

    /// <summary>
    /// Восстанавливает состояние загрузки для ресурса из базы данных
    /// </summary>
    Task RestoreUploadStateAsync(BaseResource resource, CancellationToken ct = default);

    /// <summary>
    /// Восстанавливает состояние загрузки для коллекции ресурсов
    /// </summary>
    Task RestoreUploadStatesAsync(IEnumerable<BaseResource> resources, CancellationToken ct = default);

    /// <summary>
    /// Получает количество записей в истории загрузок
    /// </summary>
    Task<int> GetUploadRecordCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Получает записи истории с пагинацией
    /// </summary>
    Task<IReadOnlyList<UploadRecord>> GetUploadHistoryAsync(int offset = 0, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Вычисление SHA1 хеша файла
    /// </summary>
    string ComputeFileHash(string filePath);
}

/// <summary>
/// Аргументы события прогресса загрузки
/// </summary>
public class UploadProgressEventArgs : EventArgs {
    public UploadProgressEventArgs(int completed, int total, BaseResource? currentResource, double currentProgress) {
        Completed = completed;
        Total = total;
        CurrentResource = currentResource;
        CurrentProgress = currentProgress;
    }

    public int Completed { get; }
    public int Total { get; }
    public BaseResource? CurrentResource { get; }
    public double CurrentProgress { get; }
    public double OverallProgress => Total > 0 ? (Completed + CurrentProgress / 100.0) / Total * 100.0 : 0;
}

/// <summary>
/// Результат загрузки одного файла
/// </summary>
public record UploadResult(
    bool Success,
    string? FileId,
    string? FileName,
    string? FileUrl,
    string? ContentSha1,
    long ContentLength,
    string? ErrorMessage = null
);

/// <summary>
/// Результат загрузки ассетов
/// </summary>
public record AssetUploadResult(
    bool IsSuccess,
    int UploadedCount,
    int SkippedCount,
    int FailedCount,
    string Message,
    IReadOnlyList<UploadResult> Results);

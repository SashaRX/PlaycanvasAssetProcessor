using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Data {
    /// <summary>
    /// Сервис для хранения состояния загрузок
    /// </summary>
    public interface IUploadStateService : IDisposable {
        /// <summary>
        /// Инициализирует базу данных
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>
        /// Сохраняет запись о загрузке
        /// </summary>
        Task<long> SaveUploadAsync(UploadRecord record, CancellationToken ct = default);

        /// <summary>
        /// Обновляет существующую запись
        /// </summary>
        Task UpdateUploadAsync(UploadRecord record, CancellationToken ct = default);

        /// <summary>
        /// Получает запись по локальному пути
        /// </summary>
        Task<UploadRecord?> GetByLocalPathAsync(string localPath, CancellationToken ct = default);

        /// <summary>
        /// Получает запись по remote path
        /// </summary>
        Task<UploadRecord?> GetByRemotePathAsync(string remotePath, CancellationToken ct = default);

        /// <summary>
        /// Получает все записи для проекта
        /// </summary>
        Task<IReadOnlyList<UploadRecord>> GetByProjectAsync(string projectName, CancellationToken ct = default);

        /// <summary>
        /// Получает все записи
        /// </summary>
        Task<IReadOnlyList<UploadRecord>> GetAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Получает записи с пагинацией
        /// </summary>
        Task<IReadOnlyList<UploadRecord>> GetPageAsync(int offset, int limit, CancellationToken ct = default);

        /// <summary>
        /// Получает общее количество записей
        /// </summary>
        Task<int> GetCountAsync(CancellationToken ct = default);

        /// <summary>
        /// Удаляет запись по ID
        /// </summary>
        Task DeleteAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Удаляет запись по локальному пути
        /// </summary>
        Task DeleteByLocalPathAsync(string localPath, CancellationToken ct = default);

        /// <summary>
        /// Проверяет, был ли файл загружен (по хешу)
        /// </summary>
        Task<bool> IsUploadedAsync(string localPath, string currentHash, CancellationToken ct = default);

        /// <summary>
        /// Очищает все записи
        /// </summary>
        Task ClearAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Получает запись по PlayCanvas ResourceId и типу ресурса
        /// </summary>
        Task<UploadRecord?> GetByResourceIdAsync(int resourceId, string resourceType, CancellationToken ct = default);

        /// <summary>
        /// Получает записи для нескольких ресурсов по ResourceId и типу
        /// </summary>
        Task<IReadOnlyList<UploadRecord>> GetByResourceIdsAsync(IEnumerable<int> resourceIds, string resourceType, CancellationToken ct = default);
    }
}

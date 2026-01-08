using System;

namespace AssetProcessor.Data {
    /// <summary>
    /// Запись об загруженном файле в CDN
    /// </summary>
    public class UploadRecord {
        /// <summary>
        /// Уникальный ID записи
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Локальный путь к файлу
        /// </summary>
        public string LocalPath { get; set; } = string.Empty;

        /// <summary>
        /// Путь на сервере (B2)
        /// </summary>
        public string RemotePath { get; set; } = string.Empty;

        /// <summary>
        /// SHA1 хеш файла
        /// </summary>
        public string ContentSha1 { get; set; } = string.Empty;

        /// <summary>
        /// Размер файла в байтах
        /// </summary>
        public long ContentLength { get; set; }

        /// <summary>
        /// Время загрузки
        /// </summary>
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// CDN URL файла
        /// </summary>
        public string CdnUrl { get; set; } = string.Empty;

        /// <summary>
        /// Статус загрузки: Uploaded, Failed
        /// </summary>
        public string Status { get; set; } = "Uploaded";

        /// <summary>
        /// B2 File ID (для удаления)
        /// </summary>
        public string? FileId { get; set; }

        /// <summary>
        /// Имя проекта (для группировки)
        /// </summary>
        public string? ProjectName { get; set; }

        /// <summary>
        /// Сообщение об ошибке (если Status = Failed)
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}

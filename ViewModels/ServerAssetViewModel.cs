using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AssetProcessor.Helpers;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// ViewModel для отображения ассета на сервере
    /// </summary>
    public class ServerAssetViewModel : INotifyPropertyChanged {
        private string _remotePath = string.Empty;
        private string _fileName = string.Empty;
        private long _size;
        private DateTime _uploadedAt;
        private string _contentSha1 = string.Empty;
        private string _cdnUrl = string.Empty;
        private string? _localPath;
        private string _syncStatus = "Unknown";
        private string? _fileId;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Путь на сервере (B2)
        /// </summary>
        public string RemotePath {
            get => _remotePath;
            set { _remotePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); }
        }

        /// <summary>
        /// Имя файла (вычисляется из RemotePath)
        /// </summary>
        public string FileName => System.IO.Path.GetFileName(_remotePath);

        /// <summary>
        /// Путь к папке (для группировки)
        /// </summary>
        public string FolderPath {
            get {
                var dir = System.IO.Path.GetDirectoryName(_remotePath)?.Replace('\\', '/');
                return string.IsNullOrEmpty(dir) ? "/" : dir;
            }
        }

        /// <summary>
        /// Размер файла в байтах
        /// </summary>
        public long Size {
            get => _size;
            set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
        }

        /// <summary>
        /// Форматированный размер для отображения
        /// </summary>
        public string SizeDisplay {
            get {
                if (_size < 1024) return $"{_size} B";
                if (_size < 1024 * 1024) return $"{_size / 1024.0:F1} KB";
                return $"{_size / (1024.0 * 1024.0):F2} MB";
            }
        }

        /// <summary>
        /// Время загрузки
        /// </summary>
        public DateTime UploadedAt {
            get => _uploadedAt;
            set { _uploadedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(UploadedAtDisplay)); }
        }

        /// <summary>
        /// Форматированная дата загрузки
        /// </summary>
        public string UploadedAtDisplay => _uploadedAt.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// SHA1 хеш файла
        /// </summary>
        public string ContentSha1 {
            get => _contentSha1;
            set { _contentSha1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentSha1Short)); }
        }

        /// <summary>
        /// Короткий SHA1 для отображения
        /// </summary>
        public string ContentSha1Short => _contentSha1.Length > 8 ? _contentSha1[..8] + "..." : _contentSha1;

        /// <summary>
        /// CDN URL для доступа к файлу
        /// </summary>
        public string CdnUrl {
            get => _cdnUrl;
            set { _cdnUrl = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Локальный путь (если найден)
        /// </summary>
        public string? LocalPath {
            get => _localPath;
            set { _localPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLocalFile)); }
        }

        /// <summary>
        /// Есть ли локальный файл
        /// </summary>
        public bool HasLocalFile => !string.IsNullOrEmpty(_localPath) && System.IO.File.Exists(_localPath);

        /// <summary>
        /// B2 File ID (для удаления)
        /// </summary>
        public string? FileId {
            get => _fileId;
            set { _fileId = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Статус синхронизации: Synced, LocalOnly, ServerOnly, HashMismatch
        /// </summary>
        public string SyncStatus {
            get => _syncStatus;
            set { _syncStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(SyncStatusColor)); }
        }

        /// <summary>
        /// Цвет статуса для отображения (видимый на обоих темах, cached)
        /// </summary>
        public Brush SyncStatusColor => SyncStatus switch {
            "Synced" => ThemeBrushCache.SyncedBrush,
            "LocalOnly" => ThemeBrushCache.LocalOnlyBrush,
            "ServerOnly" => ThemeBrushCache.ServerOnlyBrush,
            "HashMismatch" => ThemeBrushCache.HashMismatchBrush,
            "Outdated" => ThemeBrushCache.OutdatedBrush,
            _ => ThemeBrushCache.UnknownBrush
        };

        /// <summary>
        /// Тип файла (по расширению)
        /// </summary>
        public string FileType {
            get {
                var ext = System.IO.Path.GetExtension(_remotePath).ToLowerInvariant();
                return ext switch {
                    ".ktx2" => "Texture",
                    ".glb" => "Model",
                    ".gltf" => "Model",
                    ".json" => "JSON",
                    ".bin" => "Binary",
                    ".png" => "Image",
                    ".jpg" or ".jpeg" => "Image",
                    _ => "Other"
                };
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

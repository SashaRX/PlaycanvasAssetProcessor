using System.ComponentModel;

namespace AssetProcessor.Resources {
    public abstract class BaseResource : INotifyPropertyChanged {
        private int id;
        private string? name;
        private string? extension;
        private int index;
        private int size;
        private string? status;
        private string? hash;
        private string? url;
        private string? path;
        private string? type;
        private double downloadProgress;
        private int? folder;
        private int? parent;
        private bool exportToServer;
        private string? uploadStatus;
        private string? uploadedHash;
        private DateTime? lastUploadedAt;
        private string? remoteUrl;
        private double uploadProgress;

        /// <summary>
        /// Флаг для пометки ресурса к экспорту на сервер
        /// </summary>
        public bool ExportToServer {
            get => exportToServer;
            set {
                if (exportToServer != value) {
                    exportToServer = value;
                    OnPropertyChanged(nameof(ExportToServer));
                }
            }
        }

        /// <summary>
        /// Статус загрузки: "Queued", "Uploading", "Uploaded", "Upload Failed", "Upload Outdated"
        /// </summary>
        public string? UploadStatus {
            get => uploadStatus;
            set {
                if (uploadStatus != value) {
                    uploadStatus = value;
                    OnPropertyChanged(nameof(UploadStatus));
                    OnPropertyChanged(nameof(UploadStatusOrProgress));
                }
            }
        }

        /// <summary>
        /// Отображение статуса или прогресса загрузки
        /// </summary>
        public string? UploadStatusOrProgress {
            get {
                if (UploadStatus == "Uploading") {
                    return $"{UploadProgress:F0}%";
                }
                return UploadStatus;
            }
        }

        /// <summary>
        /// SHA1 хеш загруженного файла для версионирования
        /// </summary>
        public string? UploadedHash {
            get => uploadedHash;
            set {
                if (uploadedHash != value) {
                    uploadedHash = value;
                    OnPropertyChanged(nameof(UploadedHash));
                }
            }
        }

        /// <summary>
        /// Время последней загрузки на сервер
        /// </summary>
        public DateTime? LastUploadedAt {
            get => lastUploadedAt;
            set {
                if (lastUploadedAt != value) {
                    lastUploadedAt = value;
                    OnPropertyChanged(nameof(LastUploadedAt));
                }
            }
        }

        /// <summary>
        /// URL файла на Backblaze B2
        /// </summary>
        public string? RemoteUrl {
            get => remoteUrl;
            set {
                if (remoteUrl != value) {
                    remoteUrl = value;
                    OnPropertyChanged(nameof(RemoteUrl));
                }
            }
        }

        /// <summary>
        /// Прогресс загрузки на сервер (0-100)
        /// </summary>
        public double UploadProgress {
            get => uploadProgress;
            set {
                if (Math.Abs(uploadProgress - value) > 0.5) {
                    uploadProgress = value;
                    OnPropertyChanged(nameof(UploadProgress));
                    if (UploadStatus == "Uploading") {
                        OnPropertyChanged(nameof(UploadStatusOrProgress));
                    }
                } else if (uploadProgress != value) {
                    uploadProgress = value;
                }
            }
        }

        public int ID {
            get { return id; }
            set {
                id = value;
                OnPropertyChanged(nameof(ID));
            }
        }


        public int Index {
            get { return index; }
            set {
                index = value;
                OnPropertyChanged(nameof(Index));
            }
        }

        public string? Name {
            get => name;
            set {
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string? Extension {
            get => extension;
            set {
                extension = value;
                OnPropertyChanged(nameof(Extension));
            }
        }

        public int Size {
            get => size;
            set {
                size = value;
                OnPropertyChanged(nameof(Size));
            }
        }

        public string? Status {
            get => status;
            set {
                if (status != value) {
                    status = value;
                    OnPropertyChanged(nameof(Status));
                    // StatusOrProgress is derived from Status, notify only when needed
                    if (value != "Downloading") {
                        OnPropertyChanged(nameof(StatusOrProgress));
                    }
                }
            }
        }

        public string? StatusOrProgress {
            get {
                if (Status == "Downloading") {
                    return $"{DownloadProgress:F0}%";
                }
                return Status;
            }
        }

        public double DownloadProgress {
            get => downloadProgress;
            set {
                if (Math.Abs(downloadProgress - value) > 0.5) { // Throttle: notify only when change > 0.5%
                    downloadProgress = value;
                    OnPropertyChanged(nameof(DownloadProgress));
                    if (Status == "Downloading") {
                        OnPropertyChanged(nameof(StatusOrProgress));
                    }
                } else if (downloadProgress != value) {
                    downloadProgress = value; // Update value but don't notify
                }
            }
        }

        public string? Hash {
            get => hash;
            set {
                hash = value;
                OnPropertyChanged(nameof(Hash));
            }
        }

        public string? Url {
            get => url;
            set {
                url = value;
                OnPropertyChanged(nameof(Url));
            }
        }

        public string? Path {
            get => path;
            set {
                // Sanitize path: remove newlines, carriage returns, and trim whitespace
                // This prevents issues with paths containing line breaks (from clipboard paste, etc.)
                path = value?.Replace("\r", "").Replace("\n", "").Trim();
                OnPropertyChanged(nameof(Path));
            }
        }

        public string? Type {
            get => type;
            set {
                type = value;
                OnPropertyChanged(nameof(Type));
            }
        }

        public int? Folder {
            get => folder;
            set {
                folder = value;
                OnPropertyChanged(nameof(Folder));
            }
        }

        public int? Parent {
            get => parent;
            set {
                parent = value;
                OnPropertyChanged(nameof(Parent));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string? propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

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
                status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusOrProgress));
            }
        }

        public string? StatusOrProgress {
            get {
                if (Status == "Downloading") {
                    return $"{DownloadProgress}%";
                }
                return Status;
            }
        }

        public double DownloadProgress {
            get => downloadProgress;
            set {
                downloadProgress = value;
                OnPropertyChanged(nameof(DownloadProgress));
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

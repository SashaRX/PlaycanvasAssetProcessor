using System.ComponentModel;

namespace TexTool.Resources {
    public abstract class BaseResource : INotifyPropertyChanged {
        private string? name;
        private string? extension;
        private int size;
        private string? status;
        private string? hash;
        private string? url;
        private string? path;

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
                path = value;
                OnPropertyChanged(nameof(Path));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.ComponentModel;

namespace TexTool {
    public partial class MainWindow : Window {
        private const string ProjectId = "1054788";
        private const string BranchId = "55d4b774-8ecf-4a72-9798-9ca0e83304f0";
        private const string PlaycanvasApiKey = "o5lPWdvxh6lCMtw6jlvlF8jqnhq1RjGd";
        private const string baseUrl = "https://playcanvas.com";
        private const int MaxConcurrentRequests = 32; // Максимальное количество одновременных запросов

        private ObservableCollection<Texture> textures = new ObservableCollection<Texture>();
        private static readonly HttpClient client = new HttpClient();
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(MaxConcurrentRequests);

        public MainWindow() {
            InitializeComponent();
            UpdateConnectionStatus(false);
            TexturesDataGrid.ItemsSource = textures;
            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
        }

        private void UpdateConnectionStatus(bool isConnected) {
            Dispatcher.Invoke(() => {
                ConnectionStatusTextBlock.Text = isConnected ? "Connected" : "Disconnected";
                ConnectionStatusTextBlock.Foreground = isConnected ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
            });
        }

        private void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        }

        private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e) {
            var texture = e.Row.Item as Texture;
            if (texture != null && texture.Status == "Error") {
                e.Row.Background = new SolidColorBrush(Colors.LightCoral);
            } else {
                e.Row.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private async Task TryConnect() {
            var assets = await GetAssetsAsync();
            if (assets != null) {
                UpdateConnectionStatus(true);
                Dispatcher.Invoke(() => textures.Clear());

                int textureCount = assets.Count(asset => asset["file"] != null && asset["type"]?.ToString() == "texture");

                Dispatcher.Invoke(() => {
                    ProgressBar.Value = 0;
                    ProgressBar.Maximum = textureCount;
                    ProgressTextBlock.Text = $"0/{textureCount}";
                });

                var tasks = assets
                    .Where(asset => asset["file"] != null && asset["type"]?.ToString() == "texture")
                    .Select(asset => ProcessAsset(asset, textureCount));

                await Task.WhenAll(tasks);
            } else {
                UpdateConnectionStatus(false);
            }
        }

        private async Task ProcessAsset(JToken asset, int textureCount) {
            await semaphore.WaitAsync();
            try {
                var file = asset["file"];
                if (file != null) {
                    string fileUrl = file["url"] != null ? $"{baseUrl}{file["url"]}" : string.Empty;

                    var texture = new Texture {
                        Name = asset["name"]?.ToString() ?? "Unknown",
                        Size = int.TryParse(file["size"]?.ToString(), out var size) ? size : 0,
                        Url = fileUrl.Split('?')[0],
                        Resolution = new int[] { 0, 0 },
                        ResizeResolution = new int[] { 0, 0 },
                        Status = "On Server",
                        Hash = file["hash"]?.ToString() ?? string.Empty
                    };

                    await Dispatcher.InvokeAsync(() => textures.Add(texture));
                    await UpdateTextureResolutionAsync(texture);

                    Dispatcher.Invoke(() => {
                        ProgressBar.Value++;
                        ProgressTextBlock.Text = $"{ProgressBar.Value}/{textureCount}";
                    });
                }
            } finally {
                semaphore.Release();
            }
        }


        private async Task UpdateTextureResolutionAsync(Texture texture) {
            try {
                Debug.WriteLine($"Fetching resolution for: {texture.Name} ({texture.Url})");
                if (texture.Url != null) {
                    var resolution = await GetImageResolutionAsync(texture.Url);
                    texture.Resolution = new int[] { resolution.Width, resolution.Height };
                }
                Debug.WriteLine($"Fetched resolution for: {texture.Name} - {string.Join("x", texture.Resolution)}");
            } catch (Exception ex) {
                Debug.WriteLine($"Error fetching resolution for: {texture.Name} - {ex.Message}");
                texture.Resolution = new int[] { 0, 0 };
                texture.Status = "Error";
            }
        }

        private async void Connect(object sender, RoutedEventArgs e) {
            await Task.Run(TryConnect);
        }

        private async void GetList(object sender, RoutedEventArgs e) {
            await Task.Run(TryConnect);
        }

        private async void Setting(object sender, RoutedEventArgs e) {
            await Task.Run(() => {
                // Логика настройки
            });
        }

        private async Task<JArray?> GetAssetsAsync() {
            try {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PlaycanvasApiKey);
                var response = await client.GetAsync($"https://playcanvas.com/api/projects/{ProjectId}/assets?branchId={BranchId}&skip=0&limit=10000");

                response.EnsureSuccessStatusCode();

                var responseData = await response.Content.ReadAsStringAsync();
                var assetsResponse = JObject.Parse(responseData);

                Console.WriteLine("Response Status:", response.StatusCode);
                return (JArray?)assetsResponse["result"];
            } catch (HttpRequestException e) {
                Console.Error.WriteLine("Request error:", e);
                return null;
            }
        }

        private async Task<(int Width, int Height)> GetImageResolutionAsync(string url) {
            if (string.IsNullOrEmpty(url)) {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            try {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PlaycanvasApiKey);
                client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 24);
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var buffer = await response.Content.ReadAsByteArrayAsync();

                if (buffer.Length < 24) {
                    throw new Exception("Unable to read image header");
                }

                if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47 &&
                    buffer[4] == 0x0D && buffer[5] == 0x0A && buffer[6] == 0x1A && buffer[7] == 0x0A) {
                    int width = BitConverter.ToInt32(new byte[] { buffer[19], buffer[18], buffer[17], buffer[16] }, 0);
                    int height = BitConverter.ToInt32(new byte[] { buffer[23], buffer[22], buffer[21], buffer[20] }, 0);
                    return (width, height);
                }

                throw new Exception("Image format not supported or not a PNG");
            } catch (Exception ex) {
                Debug.WriteLine($"Error in GetImageResolutionAsync: {ex.Message}");
                throw;
            }
        }
    }

    public class Texture : INotifyPropertyChanged {
        private string? name;
        private int size;
        private int[] resolution = new int[2];
        private int[] resizeResolution = new int[2];
        private string? status;
        private string? hash;
        private string? url;

        public string? Name {
            get => name;
            set {
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public int Size {
            get => size;
            set {
                size = value;
                OnPropertyChanged(nameof(Size));
            }
        }

        public int[] Resolution {
            get => resolution;
            set {
                resolution = value;
                OnPropertyChanged(nameof(Resolution));
                OnPropertyChanged(nameof(ResolutionArea));
            }
        }

        public int[] ResizeResolution {
            get => resizeResolution;
            set {
                resizeResolution = value;
                OnPropertyChanged(nameof(ResizeResolution));
                OnPropertyChanged(nameof(ResizeResolutionArea));
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

        public int ResolutionArea => Resolution[0] * Resolution[1];
        public int ResizeResolutionArea => ResizeResolution[0] * ResizeResolution[1];

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SizeConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value?.ToString() ?? "0";  // Ensures that a non-null string is always returned
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }


    public class ResolutionConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int[] resolution && resolution.Length == 2) {
                return $"{resolution[0]}x{resolution[1]}";
            }
            return "0x0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}

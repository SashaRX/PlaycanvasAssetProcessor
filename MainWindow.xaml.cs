using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Ookii.Dialogs.Wpf;
using System.Net;
using System.Linq;
using System.Windows.Documents;

namespace TexTool {
    public partial class MainWindow : Window, INotifyPropertyChanged {
        private ObservableCollection<Texture> textures = new ObservableCollection<Texture>();
        private static readonly HttpClient client = new HttpClient();
        private SemaphoreSlim? semaphore;
        private string? folderName = string.Empty;

        private string? userName = string.Empty;
        private string? userID = string.Empty;

        public MainWindow() {
            InitializeComponent();
            UpdateConnectionStatus(false);
            TexturesDataGrid.ItemsSource = textures;
            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
            DataContext = this;

            InitializeSemaphore();

            this.Closing += MainWindow_Closing;
            LoadLastSettings(); // Загружаем последние настройки
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();



        private void InitializeSemaphore() {
            semaphore = new SemaphoreSlim(Settings.Default.SemaphoreLimit);
        }

        private string? selectedFolderPath = "Textures Download Folder";
        public string? SelectedFolderPath {
            get => selectedFolderPath;
            set {
                selectedFolderPath = value;
                OnPropertyChanged(nameof(SelectedFolderPath));
            }
        }

        private bool isDownloadButtonEnabled = false;
        public bool IsDownloadButtonEnabled {
            get => isDownloadButtonEnabled;
            set {
                isDownloadButtonEnabled = value;
                OnPropertyChanged(nameof(IsDownloadButtonEnabled));
            }
        }

        private async Task<string> GetUserIdAsync(string username) {
            string url = $"https://playcanvas.com/api/users/{username}";

            SetDefaultRequestHeaders();

            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) {
                await HandleHttpError(response);
                throw new Exception($"Failed to get user ID: {response.StatusCode}");
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseBody)) {
                throw new Exception("Empty response body");
            }

            var json = JObject.Parse(responseBody);
            string? userId = json["id"]?.ToString();

            if (string.IsNullOrEmpty(userId)) {
                throw new Exception("User ID not found in response");
            } else {
                UpdateConnectionStatus(true, $"by userID: {userId}");
                userID = userId;
            }

            return userId;
        }

        private async Task<Dictionary<string, string>> GetProjectsAsync(string userId) {
            string url = $"https://playcanvas.com/api/users/{userId}/projects";

            SetDefaultRequestHeaders();

            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) {
                await HandleHttpError(response);
                throw new Exception($"Failed to get projects: {response.StatusCode}");
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseBody)) {
                throw new Exception("Empty response body");
            }

            var json = JObject.Parse(responseBody);
            var projectsArray = json["result"] as JArray;
            if (projectsArray == null) {
                throw new Exception("Expected an array in 'result'");
            }

            var projects = new Dictionary<string, string>();
            foreach (var project in projectsArray) {
                string? projectId = project["id"]?.ToString();
                string? projectName = project["name"]?.ToString();
                if (!string.IsNullOrEmpty(projectId) && !string.IsNullOrEmpty(projectName)) {
                    projects.Add(projectId, projectName);
                }
            }

            return projects;
        }

        private async void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ProjectsComboBox.SelectedItem != null) {
                string? projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                try {
                    if (string.IsNullOrEmpty(projectId)) {
                        throw new Exception("Project ID is null or empty");
                    }
                    var branches = await GetBranchesAsync(projectId);
                    BranchesComboBox.ItemsSource = branches;
                    BranchesComboBox.DisplayMemberPath = "Value";
                    BranchesComboBox.SelectedValuePath = "Key";
                    if (branches.Count > 0) {
                        BranchesComboBox.SelectedIndex = 0; // Select the first branch by default
                    }
                    SaveCurrentSettings(); // Сохраняем текущие настройки при изменении проекта
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        private void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SaveCurrentSettings(); // Сохраняем текущие настройки при изменении ветки
        }

        private async Task<Dictionary<string, string>> GetBranchesAsync(string projectId) {
            string url = $"https://playcanvas.com/api/projects/{projectId}/branches";

            SetDefaultRequestHeaders();

            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) {
                await HandleHttpError(response);
                throw new Exception($"Failed to get branches: {response.StatusCode}");
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseBody)) {
                throw new Exception("Empty response body");
            }

            var json = JObject.Parse(responseBody);
            var branchesArray = json["result"] as JArray;
            if (branchesArray == null) {
                throw new Exception("Expected an array in 'result'");
            }

            var branches = new Dictionary<string, string>();
            foreach (var branch in branchesArray) {
                string? branchID = branch["id"]?.ToString();
                string? branchName = branch["name"]?.ToString();
                if (!string.IsNullOrEmpty(branchID) && !string.IsNullOrEmpty(branchName)) {
                    branches.Add(branchID, branchName);
                }
            }

            return branches;
        }

        private void UpdateConnectionStatus(bool isConnected, string message = "") {
            if (isConnected) {
                ConnectionStatusTextBlock.Text = string.IsNullOrEmpty(message) ? "Connected" : $"Connected: {message}";
                ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            } else {
                ConnectionStatusTextBlock.Text = string.IsNullOrEmpty(message) ? "Disconnected" : $"Error: {message}";
                ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private async Task HandleHttpError(HttpResponseMessage response) {
            string message = response.StatusCode switch {
                HttpStatusCode.Unauthorized => "401: Unauthorized. Please check your API key.",
                HttpStatusCode.Forbidden => "403: Forbidden. You don't have permission to access this resource.",
                HttpStatusCode.NotFound => "404: Project or Asset not found.",
                HttpStatusCode.TooManyRequests => "429: Too many requests. Please try again later.",
                _ => $"{(int)response.StatusCode}: {response.ReasonPhrase}"
            };

            // Logging the error
            logger.Error($"HTTP error: {message}");

            // Updating the connection status with the error message
            await Dispatcher.InvokeAsync(() => {
                UpdateConnectionStatus(false, message);
                MessageBox.Show(message); // Displaying the error message to the user
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

        private void MainWindow_Closing(object? sender, CancelEventArgs e) {
            SaveCurrentSettings(); // Сохраняем текущие настройки при закрытии
        }

        private async void GetListTextures(object sender, RoutedEventArgs e) {
            await TryConnect();
        }

        private void SetDefaultRequestHeaders() {
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);
        }

        private async Task TryConnect() {
            var assets = await GetAssetsAsync();
            if (assets != null) {
                UpdateConnectionStatus(true);
                textures.Clear();

                int textureCount = assets.Count(asset => asset["file"] != null && asset["type"]?.ToString() == "texture");

                ProgressBar.Value = 0;
                ProgressBar.Maximum = textureCount;
                ProgressTextBlock.Text = $"0/{textureCount}";

                var tasks = assets
                    .Where(asset => asset["file"] != null && asset["type"]?.ToString() == "texture")
                    .Select(async asset => {
                        await ProcessAsset(asset, textureCount);
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value++;
                            ProgressTextBlock.Text = $"{ProgressBar.Value}/{textureCount}";
                        });
                    });

                await Task.WhenAll(tasks);
            } else {
                // Если assets равно null, то произошла ошибка подключения, сообщение об ошибке уже установлено в HandleHttpError
                if (ConnectionStatusTextBlock.Text == "Disconnected") {
                    UpdateConnectionStatus(false, "Failed to connect");
                }
            }
        }

        private async Task ProcessAsset(JToken asset, int textureCount) {
            if (semaphore == null) throw new InvalidOperationException("Semaphore is not initialized.");
            await semaphore.WaitAsync();
            try {
                var file = asset["file"];
                if (file != null) {
                    string fileUrl = file["url"] != null ? $"{Settings.Default.BaseUrl}{file["url"]}" : string.Empty;

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
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (string.IsNullOrEmpty(texture.Url)) {
                Debug.WriteLine($"Texture URL is null or empty for texture: {texture.Name}");
                texture.Resolution = new int[] { 0, 0 };
                texture.Status = "Error";
                return;
            }

            try {
                Debug.WriteLine($"Fetching resolution for: {texture.Name} ({texture.Url})");

                var resolution = await GetImageResolutionAsync(texture.Url);
                texture.Resolution = new int[] { resolution.Width, resolution.Height };

                Debug.WriteLine($"Fetched resolution for: {texture.Name} - {string.Join("x", texture.Resolution)}");
            } catch (HttpRequestException ex) {
                Debug.WriteLine($"HTTP request error fetching resolution for: {texture.Name} - {ex.Message}");
                texture.Resolution = new int[] { 0, 0 };
                texture.Status = "Error";
            } catch (Exception ex) {
                Debug.WriteLine($"General error fetching resolution for: {texture.Name} - {ex.Message}");
                texture.Resolution = new int[] { 0, 0 };
                texture.Status = "Error";
            }
        }

        private async void Connect(object sender, RoutedEventArgs e) {
            if (Settings.Default.PlaycanvasApiKey == "" || Settings.Default.Username == "") {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
            } else {
                try {
                    userName = Settings.Default.Username.ToLower();
                    userID = await GetUserIdAsync(userName);
                    if (string.IsNullOrEmpty(userID)) {
                        throw new Exception("User ID is null or empty");
                    }

                    var projects = await GetProjectsAsync(userID);
                    if (projects != null && projects.Count > 0) {
                        string? lastSelectedProjectId = Settings.Default.LastSelectedProjectId;
                        string? lastSelectedBranchId = Settings.Default.LastSelectedBranchId;

                        ProjectsComboBox.ItemsSource = projects;
                        ProjectsComboBox.DisplayMemberPath = "Value";
                        ProjectsComboBox.SelectedValuePath = "Key";

                        if (!string.IsNullOrEmpty(lastSelectedProjectId) && projects.ContainsKey(lastSelectedProjectId)) {
                            ProjectsComboBox.SelectedValue = lastSelectedProjectId;
                        } else {
                            ProjectsComboBox.SelectedIndex = 0;
                        }

                        if (ProjectsComboBox.SelectedItem != null) {
                            string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                            var branches = await GetBranchesAsync(projectId);

                            if (branches != null && branches.Count > 0) {
                                BranchesComboBox.ItemsSource = branches;

                                if (!string.IsNullOrEmpty(lastSelectedBranchId) && branches.ContainsKey(lastSelectedBranchId)) {
                                    BranchesComboBox.SelectedValue = lastSelectedBranchId;
                                } else {
                                    BranchesComboBox.SelectedIndex = 0;
                                }
                            }
                        }
                    } else {
                        throw new Exception("Project list is empty");
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        private void Download(object sender, RoutedEventArgs e) {
            // Implement your download logic here
        }

        private void SelectFolder(object sender, RoutedEventArgs e) {
            var folderDialog = new VistaFolderBrowserDialog {
                Description = "Select a folder to save downloaded textures",
                UseDescriptionForTitle = true
            };

            // Используем ?? false, чтобы гарантировать, что значение не будет null
            if ((folderDialog.ShowDialog(this) ?? false)) {
                SelectedFolderPath = folderDialog.SelectedPath;
                IsDownloadButtonEnabled = !string.IsNullOrEmpty(SelectedFolderPath);
            }
        }

        private void Setting(object sender, RoutedEventArgs e) {
            var settingsWindow = new SettingsWindow();
            settingsWindow.SettingsSaved += OnSettingsSaved;
            settingsWindow.ShowDialog();
        }

        private void OnSettingsSaved(object? sender, EventArgs e) {
            InitializeSemaphore();
        }

        private async Task<JArray?> GetAssetsAsync() {
            try {
                // Получаем выбранный проект и ветку
                string? selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                string? selectedBranchID = BranchesComboBox.SelectedValue?.ToString();

                // Обновляем URL запроса, используя данные о проекте и ветке
                string url = $"https://playcanvas.com/api/projects/{selectedProjectId}/assets?branch={selectedBranchID}&skip=0&limit=10000";

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);
                var response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode) {
                    var responseData = await response.Content.ReadAsStringAsync();
                    var assetsResponse = JObject.Parse(responseData);
                    return (JArray?)assetsResponse["result"];
                } else {
                    await HandleHttpError(response);
                    return null;
                }
            } catch (HttpRequestException e) {
                await Dispatcher.InvokeAsync(() => {
                    UpdateConnectionStatus(false, "Network error: " + e.Message);
                });

                Console.Error.WriteLine("Request error:", e);
                return null;
            }
        }

        private async Task<(int Width, int Height)> GetImageResolutionAsync(string url) {
            if (string.IsNullOrEmpty(url)) {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            try {
                SetDefaultRequestHeaders();
                client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 24);

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var buffer = await response.Content.ReadAsByteArrayAsync();

                if (buffer.Length < 24) {
                    throw new Exception("Unable to read image header");
                }

                // Проверка, является ли файл PNG
                if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47 &&
                    buffer[4] == 0x0D && buffer[5] == 0x0A && buffer[6] == 0x1A && buffer[7] == 0x0A) {
                    int width = BitConverter.ToInt32(new byte[] { buffer[19], buffer[18], buffer[17], buffer[16] }, 0);
                    int height = BitConverter.ToInt32(new byte[] { buffer[23], buffer[22], buffer[21], buffer[20] }, 0);
                    return (width, height);
                }

                // Можно добавить поддержку других форматов изображений (JPEG, BMP и т.д.)
                throw new Exception("Image format not supported or not a PNG");
            } catch (HttpRequestException e) {
                Debug.WriteLine($"HTTP request error in GetImageResolutionAsync: {e.Message}");
                throw;
            } catch (Exception ex) {
                Debug.WriteLine($"General error in GetImageResolutionAsync: {ex.Message}");
                throw;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SaveCurrentSettings() {
            if (ProjectsComboBox.SelectedItem != null) {
                Settings.Default.LastSelectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
            }
            if (BranchesComboBox.SelectedItem != null) {
                Settings.Default.LastSelectedBranchId = BranchesComboBox.SelectedValue.ToString();
                Settings.Default.LastSelectedBranchName = BranchesComboBox.Text;
            }
            Settings.Default.Save();
        }

        private async void LoadLastSettings() {
            try {
                userName = Settings.Default.Username.ToLower();
                userID = await GetUserIdAsync(userName);
                var projects = await GetProjectsAsync(userID);

                if (projects != null && projects.Count > 0) {
                    ProjectsComboBox.ItemsSource = projects;
                    ProjectsComboBox.DisplayMemberPath = "Value";
                    ProjectsComboBox.SelectedValuePath = "Key";

                    // Восстанавливаем последний выбранный проект
                    if (!string.IsNullOrEmpty(Settings.Default.LastSelectedProjectId) && projects.ContainsKey(Settings.Default.LastSelectedProjectId)) {
                        ProjectsComboBox.SelectedValue = Settings.Default.LastSelectedProjectId;
                    } else {
                        ProjectsComboBox.SelectedIndex = 0; // Выбираем первый проект, если сохраненный проект не найден
                    }
                }

                if (ProjectsComboBox.SelectedItem != null) {
                    string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                    var branches = await GetBranchesAsync(projectId);

                    if (branches != null && branches.Count > 0) {
                        BranchesComboBox.ItemsSource = branches;

                        // Восстанавливаем последнюю выбранную ветку
                        if (!string.IsNullOrEmpty(Settings.Default.LastSelectedBranchId) && branches.ContainsKey(Settings.Default.LastSelectedBranchId)) {
                            BranchesComboBox.SelectedValue = Settings.Default.LastSelectedBranchId;
                        } else {
                            BranchesComboBox.SelectedIndex = 0; // Выбираем первую ветку, если сохраненная ветка не найдена
                        }
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error loading last settings: {ex.Message}");
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
                OnPropertyChanged(nameof(ResolutionArea)); // Trigger PropertyChanged for ResolutionArea
            }
        }

        public int[] ResizeResolution {
            get => resizeResolution;
            set {
                resizeResolution = value;
                OnPropertyChanged(nameof(ResizeResolution));
                OnPropertyChanged(nameof(ResizeResolutionArea)); // Trigger PropertyChanged for ResizeResolutionArea
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
            if (value is int size) {
                double sizeInMB = Math.Round(size / 1_000_000.0, 2);
                return $"{sizeInMB} MB";
            }
            return "0 MB";
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

using Newtonsoft.Json.Linq;
using Ookii.Dialogs.Wpf;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SixLabors.ImageSharp;
using TexTool.Resources;
using TexTool.Helpers;

namespace TexTool {
    public partial class MainWindow : Window, INotifyPropertyChanged {
        private readonly ObservableCollection<TextureResource> textures = [];
        private readonly SemaphoreSlim semaphore = new(Settings.Default.SemaphoreLimit);
        private string userName = string.Empty;
        private string userID = string.Empty;

        private readonly List<string> supportedFormats = [".png", ".jpg", ".jpeg"];
        private readonly List<string> excludedFormats = [".hdr", ".avif"];

        private CancellationTokenSource cancellationTokenSource = new();
        private readonly PlayCanvasService playCanvasService = new();

        private ObservableCollection<KeyValuePair<string, string>> projects = [];
        public ObservableCollection<KeyValuePair<string, string>> Projects {
            get { return projects; }
            set {
                projects = value;
                OnPropertyChanged(nameof(Projects));
            }
        }

        private readonly ObservableCollection<Branch> branches = [];
        public ObservableCollection<Branch> Branches {
            get { return branches; }
        }

        public MainWindow() {
            InitializeComponent();
            UpdateConnectionStatus(false);
            TexturesDataGrid.ItemsSource = textures;
            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
            DataContext = this;
            this.Closing += MainWindow_Closing;
            LoadLastSettings();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string selectedFolderPath = "Textures Download Folder";
        public string SelectedFolderPath {
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

        #region UI Event Handlers

        private void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            _ = LoadBranchesAsync();
        }

        private void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SaveCurrentSettings();
        }

        private void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs? e) {
            if (e != null && e.Row != null && e.Row.Item != null) {
                if (e.Row.Item is TextureResource texture && texture.Status != null && texture.Status == "Error") {
                    e.Row.Background = new SolidColorBrush(Colors.LightCoral);
                } else {
                    e.Row.Background = new SolidColorBrush(Colors.Transparent);
                }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs? e) {
            SaveCurrentSettings();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
        }

        private void SelectFolder(object sender, RoutedEventArgs e) {
            var folderDialog = new VistaFolderBrowserDialog {
                Description = "Select a folder to save downloaded textures",
                UseDescriptionForTitle = true
            };

            if ((folderDialog.ShowDialog(this) ?? false)) {
                SelectedFolderPath = folderDialog.SelectedPath;
                IsDownloadButtonEnabled = !string.IsNullOrEmpty(SelectedFolderPath);
            }
        }

        private void Setting(object sender, RoutedEventArgs e) {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ClearTextures(object sender, RoutedEventArgs e) {
            textures?.Clear();
        }

        private async void GetListTextures(object? sender, RoutedEventArgs? e) {
            try {
                CancelButton.IsEnabled = true;
                if (cancellationTokenSource != null) {
                    await TryConnect(cancellationTokenSource.Token);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in GetListTextures: {ex.Message}");
                LogError($"Error in GetListTextures: {ex}");
            } finally {
                CancelButton.IsEnabled = false;
            }
        }

        private async void Connect(object sender, RoutedEventArgs e) {
            var cancellationToken = cancellationTokenSource.Token;

            if (string.IsNullOrEmpty(Settings.Default.PlaycanvasApiKey) || string.IsNullOrEmpty(Settings.Default.Username)) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
            } else {
                try {
                    userName = Settings.Default.Username.ToLower();
                    userID = await playCanvasService.GetUserIdAsync(userName, Settings.Default.PlaycanvasApiKey, cancellationToken);
                    if (string.IsNullOrEmpty(userID)) {
                        throw new Exception("User ID is null or empty");
                    } else {
                        await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {userID}"));
                    }

                    var projectsDict = await playCanvasService.GetProjectsAsync(userID, Settings.Default.PlaycanvasApiKey, cancellationToken);
                    if (projectsDict != null && projectsDict.Count > 0) {
                        string lastSelectedProjectId = Settings.Default.LastSelectedProjectId;
                        string lastSelectedBranchName = Settings.Default.LastSelectedBranchName;

                        Projects.Clear();
                        foreach (var project in projectsDict) {
                            Projects.Add(project);
                        }

                        if (!string.IsNullOrEmpty(lastSelectedProjectId) && projectsDict.ContainsKey(lastSelectedProjectId)) {
                            ProjectsComboBox.SelectedValue = lastSelectedProjectId;
                        } else {
                            ProjectsComboBox.SelectedIndex = 0;
                        }

                        if (ProjectsComboBox.SelectedItem != null) {
                            string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                            var branchesList = await playCanvasService.GetBranchesAsync(projectId, Settings.Default.PlaycanvasApiKey, cancellationToken);

                            if (branchesList != null && branchesList.Count > 0) {
                                Branches.Clear();
                                foreach (var branch in branchesList) {
                                    Branches.Add(branch);
                                }

                                if (!string.IsNullOrEmpty(lastSelectedBranchName)) {
                                    var selectedBranch = branchesList.FirstOrDefault(b => b.Name == lastSelectedBranchName);
                                    if (selectedBranch != null) {
                                        BranchesComboBox.SelectedValue = selectedBranch.Id;
                                    } else {
                                        BranchesComboBox.SelectedIndex = 0;
                                    }
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

        #endregion

        #region API Methods

        private async Task TryConnect(CancellationToken cancellationToken) {
            try {
                if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null) {
                    MessageBox.Show("Please select a project and a branch");
                    return;
                }
                if (playCanvasService == null) {
                    MessageBox.Show("PlayCanvasService is null");
                    return;
                }

                var selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                var selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;

                var assets = await playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, Settings.Default.PlaycanvasApiKey, cancellationToken);
                if (assets != null) {
                    UpdateConnectionStatus(true);

                    if (textures != null && textures.Count > 0)
                        textures.Clear();

                    int textureCount = assets.Count(asset => asset["file"] != null && asset["type"]?.ToString() == "texture");

                    ProgressBar.Value = 0;
                    ProgressBar.Maximum = textureCount;
                    ProgressTextBlock.Text = $"0/{textureCount}";

                    var tasks = assets
                        .Where(asset => asset["file"] != null && asset["type"]?.ToString() == "texture")
                        .Select(async asset => {
                            await ProcessAsset(asset, textureCount, cancellationToken);
                            Dispatcher.Invoke(() => {
                                ProgressBar.Value++;
                                ProgressTextBlock.Text = $"{ProgressBar.Value}/{textureCount}";
                            });
                        });

                    await Task.WhenAll(tasks);
                } else {
                    if (ConnectionStatusTextBlock.Text == "Disconnected") {
                        UpdateConnectionStatus(false, "Failed to connect");
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in TryConnect: {ex.Message}");
                LogError($"Error in TryConnect: {ex}");
            }
        }

        private async Task ProcessAsset(JToken asset, int textureCount, CancellationToken cancellationToken) {
            if (semaphore == null) throw new InvalidOperationException("Semaphore is not initialized.");
            await semaphore.WaitAsync(cancellationToken);
            try {
                var file = asset["file"];
                if (file != null) {
                    string fileUrl = file["url"] != null ? $"{Settings.Default.BaseUrl}{file["url"]}" : string.Empty;

                    if (string.IsNullOrEmpty(fileUrl)) {
                        throw new Exception("File URL is null or empty");
                    }

                    string? extension = Path.GetExtension(fileUrl.Split('?')[0])?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(extension)) {
                        throw new Exception("Unable to determine file extension");
                    }

                    if (!IsSupportedFormat(fileUrl)) {
                        throw new Exception($"Unsupported image format: {extension}");
                    }

                    var texture = new TextureResource {
                        Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                        Size = int.TryParse(file["size"]?.ToString(), out var size) ? size : 0,
                        Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                        Path = string.Empty,
                        Extension = extension,
                        Resolution = [0, 0],
                        ResizeResolution = [0, 0],
                        Status = "On Server",
                        Hash = file["hash"]?.ToString() ?? string.Empty
                    };

                    if (textures != null) {
                        await Dispatcher.InvokeAsync(() => textures.Add(texture));
                        await UpdateTextureResolutionAsync(texture, cancellationToken);
                    }

                    Dispatcher.Invoke(() => {
                        ProgressBar.Value++;
                        ProgressTextBlock.Text = $"{ProgressBar.Value}/{textureCount}";
                    });
                }
            } catch (Exception ex) {
                LogError($"Error in ProcessAsset: {ex}");
            } finally {
                semaphore.Release();
            }
        }

        private static async Task UpdateTextureResolutionAsync(TextureResource texture, CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(texture);

            if (string.IsNullOrEmpty(texture.Url)) {
                texture.Resolution = [0, 0];
                texture.Status = "Error";
                return;
            }

            try {
                var resolution = await ImageHelper.GetImageResolutionAsync(texture.Url, cancellationToken);
                texture.Resolution = [resolution.Width, resolution.Height];
                LogError($"Successfully retrieved resolution for {texture.Url}: {resolution.Width}x{resolution.Height}");
            } catch (Exception ex) {
                texture.Resolution = [0, 0];
                texture.Status = "Error";
                LogError($"Exception in UpdateTextureResolutionAsync for {texture.Url}: {ex.Message}");
            }
        }

        private async Task LoadBranchesAsync() {
            if (ProjectsComboBox.SelectedItem != null) {
                string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                try {
                    if (string.IsNullOrEmpty(projectId)) {
                        throw new Exception("Project ID is null or empty");
                    }

                    var branchesList = await playCanvasService.GetBranchesAsync(projectId, Settings.Default.PlaycanvasApiKey, cancellationTokenSource.Token);

                    Branches.Clear();
                    foreach (var branch in branchesList) {
                        Branches.Add(branch);
                    }

                    BranchesComboBox.ItemsSource = Branches;
                    BranchesComboBox.DisplayMemberPath = "Name";
                    BranchesComboBox.SelectedValuePath = "Id";

                    if (branchesList.Count > 0) {
                        BranchesComboBox.SelectedIndex = 0;
                    }
                    SaveCurrentSettings();
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                    UpdateConnectionStatus(false, ex.Message);
                }
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateConnectionStatus(bool isConnected, string message = "") {
            Dispatcher.Invoke(() => {
                if (isConnected) {
                    ConnectionStatusTextBlock.Text = string.IsNullOrEmpty(message) ? "Connected" : $"Connected: {message}";
                    ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                } else {
                    ConnectionStatusTextBlock.Text = string.IsNullOrEmpty(message) ? "Disconnected" : $"Error: {message}";
                    ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
            });
        }

        private void SaveCurrentSettings() {
            if (ProjectsComboBox.SelectedItem != null) {
                Settings.Default.LastSelectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
            }
            if (BranchesComboBox.SelectedItem != null) {
                Settings.Default.LastSelectedBranchName = ((Branch)BranchesComboBox.SelectedItem).Name;
            }
            Settings.Default.Save();
        }

        private async void LoadLastSettings() {
            try {
                userName = Settings.Default.Username.ToLower();
                if (string.IsNullOrEmpty(userName)) {
                    throw new Exception("Username is null or empty");
                }

                var cancellationToken = new CancellationToken();

                userID = await playCanvasService.GetUserIdAsync(userName, Settings.Default.PlaycanvasApiKey, cancellationToken);
                if (string.IsNullOrEmpty(userID)) {
                    throw new Exception("User ID is null or empty");
                } else {
                    UpdateConnectionStatus(true, $"by userID: {userID}");
                }
                var projectsDict = await playCanvasService.GetProjectsAsync(userID, Settings.Default.PlaycanvasApiKey, cancellationToken);

                if (projectsDict != null && projectsDict.Count > 0) {
                    Projects.Clear();
                    foreach (var project in projectsDict) {
                        Projects.Add(project);
                    }

                    if (!string.IsNullOrEmpty(Settings.Default.LastSelectedProjectId) && projectsDict.ContainsKey(Settings.Default.LastSelectedProjectId)) {
                        ProjectsComboBox.SelectedValue = Settings.Default.LastSelectedProjectId;
                    } else {
                        ProjectsComboBox.SelectedIndex = 0;
                    }
                }

                if (ProjectsComboBox.SelectedItem != null) {
                    string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                    var branchesList = await playCanvasService.GetBranchesAsync(projectId, Settings.Default.PlaycanvasApiKey, cancellationToken);

                    if (branchesList != null && branchesList.Count > 0) {
                        Branches.Clear();
                        foreach (var branch in branchesList) {
                            Branches.Add(branch);
                        }

                        BranchesComboBox.ItemsSource = Branches;
                        BranchesComboBox.DisplayMemberPath = "Name";
                        BranchesComboBox.SelectedValuePath = "Id";

                        if (!string.IsNullOrEmpty(Settings.Default.LastSelectedBranchName)) {
                            var selectedBranch = branchesList.FirstOrDefault(b => b.Name == Settings.Default.LastSelectedBranchName);
                            if (selectedBranch != null) {
                                BranchesComboBox.SelectedValue = selectedBranch.Id;
                            } else {
                                BranchesComboBox.SelectedIndex = 0;
                            }
                        } else {
                            BranchesComboBox.SelectedIndex = 0;
                        }
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error loading last settings: {ex.Message}");
            }
        }

        private bool IsSupportedFormat(string fileUrl) {
            string? cleanUrl = fileUrl.Split('?')[0];
            string? extension = Path.GetExtension(cleanUrl)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension)) {
                return false;
            }
            return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private static void LogError(string message) {
            string logFilePath = "error_log.txt";
            File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
        }

        #endregion
    }
}

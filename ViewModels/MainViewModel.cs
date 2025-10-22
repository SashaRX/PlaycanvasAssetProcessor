using AssetProcessor.Exceptions;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using NLog;
using System.Collections.ObjectModel;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// Main ViewModel for the application's primary window
    /// </summary>
    public partial class MainViewModel : ObservableObject {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IPlayCanvasService playCanvasService;

        [ObservableProperty]
        private ObservableCollection<TextureResource> textures = [];

        [ObservableProperty]
        private ObservableCollection<ModelResource> models = [];

        [ObservableProperty]
        private ObservableCollection<MaterialResource> materials = [];

        [ObservableProperty]
        private ObservableCollection<BaseResource> assets = [];

        [ObservableProperty]
        private bool isDownloadButtonEnabled;

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        private Dictionary<string, string> projects = new();

        [ObservableProperty]
        private List<Branch> branches = [];

        [ObservableProperty]
        private string? selectedProjectId;

        [ObservableProperty]
        private string? selectedBranchId;

        [ObservableProperty]
        private string? username;

        [ObservableProperty]
        private string? apiKey;

        public MainViewModel(IPlayCanvasService playCanvasService) {
            this.playCanvasService = playCanvasService;
            logger.Info("MainViewModel initialized");
        }

        [RelayCommand]
        private async Task ConnectAsync(CancellationToken cancellationToken = default) {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(ApiKey)) {
                StatusMessage = "Username and API Key are required";
                logger.Warn("Connection attempt without username or API key");
                return;
            }

            try {
                StatusMessage = "Connecting to PlayCanvas...";
                logger.Info($"Attempting to connect for user: {Username}");

                var userId = await playCanvasService.GetUserIdAsync(Username, ApiKey, cancellationToken);
                logger.Info($"Retrieved user ID: {userId}");

                var projectsDict = await playCanvasService.GetProjectsAsync(userId, ApiKey, new Dictionary<string, string>(), cancellationToken);
                Projects = projectsDict;
                logger.Info($"Retrieved {Projects.Count} projects");

                IsConnected = true;
                StatusMessage = $"Connected successfully. Found {Projects.Count} projects.";
            } catch (InvalidConfigurationException ex) {
                StatusMessage = $"Configuration error: {ex.Message}";
                logger.Error(ex, "Invalid configuration during connection");
                IsConnected = false;
            } catch (PlayCanvasApiException ex) {
                StatusMessage = $"API error: {ex.Message}";
                logger.Error(ex, "PlayCanvas API error during connection");
                IsConnected = false;
            } catch (NetworkException ex) {
                StatusMessage = $"Network error: {ex.Message}";
                logger.Error(ex, "Network error during connection");
                IsConnected = false;
            } catch (Exception ex) {
                StatusMessage = $"Unexpected error: {ex.Message}";
                logger.Error(ex, "Unexpected error during connection");
                IsConnected = false;
            }
        }

        [RelayCommand]
        private async Task LoadBranchesAsync(CancellationToken cancellationToken = default) {
            if (string.IsNullOrEmpty(SelectedProjectId) || string.IsNullOrEmpty(ApiKey)) {
                StatusMessage = "Please select a project first";
                return;
            }

            try {
                StatusMessage = "Loading branches...";
                logger.Info($"Loading branches for project: {SelectedProjectId}");

                var branchesList = await playCanvasService.GetBranchesAsync(SelectedProjectId, ApiKey, new List<Branch>(), cancellationToken);
                Branches = branchesList;
                logger.Info($"Retrieved {Branches.Count} branches");

                StatusMessage = $"Loaded {Branches.Count} branches.";
            } catch (PlayCanvasApiException ex) {
                StatusMessage = $"Failed to load branches: {ex.Message}";
                logger.Error(ex, "Error loading branches");
            } catch (NetworkException ex) {
                StatusMessage = $"Network error: {ex.Message}";
                logger.Error(ex, "Network error loading branches");
            }
        }

        [RelayCommand]
        private async Task LoadAssetsAsync(CancellationToken cancellationToken = default) {
            if (string.IsNullOrEmpty(SelectedProjectId) || string.IsNullOrEmpty(SelectedBranchId) || string.IsNullOrEmpty(ApiKey)) {
                StatusMessage = "Please select a project and branch first";
                return;
            }

            try {
                StatusMessage = "Loading assets...";
                logger.Info($"Loading assets for project: {SelectedProjectId}, branch: {SelectedBranchId}");

                JArray assetsArray = await playCanvasService.GetAssetsAsync(SelectedProjectId, SelectedBranchId, ApiKey, cancellationToken);

                // Clear existing collections
                Textures.Clear();
                Models.Clear();
                Materials.Clear();
                Assets.Clear();

                // Parse and categorize assets
                foreach (JToken asset in assetsArray) {
                    var type = asset["type"]?.ToString();
                    var id = asset["id"]?.ToString();
                    var name = asset["name"]?.ToString();

                    if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id)) continue;

                    switch (type.ToLower()) {
                        case "texture":
                            var texture = new TextureResource {
                                ID = int.TryParse(id, out var texId) ? texId : 0,
                                Name = name,
                                Type = type,
                                Status = "Ready"
                            };
                            Textures.Add(texture);
                            Assets.Add(texture);
                            break;

                        case "model":
                            var model = new ModelResource {
                                ID = int.TryParse(id, out var modelId) ? modelId : 0,
                                Name = name,
                                Type = type,
                                Status = "Ready"
                            };
                            Models.Add(model);
                            Assets.Add(model);
                            break;

                        case "material":
                            var material = new MaterialResource {
                                ID = int.TryParse(id, out var matId) ? matId : 0,
                                Name = name,
                                Type = type,
                                Status = "Ready"
                            };
                            Materials.Add(material);
                            Assets.Add(material);
                            break;
                    }
                }

                IsDownloadButtonEnabled = Assets.Count > 0;
                StatusMessage = $"Loaded {Assets.Count} assets ({Textures.Count} textures, {Models.Count} models, {Materials.Count} materials).";
                logger.Info($"Loaded {Assets.Count} total assets");
            } catch (PlayCanvasApiException ex) {
                StatusMessage = $"Failed to load assets: {ex.Message}";
                logger.Error(ex, "Error loading assets");
            } catch (NetworkException ex) {
                StatusMessage = $"Network error: {ex.Message}";
                logger.Error(ex, "Network error loading assets");
            }
        }

        [RelayCommand]
        private async Task DownloadAssetsAsync(CancellationToken cancellationToken = default) {
            if (Assets.Count == 0) {
                StatusMessage = "No assets to download";
                return;
            }

            StatusMessage = $"Downloading {Assets.Count} assets...";
            logger.Info($"Starting download of {Assets.Count} assets");

            // TODO: Implement download logic
            // This will be implemented in the MainWindow code-behind or a separate service

            await Task.CompletedTask;
        }
    }
}

using AssetProcessor.Exceptions;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

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
        private ObservableCollection<KeyValuePair<string, string>> projects = [];

        [ObservableProperty]
        private ObservableCollection<Branch> branches = [];

        [ObservableProperty]
        private string? selectedProjectId;

        [ObservableProperty]
        private string? selectedBranchId;

        [ObservableProperty]
        private string? username;

        [ObservableProperty]
        private string? apiKey;

        [ObservableProperty]
        private MaterialResource? selectedMaterial;

        [ObservableProperty]
        private ObservableCollection<TextureResource> filteredTextures = [];

        public MainViewModel(IPlayCanvasService playCanvasService) {
            this.playCanvasService = playCanvasService;
            logger.Info("MainViewModel initialized");
        }

        private string? ResolveApiKey() {
            if (!string.IsNullOrWhiteSpace(ApiKey)) {
                return ApiKey;
            }

            try {
                return AppSettings.Default.GetDecryptedPlaycanvasApiKey();
            } catch (CryptographicException ex) {
                logger.Error(ex, "Не удалось расшифровать Playcanvas API key из настроек.");
                StatusMessage = "Ошибка безопасности: проверьте мастер-пароль.";
                return null;
            }
        }

        [RelayCommand]
        private async Task ConnectAsync(CancellationToken cancellationToken = default) {
            string? resolvedApiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(resolvedApiKey)) {
                StatusMessage = "Username and API Key are required";
                logger.Warn("Connection attempt without username or API key");
                return;
            }

            try {
                StatusMessage = "Connecting to PlayCanvas...";
                logger.Info($"Attempting to connect for user: {Username}");

                var userId = await playCanvasService.GetUserIdAsync(Username, resolvedApiKey, cancellationToken);
                logger.Info($"Retrieved user ID: {userId}");

                var projectsDict = await playCanvasService.GetProjectsAsync(userId, resolvedApiKey, new Dictionary<string, string>(), cancellationToken);
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
            string? resolvedApiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(SelectedProjectId) || string.IsNullOrEmpty(resolvedApiKey)) {
                StatusMessage = "Please select a project first";
                return;
            }

            try {
                StatusMessage = "Loading branches...";
                logger.Info($"Loading branches for project: {SelectedProjectId}");

                var branchesList = await playCanvasService.GetBranchesAsync(SelectedProjectId, resolvedApiKey, new List<Branch>(), cancellationToken);
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
            string? resolvedApiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(SelectedProjectId) || string.IsNullOrEmpty(SelectedBranchId) || string.IsNullOrEmpty(resolvedApiKey)) {
                StatusMessage = "Please select a project and branch first";
                return;
            }

            try {
                StatusMessage = "Loading assets...";
                logger.Info($"Loading assets for project: {SelectedProjectId}, branch: {SelectedBranchId}");

                List<PlayCanvasAssetSummary> assetsArray = [];
                await foreach (PlayCanvasAssetSummary asset in playCanvasService.GetAssetsAsync(SelectedProjectId, SelectedBranchId, resolvedApiKey, cancellationToken)) {
                    assetsArray.Add(asset);
                }

                // Clear existing collections
                Textures.Clear();
                Models.Clear();
                Materials.Clear();
                Assets.Clear();

                // Parse and categorize assets
                foreach (PlayCanvasAssetSummary asset in assetsArray) {
                    string? type = asset.Type;
                    int id = asset.Id;
                    string? name = asset.Name;

                    if (string.IsNullOrEmpty(type) || id == 0) {
                        continue;
                    }

                    switch (type.ToLower()) {
                        case "texture":
                            var texture = new TextureResource {
                                ID = id,
                                Name = name,
                                Type = type,
                                Status = "Ready"
                            };
                            Textures.Add(texture);
                            Assets.Add(texture);
                            break;

                        case "model":
                            var model = new ModelResource {
                                ID = id,
                                Name = name,
                                Type = type,
                                Status = "Ready"
                            };
                            Models.Add(model);
                            Assets.Add(model);
                            break;

                        case "material":
                            var material = new MaterialResource {
                                ID = id,
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

        /// <summary>
        /// Фильтрует текстуры на основе выбранного материала
        /// </summary>
        private void FilterTexturesForMaterial(MaterialResource? material) {
            if (material == null) {
                FilteredTextures.Clear();
                return;
            }

            var materialTextureIds = new List<int>();

            // Собираем все ID текстур, используемых в материале
            if (material.DiffuseMapId.HasValue) materialTextureIds.Add(material.DiffuseMapId.Value);
            if (material.SpecularMapId.HasValue) materialTextureIds.Add(material.SpecularMapId.Value);
            if (material.NormalMapId.HasValue) materialTextureIds.Add(material.NormalMapId.Value);
            if (material.GlossMapId.HasValue) materialTextureIds.Add(material.GlossMapId.Value);
            if (material.MetalnessMapId.HasValue) materialTextureIds.Add(material.MetalnessMapId.Value);
            if (material.EmissiveMapId.HasValue) materialTextureIds.Add(material.EmissiveMapId.Value);
            if (material.AOMapId.HasValue) materialTextureIds.Add(material.AOMapId.Value);
            if (material.OpacityMapId.HasValue) materialTextureIds.Add(material.OpacityMapId.Value);

            // Фильтруем текстуры
            var filtered = Textures.Where(t => materialTextureIds.Contains(t.ID)).ToList();
            
            FilteredTextures.Clear();
            foreach (var texture in filtered) {
                FilteredTextures.Add(texture);
            }

            logger.Info($"Filtered {FilteredTextures.Count} textures for material {material.Name}");
        }

        /// <summary>
        /// Обновляет фильтрацию текстур при изменении выбранного материала
        /// </summary>
        partial void OnSelectedMaterialChanged(MaterialResource? value) {
            FilterTexturesForMaterial(value);
        }
    }
}

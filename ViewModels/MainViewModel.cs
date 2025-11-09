using AssetProcessor.Exceptions;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// Главная модель-представление приложения.
    /// </summary>
    public partial class MainViewModel : ObservableObject {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IPlayCanvasService playCanvasService;
        private readonly AppSettings appSettings;

        public MainViewModel(IPlayCanvasService playCanvasService, AppSettings? appSettings = null) {
            this.playCanvasService = playCanvasService;
            this.appSettings = appSettings ?? AppSettings.Default;

            Username = this.appSettings.UserName;
            ApiKey = this.appSettings.PlaycanvasApiKey;

            logger.Info("MainViewModel initialized");
        }

        [ObservableProperty]
        private ObservableCollection<TextureResource> textures = new();

        [ObservableProperty]
        private ObservableCollection<ModelResource> models = new();

        [ObservableProperty]
        private ObservableCollection<MaterialResource> materials = new();

        [ObservableProperty]
        private ObservableCollection<BaseResource> assets = new();

        [ObservableProperty]
        private ObservableCollection<TextureResource> filteredTextures = new();

        [ObservableProperty]
        private ObservableCollection<KeyValuePair<string, string>> projects = new();

        [ObservableProperty]
        private ObservableCollection<Branch> branches = new();

        [ObservableProperty]
        private string? selectedProjectId;

        [ObservableProperty]
        private string? selectedBranchId;

        [ObservableProperty]
        private MaterialResource? selectedMaterial;

        [ObservableProperty]
        private bool isDownloadButtonEnabled;

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private ConnectionState connectionState = ConnectionState.Disconnected;

        [ObservableProperty]
        private string connectionStatusMessage = "Disconnected";

        [ObservableProperty]
        private Brush connectionStatusBrush = Brushes.Red;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        private string? username;

        [ObservableProperty]
        private string? apiKey;

        public string ConnectionButtonContent => ConnectionState switch {
            ConnectionState.Disconnected => "Connect",
            ConnectionState.UpToDate => "Refresh",
            ConnectionState.NeedsDownload => "Download",
            _ => "Connect"
        };

        public string ConnectionButtonToolTip => ConnectionState switch {
            ConnectionState.Disconnected => "Connect to PlayCanvas and load projects",
            ConnectionState.UpToDate => "Check for updates from PlayCanvas server",
            ConnectionState.NeedsDownload => "Download assets from PlayCanvas",
            _ => string.Empty
        };

        [RelayCommand(CanExecute = nameof(CanExecutePrimaryAction))]
        private async Task PrimaryActionAsync(CancellationToken cancellationToken = default) {
            switch (ConnectionState) {
                case ConnectionState.Disconnected:
                    await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case ConnectionState.UpToDate:
                    await LoadAssetsAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case ConnectionState.NeedsDownload:
                    await DownloadAssetsAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private bool CanExecutePrimaryAction() {
            if (IsBusy) {
                return false;
            }

            return ConnectionState switch {
                ConnectionState.Disconnected => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(ApiKey),
                _ => !string.IsNullOrWhiteSpace(SelectedProjectId) && !string.IsNullOrWhiteSpace(SelectedBranchId)
            };
        }

        partial void OnIsBusyChanged(bool value) {
            PrimaryActionCommand.NotifyCanExecuteChanged();
        }

        partial void OnConnectionStateChanged(ConnectionState value) {
            OnPropertyChanged(nameof(ConnectionButtonContent));
            OnPropertyChanged(nameof(ConnectionButtonToolTip));
            PrimaryActionCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedProjectIdChanged(string? value) {
            PrimaryActionCommand.NotifyCanExecuteChanged();

            Branches.Clear();
            SelectedBranchId = null;

            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            appSettings.LastSelectedProjectId = value;

            _ = LoadBranchesAsync(CancellationToken.None);
        }

        partial void OnSelectedBranchIdChanged(string? value) {
            PrimaryActionCommand.NotifyCanExecuteChanged();

            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            appSettings.LastSelectedProjectId = SelectedProjectId ?? string.Empty;
            appSettings.LastSelectedBranchId = value;

            var branchName = Branches.FirstOrDefault(b => string.Equals(b.Id, value, StringComparison.Ordinal))?.Name ?? string.Empty;
            appSettings.LastSelectedBranchName = branchName;
            SaveSettings();

            _ = LoadAssetsAsync(CancellationToken.None);
        }

        partial void OnSelectedMaterialChanged(MaterialResource? value) {
            FilterTexturesForMaterial(value);
        }

        private async Task ConnectInternalAsync(CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(ApiKey)) {
                StatusMessage = "Username and API Key are required";
                logger.Warn("Connection attempt without username or API key");
                return;
            }

            try {
                IsBusy = true;
                StatusMessage = "Connecting to PlayCanvas...";
                ConnectionStatusMessage = "Connecting...";
                ConnectionStatusBrush = Brushes.DarkOrange;

                var userId = await playCanvasService.GetUserIdAsync(Username, ApiKey, cancellationToken).ConfigureAwait(false);
                logger.Info($"Retrieved user ID: {userId}");

                var projectsDict = await playCanvasService.GetProjectsAsync(userId, ApiKey, new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);

                Projects.Clear();
                foreach (var pair in projectsDict.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase)) {
                    Projects.Add(pair);
                }

                if (Projects.Count == 0) {
                    ConnectionState = ConnectionState.Disconnected;
                    ConnectionStatusMessage = "No projects found";
                    ConnectionStatusBrush = Brushes.Red;
                    IsConnected = false;
                    StatusMessage = "No projects available for the current user.";
                    return;
                }

                IsConnected = true;
                ConnectionState = ConnectionState.UpToDate;
                ConnectionStatusMessage = $"Connected as {Username}";
                ConnectionStatusBrush = Brushes.Green;
                StatusMessage = $"Connected successfully. Found {Projects.Count} projects.";

                RestoreLastProjectSelection();
            } catch (InvalidConfigurationException ex) {
                HandleConnectionFailure($"Configuration error: {ex.Message}", ex);
            } catch (PlayCanvasApiException ex) {
                HandleConnectionFailure($"API error: {ex.Message}", ex);
            } catch (NetworkException ex) {
                HandleConnectionFailure($"Network error: {ex.Message}", ex);
            } catch (Exception ex) {
                HandleConnectionFailure($"Unexpected error: {ex.Message}", ex);
            } finally {
                IsBusy = false;
            }
        }

        private void HandleConnectionFailure(string message, Exception exception) {
            StatusMessage = message;
            ConnectionStatusMessage = message;
            ConnectionStatusBrush = Brushes.Red;
            IsConnected = false;
            ConnectionState = ConnectionState.Disconnected;
            logger.Error(exception, message);
        }

        private void RestoreLastProjectSelection() {
            if (Projects.Count == 0) {
                SelectedProjectId = null;
                return;
            }

            var lastProjectId = appSettings.LastSelectedProjectId;

            if (!string.IsNullOrWhiteSpace(lastProjectId) && Projects.Any(p => string.Equals(p.Key, lastProjectId, StringComparison.Ordinal))) {
                SelectedProjectId = lastProjectId;
            } else {
                SelectedProjectId = Projects[0].Key;
            }
        }

        private async Task LoadBranchesAsync(CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(SelectedProjectId) || string.IsNullOrWhiteSpace(ApiKey)) {
                StatusMessage = "Please select a project first";
                return;
            }

            try {
                IsBusy = true;
                StatusMessage = "Loading branches...";

                var branchesList = await playCanvasService.GetBranchesAsync(SelectedProjectId, ApiKey, new List<Branch>(), cancellationToken).ConfigureAwait(false);

                var orderedBranches = branchesList.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
                Branches.Clear();
                foreach (var branch in orderedBranches) {
                    Branches.Add(branch);
                }

                if (Branches.Count == 0) {
                    StatusMessage = "No branches available for the selected project.";
                    SelectedBranchId = null;
                    return;
                }

                RestoreLastBranchSelection();
            } catch (PlayCanvasApiException ex) {
                StatusMessage = $"Failed to load branches: {ex.Message}";
                logger.Error(ex, "Error loading branches");
            } catch (NetworkException ex) {
                StatusMessage = $"Network error: {ex.Message}";
                logger.Error(ex, "Network error loading branches");
            } finally {
                IsBusy = false;
            }
        }

        private void RestoreLastBranchSelection() {
            if (Branches.Count == 0) {
                SelectedBranchId = null;
                return;
            }

            var lastBranchId = appSettings.LastSelectedBranchId;

            if (!string.IsNullOrWhiteSpace(lastBranchId) && Branches.Any(b => string.Equals(b.Id, lastBranchId, StringComparison.Ordinal))) {
                SelectedBranchId = lastBranchId;
            } else {
                SelectedBranchId = Branches[0].Id;
            }
        }

        private async Task LoadAssetsAsync(CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(SelectedProjectId) || string.IsNullOrWhiteSpace(SelectedBranchId) || string.IsNullOrWhiteSpace(ApiKey)) {
                StatusMessage = "Please select a project and branch first";
                return;
            }

            try {
                IsBusy = true;
                StatusMessage = "Loading assets...";
                logger.Info($"Loading assets for project: {SelectedProjectId}, branch: {SelectedBranchId}");

                var assetsArray = await playCanvasService.GetAssetsAsync(SelectedProjectId, SelectedBranchId, ApiKey, cancellationToken).ConfigureAwait(false);

                Textures.Clear();
                Models.Clear();
                Materials.Clear();
                Assets.Clear();

                foreach (var asset in assetsArray) {
                    var type = asset["type"]?.ToString();
                    var id = asset["id"]?.ToString();
                    var name = asset["name"]?.ToString();

                    if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id)) {
                        continue;
                    }

                    switch (type.ToLowerInvariant()) {
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

                if (Assets.Count == 0) {
                    ConnectionState = ConnectionState.NeedsDownload;
                }
            } catch (PlayCanvasApiException ex) {
                StatusMessage = $"Failed to load assets: {ex.Message}";
                logger.Error(ex, "Error loading assets");
            } catch (NetworkException ex) {
                StatusMessage = $"Network error: {ex.Message}";
                logger.Error(ex, "Network error loading assets");
            } finally {
                IsBusy = false;
            }
        }

        private async Task DownloadAssetsAsync(CancellationToken cancellationToken) {
            if (!IsDownloadButtonEnabled) {
                StatusMessage = "No assets to download";
                return;
            }

            try {
                IsBusy = true;
                StatusMessage = $"Downloading {Assets.Count} assets...";
                logger.Info($"Starting download of {Assets.Count} assets");

                // TODO: Реализовать логику скачивания ассетов.
                await Task.CompletedTask;

                ConnectionState = ConnectionState.UpToDate;
                StatusMessage = "Download completed.";
            } finally {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Фильтрует текстуры на основе выбранного материала.
        /// </summary>
        private void FilterTexturesForMaterial(MaterialResource? material) {
            if (material == null) {
                FilteredTextures.Clear();
                return;
            }

            var materialTextureIds = new List<int>();

            if (material.DiffuseMapId.HasValue) materialTextureIds.Add(material.DiffuseMapId.Value);
            if (material.SpecularMapId.HasValue) materialTextureIds.Add(material.SpecularMapId.Value);
            if (material.NormalMapId.HasValue) materialTextureIds.Add(material.NormalMapId.Value);
            if (material.GlossMapId.HasValue) materialTextureIds.Add(material.GlossMapId.Value);
            if (material.MetalnessMapId.HasValue) materialTextureIds.Add(material.MetalnessMapId.Value);
            if (material.EmissiveMapId.HasValue) materialTextureIds.Add(material.EmissiveMapId.Value);
            if (material.AOMapId.HasValue) materialTextureIds.Add(material.AOMapId.Value);
            if (material.OpacityMapId.HasValue) materialTextureIds.Add(material.OpacityMapId.Value);

            var filtered = Textures.Where(t => materialTextureIds.Contains(t.ID)).ToList();

            FilteredTextures.Clear();
            foreach (var texture in filtered) {
                FilteredTextures.Add(texture);
            }

            logger.Info($"Filtered {FilteredTextures.Count} textures for material {material.Name}");
        }

        private void SaveSettings() {
            try {
                appSettings.Save();
            } catch (ConfigurationErrorsException ex) {
                logger.Warn(ex, "Failed to persist application settings");
            }
        }
    }
}

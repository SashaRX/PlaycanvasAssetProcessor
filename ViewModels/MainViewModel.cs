using AssetProcessor.Exceptions;
using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// Main ViewModel for the application's primary window
    /// </summary>
    public partial class MainViewModel : ObservableObject {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IPlayCanvasService playCanvasService;
        private readonly ITextureProcessingService textureProcessingService;
        private readonly ILocalCacheService localCacheService;
        private readonly IProjectSyncService projectSyncService;
        private readonly IAssetDownloadCoordinator assetDownloadCoordinator;
        private readonly SynchronizationContext? synchronizationContext;

        public event EventHandler<TextureProcessingCompletedEventArgs>? TextureProcessingCompleted;

        public event EventHandler<TexturePreviewLoadedEventArgs>? TexturePreviewLoaded;

        public ITextureConversionSettingsProvider? ConversionSettingsProvider { get; set; }

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
        private double progressValue;

        [ObservableProperty]
        private double progressMaximum;

        [ObservableProperty]
        private string? progressText;

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

        [ObservableProperty]
        private bool processAllTextures;

        [ObservableProperty]
        private bool isTextureProcessing;

        [ObservableProperty]
        private TextureResource? selectedTexture;

        [ObservableProperty]
        private IReadOnlyDictionary<int, string> folderPaths = new Dictionary<int, string>();

        [ObservableProperty]
        private string? currentProjectName;

        public MainViewModel(
            IPlayCanvasService playCanvasService,
            ITextureProcessingService textureProcessingService,
            ILocalCacheService localCacheService,
            IProjectSyncService projectSyncService,
            IAssetDownloadCoordinator assetDownloadCoordinator) {
            this.playCanvasService = playCanvasService;
            this.textureProcessingService = textureProcessingService;
            this.localCacheService = localCacheService;
            this.projectSyncService = projectSyncService;
            this.assetDownloadCoordinator = assetDownloadCoordinator;
            synchronizationContext = SynchronizationContext.Current;

            logger.Info("MainViewModel initialized");
        }

        private void UpdateResourceStatusMessage(BaseResource resource) {
            if (resource == null) {
                return;
            }

            void UpdateStatus() => StatusMessage = $"{resource.Name}: {resource.Status ?? "Pending"}";
            PostToUi(UpdateStatus);
        }

        private void UpdateProgress(AssetDownloadProgress progress) {
            void UpdateValues() {
                ProgressMaximum = progress.Total;
                ProgressValue = progress.Completed;
                ProgressText = progress.Total > 0 ? $"{progress.Completed}/{progress.Total}" : null;
            }

            PostToUi(UpdateValues);
        }

        private void PostToUi(Action updateAction) {
            if (updateAction == null) {
                return;
            }

            if (synchronizationContext != null) {
                synchronizationContext.Post(_ => updateAction(), null);
            } else {
                updateAction();
            }
        }

        private string? ResolveApiKey() {
            if (!string.IsNullOrWhiteSpace(ApiKey)) {
                return ApiKey;
            }

            try {
                return AppSettings.Default.GetDecryptedPlaycanvasApiKey();
            } catch (CryptographicException ex) {
                logger.Error(ex, "Failed to decrypt Playcanvas API key from settings.");
                StatusMessage = "Security error: check master password.";
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
                Projects = new ObservableCollection<KeyValuePair<string, string>>(projectsDict);
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
                Branches = new ObservableCollection<Branch>(branchesList);
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
        private async Task SyncProjectAsync(CancellationToken cancellationToken = default) {
            string? resolvedApiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(SelectedProjectId) || string.IsNullOrEmpty(SelectedBranchId) || string.IsNullOrEmpty(resolvedApiKey)) {
                StatusMessage = "Please select a project and branch first";
                return;
            }

            KeyValuePair<string, string> selectedProject = Projects.FirstOrDefault(p => p.Key == SelectedProjectId);
            if (selectedProject.Equals(default(KeyValuePair<string, string>))) {
                StatusMessage = "Project not found";
                return;
            }

            string projectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
            CurrentProjectName = projectName;

            try {
                StatusMessage = "Synchronizing project assets...";
                ProgressText = null;
                ProgressValue = 0;
                ProgressMaximum = 0;

                ProjectSyncRequest request = new(
                    SelectedProjectId,
                    SelectedBranchId,
                    resolvedApiKey,
                    projectName,
                    AppSettings.Default.ProjectsFolderPath);

                Progress<ProjectSyncProgress> syncProgress = new(progress => {
                    ProgressMaximum = Math.Max(ProgressMaximum, Math.Max(progress.Total, progress.Processed));
                    ProgressValue = progress.Processed;
                    ProgressText = progress.Total > 0
                        ? $"{progress.Processed}/{progress.Total}"
                        : $"{progress.Processed}";
                });

                ProjectSyncResult result = await projectSyncService.SyncProjectAsync(request, syncProgress, cancellationToken);

                FolderPaths = result.FolderPaths;
                PopulateResources(result);

                ProgressMaximum = Assets.Count;
                ProgressValue = Assets.Count;
                ProgressText = $"{Assets.Count}/{Assets.Count}";

                StatusMessage = $"Loaded {Assets.Count} assets ({Textures.Count} textures, {Models.Count} models, {Materials.Count} materials).";
                logger.Info($"Project synchronized: {Assets.Count} assets loaded");
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

            string? resolvedApiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(resolvedApiKey)) {
                StatusMessage = "API Key is required for downloading";
                logger.Warn("Download attempt without API key");
                return;
            }

            if (string.IsNullOrEmpty(SelectedProjectId)) {
                StatusMessage = "Please select a project first";
                return;
            }

            KeyValuePair<string, string> selectedProject = Projects.FirstOrDefault(p => p.Key == SelectedProjectId);
            if (selectedProject.Equals(default(KeyValuePair<string, string>))) {
                StatusMessage = "Project not found";
                return;
            }

            string effectiveProjectName = CurrentProjectName ?? MainWindowHelpers.CleanProjectName(selectedProject.Value);
            CurrentProjectName = effectiveProjectName;

            try {
                AssetDownloadContext context = new(
                    Assets,
                    resolvedApiKey,
                    effectiveProjectName,
                    AppSettings.Default.ProjectsFolderPath,
                    FolderPaths);

                ResetProgress();
                StatusMessage = "Preparing downloads...";

                AssetDownloadOptions downloadOptions = new(
                    progress => UpdateProgress(progress),
                    resource => UpdateResourceStatusMessage(resource));

                AssetDownloadResult result = await assetDownloadCoordinator.DownloadAssetsAsync(context, downloadOptions, cancellationToken);

                ApplyBatchResultProgress(result.BatchResult);

                StatusMessage = result.Message;
                if (result.IsSuccess) {
                    logger.Info(result.Message);
                } else {
                    logger.Warn(result.Message);
                }
            } catch (OperationCanceledException) {
                StatusMessage = "Download cancelled";
                logger.Warn("Download operation cancelled by user");
                ResetProgress(); // Reset progress on cancellation
            } catch (Exception ex) {
                // Handle any other exceptions to prevent unhandled exceptions and UI inconsistency
                StatusMessage = $"Download error: {ex.Message}";
                logger.Error(ex, "Error during download operation");
                ResetProgress(); // Reset progress on error to restore UI to consistent state
            }
        }

        private void ResetProgress() {
            ProgressValue = 0;
            ProgressMaximum = 0;
            ProgressText = null;
        }

        private void ApplyBatchResultProgress(ResourceDownloadBatchResult batchResult) {
            if (batchResult.Total == 0) {
                ResetProgress();
                return;
            }

            ProgressMaximum = batchResult.Total;
            ProgressValue = batchResult.Succeeded;
            ProgressText = $"{batchResult.Succeeded}/{batchResult.Total}";
        }

        private void PopulateResources(ProjectSyncResult result) {
            ArgumentNullException.ThrowIfNull(result);

            Textures.Clear();
            Models.Clear();
            Materials.Clear();
            Assets.Clear();

            Uri baseUri = new("https://playcanvas.com");

            foreach (PlayCanvasAssetSummary asset in result.Assets) {
                if (string.IsNullOrEmpty(asset.Type) || asset.Id == 0) {
                    continue;
                }

                string sanitizedName = localCacheService.SanitizePath(asset.Name);
                string fileName = localCacheService.SanitizePath(asset.File?.Filename ?? sanitizedName ?? $"asset_{asset.Id}");

                if (string.Equals(asset.Type, "material", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                    fileName = string.IsNullOrEmpty(fileName) ? $"material_{asset.Id}.json" : $"{fileName}.json";
                }

                string resourcePath = localCacheService.GetResourcePath(
                    AppSettings.Default.ProjectsFolderPath,
                    result.ProjectName,
                    result.FolderPaths,
                    fileName,
                    asset.Parent);

                string? url = asset.File?.Url;
                if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Relative, out Uri? relative)) {
                    url = new Uri(baseUri, relative).ToString();
                }

                switch (asset.Type.ToLowerInvariant()) {
                    case "texture":
                        TextureResource texture = new() {
                            ID = asset.Id,
                            Name = sanitizedName,
                            Type = asset.Type,
                            Status = "On Server",
                            Url = url,
                            Path = resourcePath,
                            Size = (int)(asset.File?.Size ?? 0),
                            Hash = asset.File?.Hash,
                            Parent = asset.Parent,
                            Extension = Path.GetExtension(resourcePath)
                        };
                        Textures.Add(texture);
                        Assets.Add(texture);
                        break;

                    case "model":
                        ModelResource model = new() {
                            ID = asset.Id,
                            Name = sanitizedName,
                            Type = asset.Type,
                            Status = "On Server",
                            Url = url,
                            Path = resourcePath,
                            Size = (int)(asset.File?.Size ?? 0),
                            Hash = asset.File?.Hash,
                            Parent = asset.Parent,
                            Extension = Path.GetExtension(resourcePath)
                        };
                        Models.Add(model);
                        Assets.Add(model);
                        break;

                    case "material":
                        MaterialResource material = new() {
                            ID = asset.Id,
                            Name = sanitizedName,
                            Type = asset.Type,
                            Status = "On Server",
                            Path = resourcePath,
                            Parent = asset.Parent
                        };
                        Materials.Add(material);
                        Assets.Add(material);
                        break;
                }
            }

            IsDownloadButtonEnabled = Assets.Count > 0;
        }

        /// <summary>
        /// Filters textures based on selected material
        /// </summary>
        private void FilterTexturesForMaterial(MaterialResource? material) {
            if (material == null) {
                FilteredTextures.Clear();
                return;
            }

            var materialTextureIds = new List<int>();

            // Collect all texture IDs used in the material
            if (material.DiffuseMapId.HasValue) materialTextureIds.Add(material.DiffuseMapId.Value);
            if (material.SpecularMapId.HasValue) materialTextureIds.Add(material.SpecularMapId.Value);
            if (material.NormalMapId.HasValue) materialTextureIds.Add(material.NormalMapId.Value);
            if (material.GlossMapId.HasValue) materialTextureIds.Add(material.GlossMapId.Value);
            if (material.MetalnessMapId.HasValue) materialTextureIds.Add(material.MetalnessMapId.Value);
            if (material.EmissiveMapId.HasValue) materialTextureIds.Add(material.EmissiveMapId.Value);
            if (material.AOMapId.HasValue) materialTextureIds.Add(material.AOMapId.Value);
            if (material.OpacityMapId.HasValue) materialTextureIds.Add(material.OpacityMapId.Value);

            // Filter textures
            var filtered = Textures.Where(t => materialTextureIds.Contains(t.ID)).ToList();
            
            FilteredTextures.Clear();
            foreach (var texture in filtered) {
                FilteredTextures.Add(texture);
            }

            logger.Info($"Filtered {FilteredTextures.Count} textures for material {material.Name}");
        }

        /// <summary>
        /// Updates texture filtering when selected material changes
        /// </summary>
        partial void OnSelectedMaterialChanged(MaterialResource? value) {
            FilterTexturesForMaterial(value);
        }

        partial void OnIsTextureProcessingChanged(bool value) {
            ProcessTexturesCommand.NotifyCanExecuteChanged();
        }

        partial void OnProcessAllTexturesChanged(bool value) {
            ProcessTexturesCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanProcessTextures))]
        private async Task ProcessTexturesAsync(IList? selectedItems) {
            if (IsTextureProcessing) {
                return;
            }

            if (ConversionSettingsProvider == null) {
                StatusMessage = "Conversion settings panel is not available.";
                return;
            }

            var texturesToProcess = ProcessAllTextures
                ? Textures.Where(t => !string.IsNullOrEmpty(t.Path)).ToList()
                : selectedItems?.OfType<TextureResource>().Where(t => !string.IsNullOrEmpty(t.Path)).ToList()
                    ?? new List<TextureResource>();

            if (texturesToProcess.Count == 0) {
                StatusMessage = "No textures to process.";
                return;
            }

            try {
                IsTextureProcessing = true;
                StatusMessage = $"Converting {texturesToProcess.Count} textures...";

                var result = await textureProcessingService.ProcessTexturesAsync(new TextureProcessingRequest {
                    Textures = texturesToProcess,
                    SettingsProvider = ConversionSettingsProvider,
                    SelectedTexture = SelectedTexture
                }, CancellationToken.None);

                StatusMessage = $"Conversion completed. Success: {result.SuccessCount}, errors: {result.ErrorCount}.";
                TextureProcessingCompleted?.Invoke(this, new TextureProcessingCompletedEventArgs(result));
            } catch (OperationCanceledException) {
                StatusMessage = "Conversion cancelled.";
            } catch (Exception ex) {
                StatusMessage = $"Conversion error: {ex.Message}";
                logger.Error(ex, "Error processing textures");
            } finally {
                IsTextureProcessing = false;
            }
        }

        private bool CanProcessTextures(IList? selectedItems) {
            if (IsTextureProcessing) {
                return false;
            }

            if (ProcessAllTextures) {
                return Textures.Any(t => !string.IsNullOrEmpty(t.Path));
            }

            if (selectedItems == null) {
                return false;
            }

            return selectedItems.OfType<TextureResource>().Any(t => !string.IsNullOrEmpty(t.Path));
        }

        [RelayCommand]
        private void AutoDetectPresets(IList? selectedItems) {
            if (ConversionSettingsProvider == null) {
                StatusMessage = "Conversion settings panel is not available.";
                return;
            }

            var texturesToProcess = ProcessAllTextures
                ? Textures.ToList()
                : selectedItems?.OfType<TextureResource>().ToList() ?? new List<TextureResource>();

            if (texturesToProcess.Count == 0) {
                StatusMessage = "No textures for auto-detection.";
                return;
            }

            var result = textureProcessingService.AutoDetectPresets(texturesToProcess, ConversionSettingsProvider);
            StatusMessage = $"Auto-detect: found {result.MatchedCount}, not found {result.NotMatchedCount}.";
        }

        [RelayCommand]
        private async Task LoadKtxPreviewAsync(TextureResource? texture) {
            if (texture == null) {
                return;
            }

            try {
                var preview = await textureProcessingService.LoadKtxPreviewAsync(texture, CancellationToken.None);
                if (preview != null) {
                    TexturePreviewLoaded?.Invoke(this, new TexturePreviewLoadedEventArgs(texture, preview));
                }
            } catch (Exception ex) {
                logger.Warn(ex, "Failed to load KTX2 preview");
            }
        }
    }

    public sealed class TextureProcessingCompletedEventArgs : EventArgs {
        public TextureProcessingCompletedEventArgs(TextureProcessingResult result) {
            Result = result;
        }

        public TextureProcessingResult Result { get; }
    }

    public sealed class TexturePreviewLoadedEventArgs : EventArgs {
        public TexturePreviewLoadedEventArgs(TextureResource texture, TexturePreviewResult preview) {
            Texture = texture;
            Preview = preview;
        }

        public TextureResource Texture { get; }

        public TexturePreviewResult Preview { get; }
    }
}

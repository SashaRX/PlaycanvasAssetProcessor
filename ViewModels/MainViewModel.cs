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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// Main ViewModel for the application's primary window
    /// </summary>
    public partial class MainViewModel : ObservableObject {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private const int ProgressUpdateIntervalMs = 100; // Throttle UI updates to every 100ms
        private readonly IPlayCanvasService playCanvasService;
        private readonly ITextureProcessingService textureProcessingService;
        private readonly ILocalCacheService localCacheService;
        private readonly IProjectSyncService projectSyncService;
        private readonly IAssetDownloadCoordinator assetDownloadCoordinator;
        private readonly SynchronizationContext? synchronizationContext;
        private readonly IProjectSelectionService projectSelectionService;
        private readonly IPlayCanvasCredentialsService credentialsService;
        private readonly TextureSelectionViewModel textureSelectionViewModel;
        private readonly ORMTextureViewModel ormTextureViewModel;
        private readonly TextureConversionSettingsViewModel conversionSettingsViewModel;
        private readonly AssetLoadingViewModel assetLoadingViewModel;
        private readonly MaterialSelectionViewModel materialSelectionViewModel;
        private readonly MasterMaterialsViewModel masterMaterialsViewModel;
        private long lastProgressUpdateTicks;
        private AssetDownloadProgress? pendingProgress;
        private BaseResource? pendingResource;

        public event EventHandler<TextureProcessingCompletedEventArgs>? TextureProcessingCompleted;

        public event EventHandler<TexturePreviewLoadedEventArgs>? TexturePreviewLoaded;

        public event EventHandler<ProjectSelectionChangedEventArgs>? ProjectSelectionChanged;

        public event EventHandler<BranchSelectionChangedEventArgs>? BranchSelectionChanged;

        public ITextureConversionSettingsProvider? ConversionSettingsProvider { get; set; }

        /// <summary>
        /// ViewModel for texture selection logic (debouncing, cancellation, state)
        /// </summary>
        public TextureSelectionViewModel TextureSelection => textureSelectionViewModel;

        /// <summary>
        /// ViewModel for ORM texture creation and management
        /// </summary>
        public ORMTextureViewModel ORMTexture => ormTextureViewModel;

        /// <summary>
        /// ViewModel for texture conversion settings management
        /// </summary>
        public TextureConversionSettingsViewModel ConversionSettings => conversionSettingsViewModel;

        /// <summary>
        /// ViewModel for asset loading orchestration
        /// </summary>
        public AssetLoadingViewModel AssetLoading => assetLoadingViewModel;

        /// <summary>
        /// ViewModel for material selection and texture navigation
        /// </summary>
        public MaterialSelectionViewModel MaterialSelection => materialSelectionViewModel;

        /// <summary>
        /// ViewModel for Master Materials and Shader Chunks management
        /// </summary>
        public MasterMaterialsViewModel MasterMaterialsViewModel => masterMaterialsViewModel;

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
            IAssetDownloadCoordinator assetDownloadCoordinator,
            IProjectSelectionService projectSelectionService,
            IPlayCanvasCredentialsService credentialsService,
            TextureSelectionViewModel textureSelectionViewModel,
            ORMTextureViewModel ormTextureViewModel,
            TextureConversionSettingsViewModel conversionSettingsViewModel,
            AssetLoadingViewModel assetLoadingViewModel,
            MaterialSelectionViewModel materialSelectionViewModel,
            MasterMaterialsViewModel masterMaterialsViewModel) {
            this.playCanvasService = playCanvasService;
            this.textureProcessingService = textureProcessingService;
            this.localCacheService = localCacheService;
            this.projectSyncService = projectSyncService;
            this.assetDownloadCoordinator = assetDownloadCoordinator;
            this.projectSelectionService = projectSelectionService;
            this.credentialsService = credentialsService;
            this.textureSelectionViewModel = textureSelectionViewModel;
            this.ormTextureViewModel = ormTextureViewModel;
            this.conversionSettingsViewModel = conversionSettingsViewModel;
            this.assetLoadingViewModel = assetLoadingViewModel;
            this.materialSelectionViewModel = materialSelectionViewModel;
            this.masterMaterialsViewModel = masterMaterialsViewModel;
            synchronizationContext = SynchronizationContext.Current;
            Username = credentialsService.Username;
            ApiKey = credentialsService.GetApiKeyOrNull();

            logger.Info("MainViewModel initialized");
        }

        /// <summary>
        /// Throttled callback for resource status updates. Stores latest value and only posts to UI if interval elapsed.
        /// </summary>
        private void UpdateResourceStatusMessageThrottled(BaseResource resource) {
            if (resource == null) {
                return;
            }

            pendingResource = resource;

            if (!ShouldUpdateUi()) {
                return;
            }

            PostPendingResourceStatus();
        }

        /// <summary>
        /// Throttled callback for progress updates. Stores latest value and only posts to UI if interval elapsed.
        /// </summary>
        private void UpdateProgressThrottled(AssetDownloadProgress progress) {
            pendingProgress = progress;

            if (!ShouldUpdateUi()) {
                return;
            }

            PostPendingProgress();
        }

        /// <summary>
        /// Checks if enough time has passed since last UI update.
        /// </summary>
        private bool ShouldUpdateUi() {
            long now = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref lastProgressUpdateTicks);
            long intervalTicks = TimeSpan.FromMilliseconds(ProgressUpdateIntervalMs).Ticks;

            if (now - last < intervalTicks) {
                return false;
            }

            // Try to update the last update time (atomic compare-and-swap)
            if (Interlocked.CompareExchange(ref lastProgressUpdateTicks, now, last) != last) {
                return false; // Another thread updated it
            }

            return true;
        }

        /// <summary>
        /// Posts pending progress to UI thread.
        /// </summary>
        private void PostPendingProgress() {
            AssetDownloadProgress? progress = pendingProgress;
            if (progress == null) {
                return;
            }

            PostToUi(() => {
                ProgressMaximum = progress.Total;
                ProgressValue = progress.Completed;
                ProgressText = progress.Total > 0 ? $"{progress.Completed}/{progress.Total}" : null;
            });
        }

        /// <summary>
        /// Posts pending resource status to UI thread.
        /// </summary>
        private void PostPendingResourceStatus() {
            BaseResource? resource = pendingResource;
            if (resource == null) {
                return;
            }

            PostToUi(() => StatusMessage = $"{resource.Name}: {resource.Status ?? "Pending"}");
        }

        /// <summary>
        /// Flushes any pending UI updates. Call after downloads complete.
        /// </summary>
        private void FlushPendingUpdates() {
            PostPendingProgress();
            PostPendingResourceStatus();
            pendingProgress = null;
            pendingResource = null;
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

            if (!credentialsService.TryGetApiKey(out string apiKey)) {
                StatusMessage = "Security error: check master password.";
                return null;
            }

            return apiKey;
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

                BranchSelectionResult result = await projectSelectionService.LoadBranchesAsync(
                    SelectedProjectId,
                    resolvedApiKey,
                    AppSettings.Default.LastSelectedBranchName,
                    cancellationToken);

                Branches = new ObservableCollection<Branch>(result.Branches);

                if (Branches.Count > 0) {
                    SelectedBranchId = result.SelectedBranchId ?? Branches[0].Id;
                } else {
                    SelectedBranchId = null;
                }

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
        private async Task ProjectSelectionChangedAsync(KeyValuePair<string, string>? selectedProject) {
            if (projectSelectionService.IsProjectInitializationInProgress) {
                logger.Info("Project selection ignored - initialization in progress");
                return;
            }

            if (selectedProject is null) {
                return;
            }

            SelectedProjectId = selectedProject.Value.Key;
            projectSelectionService.UpdateProjectPath(AppSettings.Default.ProjectsFolderPath, selectedProject.Value);

            // Set project context for MasterMaterials so config can be saved
            var projectFolderPath = projectSelectionService.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolderPath)) {
                await masterMaterialsViewModel.SetProjectContextAsync(projectFolderPath);
                logger.Info($"Set MasterMaterials project context: {projectFolderPath}");
            }

            try {
                await LoadBranchesAsync(CancellationToken.None);
            } catch (Exception ex) {
                logger.Error(ex, "Failed to load branches after project selection");
            }

            ProjectSelectionChanged?.Invoke(this, new ProjectSelectionChangedEventArgs(selectedProject.Value));
        }

        [RelayCommand]
        private Task BranchSelectionChangedAsync(Branch? branch) {
            if (branch == null) {
                return Task.CompletedTask;
            }

            if (projectSelectionService.IsBranchInitializationInProgress) {
                return Task.CompletedTask;
            }

            SelectedBranchId = branch.Id;
            projectSelectionService.UpdateSelectedBranch(branch);

            BranchSelectionChanged?.Invoke(this, new BranchSelectionChangedEventArgs(branch));
            return Task.CompletedTask;
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
                // Reset throttle timer so first update shows immediately
                Interlocked.Exchange(ref lastProgressUpdateTicks, 0);
                StatusMessage = "Preparing downloads...";

                // Use throttled callbacks to prevent UI freeze during rapid updates
                AssetDownloadOptions downloadOptions = new(
                    progress => UpdateProgressThrottled(progress),
                    resource => UpdateResourceStatusMessageThrottled(resource));

                AssetDownloadResult result = await assetDownloadCoordinator.DownloadAssetsAsync(context, downloadOptions, cancellationToken);

                // Flush any pending UI updates to show final state
                FlushPendingUpdates();

                ApplyBatchResultProgress(result.BatchResult);

                StatusMessage = result.Message;
                if (result.IsSuccess) {
                    logger.Info(result.Message);
                } else {
                    logger.Warn(result.Message);
                }
            } catch (OperationCanceledException) {
                FlushPendingUpdates();
                StatusMessage = "Download cancelled";
                logger.Warn("Download operation cancelled by user");
                ResetProgress(); // Reset progress on cancellation
            } catch (Exception ex) {
                FlushPendingUpdates();
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

            // Batch loading: collect all resources first, then assign collections once
            // This triggers only ONE CollectionChanged event per collection instead of N events
            var texturesList = new List<TextureResource>();
            var modelsList = new List<ModelResource>();
            var materialsList = new List<MaterialResource>();
            var assetsList = new List<BaseResource>();

            Uri baseUri = new("https://playcanvas.com");

            foreach (PlayCanvasAssetSummary asset in result.Assets) {
                if (string.IsNullOrEmpty(asset.Type) || asset.Id == 0) {
                    continue;
                }

                string sanitizedName = PathSanitizer.SanitizePath(asset.Name);
                string fileName = PathSanitizer.SanitizePath(asset.File?.Filename ?? sanitizedName ?? $"asset_{asset.Id}");

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
                        texturesList.Add(texture);
                        assetsList.Add(texture);
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
                        modelsList.Add(model);
                        assetsList.Add(model);
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
                        materialsList.Add(material);
                        assetsList.Add(material);
                        break;
                }
            }

            // Single assignment triggers only one PropertyChanged per collection
            Textures = new ObservableCollection<TextureResource>(texturesList);
            Models = new ObservableCollection<ModelResource>(modelsList);
            Materials = new ObservableCollection<MaterialResource>(materialsList);
            Assets = new ObservableCollection<BaseResource>(assetsList);

            IsDownloadButtonEnabled = Assets.Count > 0;
        }

        /// <summary>
        /// Recalculates sequential indices for all resources after filtering or sorting changes.
        /// Uses batch update mode to suppress individual PropertyChanged notifications.
        /// </summary>
        public void RecalculateIndices() {
            logger.Info($"[RecalculateIndices] Starting: {Textures.Count} textures, {Models.Count} models, {Materials.Count} materials");

            // Update indices without triggering individual PropertyChanged events
            // by using direct field access where possible
            int index = 1;
            logger.Info("[RecalculateIndices] Processing textures...");
            foreach (TextureResource texture in Textures) {
                texture.SetIndexSilent(index++);
            }
            logger.Info("[RecalculateIndices] Textures done");

            index = 1;
            logger.Info("[RecalculateIndices] Processing models...");
            foreach (ModelResource model in Models) {
                model.SetIndexSilent(index++);
            }
            logger.Info("[RecalculateIndices] Models done");

            index = 1;
            logger.Info("[RecalculateIndices] Processing materials...");
            foreach (MaterialResource material in Materials) {
                material.SetIndexSilent(index++);
            }
            logger.Info("[RecalculateIndices] Materials done, completed");
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

        /// <summary>
        /// Syncs material-to-master mappings from MasterMaterialsConfig to MaterialResource.MasterMaterialName
        /// Call this after both materials and MasterMaterialsConfig are loaded.
        /// Uses silent setter to avoid triggering PropertyChanged during batch operation.
        /// </summary>
        public void SyncMaterialMasterMappings() {
            logger.Info($"[SyncMaterialMasterMappings] Starting. Materials count: {Materials?.Count ?? 0}, Config exists: {masterMaterialsViewModel.Config != null}");

            if (Materials == null || Materials.Count == 0) {
                logger.Warn("[SyncMaterialMasterMappings] No materials to sync!");
                return;
            }

            int syncedCount = 0;
            int processedCount = 0;
            logger.Info("[SyncMaterialMasterMappings] Starting foreach loop...");

            foreach (var material in Materials) {
                processedCount++;

                // Unsubscribe first to avoid triggering save during initial sync
                material.PropertyChanged -= Material_PropertyChanged;

                // Apply EXPLICIT mapping from config to material (not default!)
                // Use silent setter to avoid triggering PropertyChanged
                var masterName = masterMaterialsViewModel.GetExplicitMasterNameForMaterial(material.ID);
                if (!string.IsNullOrEmpty(masterName)) {
                    material.SetMasterMaterialNameSilent(masterName);
                    syncedCount++;
                }

                // Now subscribe to future changes
                material.PropertyChanged += Material_PropertyChanged;
            }

            logger.Info($"[SyncMaterialMasterMappings] Loop done. Processed {processedCount}, synced {syncedCount}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG] SyncMaterialMasterMappings returning...");
        }

        /// <summary>
        /// Handles PropertyChanged on MaterialResource to update config when MasterMaterialName changes
        /// </summary>
        private void Material_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(MaterialResource.MasterMaterialName)) {
                return;
            }

            if (sender is not MaterialResource material) {
                return;
            }

            // Update the mapping in config
            masterMaterialsViewModel.SetMasterForMaterial(material.ID, material.MasterMaterialName);
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

    public sealed class ProjectSelectionChangedEventArgs : EventArgs {
        public ProjectSelectionChangedEventArgs(KeyValuePair<string, string> project) {
            SelectedProject = project;
        }

        public KeyValuePair<string, string> SelectedProject { get; }
    }

    public sealed class BranchSelectionChangedEventArgs : EventArgs {
        public BranchSelectionChangedEventArgs(Branch branch) {
            SelectedBranch = branch;
        }

        public Branch SelectedBranch { get; }
    }
}

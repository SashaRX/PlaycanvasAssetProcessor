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
        private readonly SemaphoreSlim downloadSemaphore;

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

        public MainViewModel(IPlayCanvasService playCanvasService, ITextureProcessingService textureProcessingService, ILocalCacheService localCacheService) {
            this.playCanvasService = playCanvasService;
            this.textureProcessingService = textureProcessingService;
            this.localCacheService = localCacheService;
            downloadSemaphore = new SemaphoreSlim(AppSettings.Default.DownloadSemaphoreLimit);
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

            string? resolvedApiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(resolvedApiKey)) {
                StatusMessage = "API Key is required for downloading";
                logger.Warn("Download attempt without API key");
                return;
            }

            if (string.IsNullOrEmpty(SelectedProjectId) || string.IsNullOrEmpty(SelectedBranchId)) {
                StatusMessage = "Please select a project and branch first";
                return;
            }

            // Get project name for folder structure
            var selectedProject = Projects.FirstOrDefault(p => p.Key == SelectedProjectId);
            if (selectedProject.Equals(default(KeyValuePair<string, string>))) {
                StatusMessage = "Project not found";
                return;
            }

            string projectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
            string projectFolderPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);

            try {
                StatusMessage = $"Downloading {Assets.Count} assets...";
                logger.Info($"Starting download of {Assets.Count} assets");

                // Filter assets that need downloading
                var assetsToDownload = Assets.Where(a => 
                    string.IsNullOrEmpty(a.Status) || 
                    a.Status == "Ready" || 
                    a.Status == "On Server" ||
                    a.Status == "Error"
                ).ToList();

                if (assetsToDownload.Count == 0) {
                    StatusMessage = "No assets need downloading";
                    return;
                }

                int downloadedCount = 0;
                int failedCount = 0;

                // Download assets in parallel with semaphore limit
                var downloadTasks = assetsToDownload.Select(async asset => {
                    await downloadSemaphore.WaitAsync(cancellationToken);
                    try {
                        if (asset is MaterialResource material) {
                            await DownloadMaterialAsync(material, resolvedApiKey, projectFolderPath, cancellationToken);
                        } else if (!string.IsNullOrEmpty(asset.Url)) {
                            await DownloadFileAsync(asset, resolvedApiKey, projectFolderPath, cancellationToken);
                        } else {
                            // Need to get asset details first to get URL
                            await DownloadAssetWithDetailsAsync(asset, resolvedApiKey, projectFolderPath, cancellationToken);
                        }

                        // Check status after download to determine success/failure
                        if (asset.Status == "Downloaded") {
                            Interlocked.Increment(ref downloadedCount);
                        } else {
                            // Any other status (Error, Empty File, Size Mismatch, etc.) is considered a failure
                            Interlocked.Increment(ref failedCount);
                        }
                    } catch (Exception ex) {
                        logger.Error(ex, $"Failed to download asset {asset.Name}");
                        asset.Status = "Error";
                        Interlocked.Increment(ref failedCount);
                    } finally {
                        downloadSemaphore.Release();
                    }
                });

                await Task.WhenAll(downloadTasks);

                StatusMessage = $"Downloaded {downloadedCount} assets. Failed: {failedCount}";
                logger.Info($"Download completed: {downloadedCount} succeeded, {failedCount} failed");
            } catch (Exception ex) {
                StatusMessage = $"Download error: {ex.Message}";
                logger.Error(ex, "Error during download operation");
            }
        }

        private async Task DownloadAssetWithDetailsAsync(BaseResource resource, string apiKey, string projectFolderPath, CancellationToken cancellationToken) {
            try {
                // Get asset details to obtain URL and file information
                var assetDetail = await playCanvasService.GetAssetByIdAsync(resource.ID.ToString(), apiKey, cancellationToken);
                if (assetDetail == null) {
                    resource.Status = "Error";
                    logger.Warn($"Failed to get details for asset {resource.ID}");
                    return;
                }

                // Parse JSON to extract file URL
                using JsonDocument doc = JsonDocument.Parse(assetDetail.ToJsonString());
                JsonElement root = doc.RootElement;

                // Extract file information
                if (root.TryGetProperty("file", out JsonElement fileElement)) {
                    if (fileElement.TryGetProperty("url", out JsonElement urlElement)) {
                        string? relativeUrl = urlElement.GetString();
                        if (!string.IsNullOrEmpty(relativeUrl)) {
                            resource.Url = new Uri(new Uri("https://playcanvas.com"), relativeUrl).ToString();
                        }
                    }

                    if (fileElement.TryGetProperty("hash", out JsonElement hashElement)) {
                        resource.Hash = hashElement.GetString();
                    }

                    if (fileElement.TryGetProperty("size", out JsonElement sizeElement)) {
                        if (sizeElement.ValueKind == JsonValueKind.Number && sizeElement.TryGetInt32(out int size)) {
                            resource.Size = size;
                        }
                    }
                }

                // Set file path
                if (string.IsNullOrEmpty(resource.Path)) {
                    string? fileName = resource.Name;
                    if (string.IsNullOrEmpty(fileName)) {
                        fileName = root.TryGetProperty("name", out JsonElement nameElement) 
                            ? nameElement.GetString() 
                            : $"asset_{resource.ID}";
                    }

                    // Ensure fileName is not null
                    if (string.IsNullOrEmpty(fileName)) {
                        fileName = $"asset_{resource.ID}";
                    }

                    // Determine extension from URL or type
                    string extension = "";
                    if (!string.IsNullOrEmpty(resource.Url)) {
                        extension = Path.GetExtension(new Uri(resource.Url).LocalPath);
                    }

                    if (string.IsNullOrEmpty(extension)) {
                        extension = resource.Type?.ToLower() == "texture" ? ".png" : ".fbx";
                    }

                    if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                        fileName += extension;
                    }

                    resource.Path = Path.Combine(projectFolderPath, fileName);
                    Directory.CreateDirectory(projectFolderPath);
                }

                // Download the file
                if (!string.IsNullOrEmpty(resource.Url)) {
                    await DownloadFileAsync(resource, apiKey, projectFolderPath, cancellationToken);
                } else {
                    resource.Status = "Error";
                    logger.Warn($"No URL found for asset {resource.ID}");
                }
            } catch (Exception ex) {
                resource.Status = "Error";
                logger.Error(ex, $"Error downloading asset with details {resource.ID}");
                throw;
            }
        }

        private async Task DownloadFileAsync(BaseResource resource, string apiKey, string projectFolderPath, CancellationToken cancellationToken) {
            if (string.IsNullOrEmpty(resource.Url) || string.IsNullOrEmpty(resource.Path)) {
                resource.Status = "Error";
                logger.Warn($"Missing URL or Path for resource {resource.Name}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(resource.Path) ?? projectFolderPath);

            ResourceDownloadResult result = await localCacheService.DownloadFileAsync(resource, apiKey, cancellationToken);

            if (!result.IsSuccess) {
                string message = result.ErrorMessage ?? $"Download finished with status {result.Status}";
                logger.Warn($"Download for resource {resource.Name} completed with status {result.Status} after {result.Attempts} attempts. {message}");
            } else {
                logger.Info($"File downloaded successfully: {resource.Path}");
            }
        }

        private async Task DownloadMaterialAsync(MaterialResource material, string apiKey, string projectFolderPath, CancellationToken cancellationToken) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    material.Status = "Downloading";
                    material.DownloadProgress = 0;

                    var materialDetail = await playCanvasService.GetAssetByIdAsync(material.ID.ToString(), apiKey, cancellationToken);
                    if (materialDetail == null) {
                        throw new Exception($"Failed to get material JSON for ID: {material.ID}");
                    }

                    // Set file path
                    if (string.IsNullOrEmpty(material.Path)) {
                        string directoryPath = projectFolderPath;
                        string materialPath = Path.Combine(directoryPath, $"{material.Name}.json");
                        material.Path = materialPath;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(material.Path) ?? projectFolderPath);
                    await File.WriteAllTextAsync(material.Path, materialDetail.ToJsonString(), cancellationToken);
                    
                    material.Status = "Downloaded";
                    material.DownloadProgress = 100;
                    logger.Info($"Material downloaded successfully: {material.Path}");
                    return;
                } catch (IOException ex) {
                    if (attempt == maxRetries) {
                        material.Status = "Error";
                        logger.Error(ex, $"Error downloading material after {maxRetries} attempts");
                        return;
                    }
                    logger.Warn($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying...");
                    await Task.Delay(delayMilliseconds, cancellationToken);
                } catch (Exception ex) {
                    material.Status = "Error";
                    logger.Error(ex, $"Error downloading material: {ex.Message}");
                    if (attempt == maxRetries) {
                        return;
                    }
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
            }
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
                StatusMessage = "Панель настроек конвертации недоступна.";
                return;
            }

            var texturesToProcess = ProcessAllTextures
                ? Textures.Where(t => !string.IsNullOrEmpty(t.Path)).ToList()
                : selectedItems?.OfType<TextureResource>().Where(t => !string.IsNullOrEmpty(t.Path)).ToList()
                    ?? new List<TextureResource>();

            if (texturesToProcess.Count == 0) {
                StatusMessage = "Нет текстур для обработки.";
                return;
            }

            try {
                IsTextureProcessing = true;
                StatusMessage = $"Конвертация {texturesToProcess.Count} текстур...";

                var result = await textureProcessingService.ProcessTexturesAsync(new TextureProcessingRequest {
                    Textures = texturesToProcess,
                    SettingsProvider = ConversionSettingsProvider,
                    SelectedTexture = SelectedTexture
                }, CancellationToken.None);

                StatusMessage = $"Конвертация завершена. Успехов: {result.SuccessCount}, ошибок: {result.ErrorCount}.";
                TextureProcessingCompleted?.Invoke(this, new TextureProcessingCompletedEventArgs(result));
            } catch (OperationCanceledException) {
                StatusMessage = "Конвертация отменена.";
            } catch (Exception ex) {
                StatusMessage = $"Ошибка конвертации: {ex.Message}";
                logger.Error(ex, "Ошибка при обработке текстур");
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
                StatusMessage = "Панель настроек конвертации недоступна.";
                return;
            }

            var texturesToProcess = ProcessAllTextures
                ? Textures.ToList()
                : selectedItems?.OfType<TextureResource>().ToList() ?? new List<TextureResource>();

            if (texturesToProcess.Count == 0) {
                StatusMessage = "Нет текстур для автоопределения.";
                return;
            }

            var result = textureProcessingService.AutoDetectPresets(texturesToProcess, ConversionSettingsProvider);
            StatusMessage = $"Auto-detect: найдено {result.MatchedCount}, не найдено {result.NotMatchedCount}.";
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
                logger.Warn(ex, "Не удалось загрузить превью KTX2");
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

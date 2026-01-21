using AssetProcessor.Export;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.Upload;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for orchestrating asset loading from JSON, ORM detection, and virtual texture generation.
/// Raises events for UI to handle progress updates and collection updates.
/// </summary>
public partial class AssetLoadingViewModel : ObservableObject {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly ILogService logService;
    private readonly IAssetLoadCoordinator assetLoadCoordinator;

    #region Observable Properties

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private int loadingProgress;

    [ObservableProperty]
    private int loadingTotal;

    [ObservableProperty]
    private string? loadingStatus;

    #endregion

    #region Events

    /// <summary>
    /// Raised when assets are successfully loaded from JSON.
    /// UI should update collections with the loaded assets.
    /// </summary>
    public event EventHandler<AssetsLoadedEventArgs>? AssetsLoaded;

    /// <summary>
    /// Raised when loading progress changes.
    /// </summary>
    public event EventHandler<AssetLoadProgressEventArgs>? LoadingProgressChanged;

    /// <summary>
    /// Raised when ORM textures are detected and loaded.
    /// </summary>
    public event EventHandler<ORMTexturesDetectedEventArgs>? ORMTexturesDetected;

    /// <summary>
    /// Raised when virtual ORM textures are generated.
    /// </summary>
    public event EventHandler<VirtualORMTexturesGeneratedEventArgs>? VirtualORMTexturesGenerated;

    /// <summary>
    /// Raised when upload states are restored.
    /// </summary>
    public event EventHandler<UploadStatesRestoredEventArgs>? UploadStatesRestored;

    /// <summary>
    /// Raised when an error occurs during loading.
    /// </summary>
    public event EventHandler<AssetLoadErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Raised when B2 verification completes (background check).
    /// </summary>
    public event EventHandler<B2VerificationCompletedEventArgs>? B2VerificationCompleted;

    #endregion

    public AssetLoadingViewModel(ILogService logService, IAssetLoadCoordinator assetLoadCoordinator) {
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
        this.assetLoadCoordinator = assetLoadCoordinator ?? throw new ArgumentNullException(nameof(assetLoadCoordinator));
    }

    /// <summary>
    /// Loads assets from JSON file and performs post-processing.
    /// </summary>
    [RelayCommand]
    private async Task LoadAssetsAsync(AssetLoadRequest request, CancellationToken ct = default) {
        logger.Info("[LoadAssetsAsync] >>> ENTRY (should release UI thread immediately after Task.Run)");

        if (string.IsNullOrEmpty(request?.ProjectFolderPath) || string.IsNullOrEmpty(request.ProjectName)) {
            ErrorOccurred?.Invoke(this, new AssetLoadErrorEventArgs("Invalid Request", "Project folder path or name is empty"));
            return;
        }

        IsLoading = true;
        LoadingStatus = "Loading assets...";
        LoadingProgress = 0;
        LoadingTotal = 0;

        try {
            // DIAGNOSTIC: Removed Progress<T> completely to test if it causes the freeze
            // Progress<T> marshals callbacks to UI thread via SynchronizationContext
            // which might be flooding UI message queue even with throttling

            logger.Info("[LoadAssetsAsync] Before Task.Run - UI thread should be free after this await");
            // Run loading on background thread to not block UI
            var result = await Task.Run(async () => {
                logger.Info("[LoadAssetsAsync] Inside Task.Run - now on ThreadPool");
                return await assetLoadCoordinator.LoadAssetsFromJsonAsync(
                    request.ProjectFolderPath,
                    request.ProjectName,
                    request.ProjectsBasePath,
                    request.ProjectId,
                    null,  // No progress - testing if this fixes freeze
                    ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            logger.Info("[LoadAssetsAsync] After Task.Run - loading complete");

            if (!result.Success) {
                logService.LogError($"Failed to load assets: {result.Error}");
                ErrorOccurred?.Invoke(this, new AssetLoadErrorEventArgs("Load Failed", result.Error ?? "Unknown error"));
                return;
            }

            logService.LogInfo($"Loaded {result.Textures.Count} textures, {result.Models.Count} models, {result.Materials.Count} materials");

            // Process ORM textures BEFORE sending to UI (so SubGroupName is already set)
            LoadingStatus = "Detecting ORM textures...";
            var ormResult = await Task.Run(() =>
                DetectORMTextures(request.ProjectFolderPath, result.Textures, request.ProjectId), ct).ConfigureAwait(false);

            // Apply ORM associations to textures (sets SubGroupName)
            foreach (var (texture, subGroupName, orm) in ormResult.Associations) {
                texture.SubGroupName = subGroupName;
                texture.ParentORMTexture = orm;
            }

            LoadingStatus = "Generating virtual ORM textures...";
            var virtualOrmResult = await Task.Run(() =>
                GenerateVirtualORMTextures(result.Textures, request.ProjectId), ct).ConfigureAwait(false);

            // Apply virtual ORM associations to textures (sets SubGroupName)
            foreach (var (texture, subGroupName, orm) in virtualOrmResult.Associations) {
                texture.SubGroupName = subGroupName;
                texture.ParentORMTexture = orm;
            }

            // Restore sources for virtual ORMs
            var texturesList = result.Textures.ToList();
            foreach (var orm in virtualOrmResult.GeneratedORMs) {
                orm.RestoreSources(texturesList);
            }

            LoadingStatus = "Restoring upload states...";
            var uploadResult = await Task.Run(async () =>
                await RestoreUploadStatesAsync(result.Textures, result.Models, result.Materials, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

            // Apply upload states for textures
            foreach (var (texture, status, hash, url, uploadedAt) in uploadResult.RestoredTextures) {
                texture.UploadStatus = status;
                texture.UploadedHash = hash;
                texture.RemoteUrl = url;
                texture.LastUploadedAt = uploadedAt;
            }

            // Apply upload states for models
            foreach (var (model, status, hash, url, uploadedAt) in uploadResult.RestoredModels) {
                model.UploadStatus = status;
                model.UploadedHash = hash;
                model.RemoteUrl = url;
                model.LastUploadedAt = uploadedAt;
            }

            // Apply upload states for materials
            foreach (var (material, status, hash, url, uploadedAt) in uploadResult.RestoredMaterials) {
                material.UploadStatus = status;
                material.UploadedHash = hash;
                material.RemoteUrl = url;
                material.LastUploadedAt = uploadedAt;
            }

            // Scan server folder for already exported files
            LoadingStatus = "Scanning exported files...";
            await Task.Run(() => {
                ScanExportedFiles(request.ProjectFolderPath, request.ProjectName, result.Textures, result.Models, result.Materials);
            }, ct).ConfigureAwait(false);

            // NOW send to UI with all data already prepared (SubGroupName set, upload states restored)
            LoadingStatus = "Updating UI...";
            AssetsLoaded?.Invoke(this, new AssetsLoadedEventArgs(
                result.Textures,
                result.Models,
                result.Materials,
                result.FolderPaths
            ));

            // Fire additional events for any listeners that need ORM info
            if (ormResult.DetectedCount > 0) {
                ORMTexturesDetected?.Invoke(this, ormResult);
            }
            if (virtualOrmResult.GeneratedCount > 0) {
                VirtualORMTexturesGenerated?.Invoke(this, virtualOrmResult);
            }
            if (uploadResult.RestoredCount > 0) {
                UploadStatesRestored?.Invoke(this, uploadResult);
            }

            LoadingStatus = "Loading complete";
            logService.LogInfo("=== AssetLoadingViewModel.LoadAssetsAsync COMPLETED ===");

            // Start background B2 verification (fire and forget)
            _ = VerifyB2StatusInBackgroundAsync(result.Textures, result.Models, result.Materials, request.ProjectName);

        } catch (OperationCanceledException) {
            logService.LogInfo("Asset loading cancelled");
            LoadingStatus = "Cancelled";
        } catch (Exception ex) {
            logService.LogError($"Error loading assets: {ex.Message}");
            ErrorOccurred?.Invoke(this, new AssetLoadErrorEventArgs("Error", ex.Message));
        } finally {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Detects and loads local ORM textures (_og.ktx2, _ogm.ktx2, _ogmh.ktx2).
    /// </summary>
    private ORMTexturesDetectedEventArgs DetectORMTextures(
        string projectFolderPath,
        IReadOnlyList<TextureResource> textures,
        int projectId) {

        var detectedOrms = new List<ORMTextureResource>();
        var associations = new List<(TextureResource texture, string subGroupName, ORMTextureResource orm)>();

        if (string.IsNullOrEmpty(projectFolderPath) || !Directory.Exists(projectFolderPath)) {
            return new ORMTexturesDetectedEventArgs(detectedOrms, associations);
        }

        try {
            var ktx2Files = Directory.GetFiles(projectFolderPath, "*.ktx2", SearchOption.AllDirectories);

            foreach (var ktx2Path in ktx2Files) {
                string fileName = Path.GetFileNameWithoutExtension(ktx2Path);

                ChannelPackingMode? packingMode = null;
                string baseName = fileName;

                if (fileName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) {
                    packingMode = ChannelPackingMode.OG;
                    baseName = fileName.Substring(0, fileName.Length - 3);
                } else if (fileName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) {
                    packingMode = ChannelPackingMode.OGM;
                    baseName = fileName.Substring(0, fileName.Length - 4);
                } else if (fileName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) {
                    packingMode = ChannelPackingMode.OGMH;
                    baseName = fileName.Substring(0, fileName.Length - 5);
                }

                if (!packingMode.HasValue) continue;

                string directory = Path.GetDirectoryName(ktx2Path) ?? "";
                TextureResource? aoTexture = FindTextureByPattern(textures, directory, baseName + "_ao");
                TextureResource? glossTexture = FindTextureByPattern(textures, directory, baseName + "_gloss");
                TextureResource? metallicTexture = FindTextureByPattern(textures, directory, baseName + "_metallic")
                                                ?? FindTextureByPattern(textures, directory, baseName + "_metalness")
                                                ?? FindTextureByPattern(textures, directory, baseName + "_Metalness")
                                                ?? FindTextureByPattern(textures, directory, baseName + "_metalic");

                var ormTexture = new ORMTextureResource {
                    Name = fileName,
                    GroupName = baseName,
                    SubGroupName = fileName,
                    Path = ktx2Path,
                    PackingMode = packingMode.Value,
                    AOSource = aoTexture,
                    GlossSource = glossTexture,
                    MetallicSource = metallicTexture,
                    Status = "Converted",
                    Extension = ".ktx2",
                    ProjectId = projectId,
                    SettingsKey = $"orm_{baseName}_{fileName}"
                };

                ormTexture.LoadSettings();

                if (aoTexture != null) associations.Add((aoTexture, fileName, ormTexture));
                if (glossTexture != null) associations.Add((glossTexture, fileName, ormTexture));
                if (metallicTexture != null) associations.Add((metallicTexture, fileName, ormTexture));

                // Read KTX2 metadata
                if (File.Exists(ktx2Path)) {
                    var fileInfo = new FileInfo(ktx2Path);
                    ormTexture.CompressedSize = fileInfo.Length;
                    ormTexture.Size = (int)fileInfo.Length;

                    try {
                        var ktxInfo = GetKtx2InfoSync(ktx2Path);
                        if (ktxInfo.Width > 0 && ktxInfo.Height > 0) {
                            ormTexture.Resolution = new[] { ktxInfo.Width, ktxInfo.Height };
                            ormTexture.MipmapCount = ktxInfo.MipLevels;
                            if (!string.IsNullOrEmpty(ktxInfo.CompressionFormat)) {
                                ormTexture.CompressionFormat = ktxInfo.CompressionFormat == "UASTC"
                                    ? CompressionFormat.UASTC
                                    : CompressionFormat.ETC1S;
                            }
                        }
                    } catch { }
                }

                detectedOrms.Add(ormTexture);
            }

            if (detectedOrms.Count > 0) {
                logService.LogInfo($"Detected {detectedOrms.Count} ORM textures, {associations.Count} associations");
            }

        } catch (Exception ex) {
            logService.LogError($"Error detecting ORM textures: {ex.Message}");
        }

        return new ORMTexturesDetectedEventArgs(detectedOrms, associations);
    }

    /// <summary>
    /// Generates virtual ORM textures for groups with AO/Gloss/Metallic/Height components.
    /// </summary>
    private VirtualORMTexturesGeneratedEventArgs GenerateVirtualORMTextures(
        IReadOnlyList<TextureResource> textures,
        int projectId) {

        var generatedOrms = new List<ORMTextureResource>();
        var associations = new List<(TextureResource texture, string subGroupName, ORMTextureResource orm)>();

        try {
            var textureGroups = textures
                .Where(t => !t.IsORMTexture && !string.IsNullOrEmpty(t.GroupName))
                .GroupBy(t => t.GroupName)
                .ToList();

            foreach (var group in textureGroups) {
                string groupName = group.Key!;

                TextureResource? aoTexture = group.FirstOrDefault(t => t.TextureType == "AO");
                TextureResource? glossTexture = group.FirstOrDefault(t =>
                    t.TextureType == "Gloss" || t.TextureType == "Roughness");
                TextureResource? metallicTexture = group.FirstOrDefault(t => t.TextureType == "Metallic");
                TextureResource? heightTexture = group.FirstOrDefault(t => t.TextureType == "Height");

                if (aoTexture == null && glossTexture == null) continue;

                int channelCount = (aoTexture != null ? 1 : 0) +
                                  (glossTexture != null ? 1 : 0) +
                                  (metallicTexture != null ? 1 : 0) +
                                  (heightTexture != null ? 1 : 0);

                if (channelCount < 2) continue;

                // Skip if already has parent ORM
                bool alreadyHasORM = (aoTexture?.ParentORMTexture != null) ||
                                     (glossTexture?.ParentORMTexture != null) ||
                                     (metallicTexture?.ParentORMTexture != null) ||
                                     (heightTexture?.ParentORMTexture != null);

                if (alreadyHasORM) continue;

                ChannelPackingMode packingMode;
                string suffix;

                if (heightTexture != null && metallicTexture != null) {
                    packingMode = ChannelPackingMode.OGMH;
                    suffix = "_ogmh";
                } else if (metallicTexture != null) {
                    packingMode = ChannelPackingMode.OGM;
                    suffix = "_ogm";
                } else {
                    packingMode = ChannelPackingMode.OG;
                    suffix = "_og";
                }

                string baseName = groupName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)
                    ? groupName.Substring(0, groupName.Length - 4)
                    : groupName;
                string ormName = baseName + suffix;

                var sourceForResolution = aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture;
                int[] resolution = sourceForResolution?.Resolution ?? [0, 0];

                string? sourcePath = (aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture)?.Path;
                string? directory = !string.IsNullOrEmpty(sourcePath) ? Path.GetDirectoryName(sourcePath) : null;

                var ormTexture = new ORMTextureResource {
                    Name = ormName,
                    GroupName = groupName,
                    Path = directory != null ? Path.Combine(directory, ormName + ".ktx2") : null,
                    PackingMode = packingMode,
                    AOSource = aoTexture,
                    GlossSource = glossTexture,
                    MetallicSource = metallicTexture,
                    HeightSource = heightTexture,
                    Resolution = resolution,
                    Status = "Not Packed",
                    Extension = ".ktx2",
                    TextureType = $"ORM ({packingMode})",
                    ProjectId = projectId,
                    SettingsKey = $"orm_{groupName}_{ormName}"
                };

                ormTexture.LoadSettings();

                if (aoTexture != null) associations.Add((aoTexture, ormName, ormTexture));
                if (glossTexture != null) associations.Add((glossTexture, ormName, ormTexture));
                if (metallicTexture != null) associations.Add((metallicTexture, ormName, ormTexture));
                if (heightTexture != null) associations.Add((heightTexture, ormName, ormTexture));

                generatedOrms.Add(ormTexture);
            }

            if (generatedOrms.Count > 0) {
                logService.LogInfo($"Generated {generatedOrms.Count} virtual ORM textures");
            }

        } catch (Exception ex) {
            logService.LogError($"Error generating virtual ORM textures: {ex.Message}");
        }

        return new VirtualORMTexturesGeneratedEventArgs(generatedOrms, associations);
    }

    /// <summary>
    /// Restores upload states from database for all resource types.
    /// </summary>
    private async Task<UploadStatesRestoredEventArgs> RestoreUploadStatesAsync(
        IReadOnlyList<TextureResource> textures,
        IReadOnlyList<ModelResource> models,
        IReadOnlyList<MaterialResource> materials,
        CancellationToken ct) {

        var restoredTextures = new List<(TextureResource texture, string status, string? hash, string? url, DateTime? uploadedAt)>();
        var restoredModels = new List<(ModelResource model, string status, string? hash, string? url, DateTime? uploadedAt)>();
        var restoredMaterials = new List<(MaterialResource material, string status, string? hash, string? url, DateTime? uploadedAt)>();

        try {
            using var uploadStateService = new Data.UploadStateService();
            await uploadStateService.InitializeAsync();

            // Restore textures
            foreach (var texture in textures) {
                ct.ThrowIfCancellationRequested();

                var record = await uploadStateService.GetByResourceIdAsync(texture.ID, "Texture");

                // Fallback: try by local path (legacy)
                if (record == null && !string.IsNullOrEmpty(texture.Path)) {
                    var ktx2Path = Path.ChangeExtension(texture.Path, ".ktx2");
                    if (!string.IsNullOrEmpty(ktx2Path) && File.Exists(ktx2Path)) {
                        record = await uploadStateService.GetByLocalPathAsync(ktx2Path);
                    }
                }

                if (record != null && record.Status == "Uploaded") {
                    string status = "Uploaded";

                    // Check if file changed since upload
                    if (!string.IsNullOrEmpty(texture.Path)) {
                        var ktx2Path = Path.ChangeExtension(texture.Path, ".ktx2");
                        if (!string.IsNullOrEmpty(ktx2Path) && File.Exists(ktx2Path)) {
                            status = CheckFileHashStatus(ktx2Path, record.ContentSha1);
                        }
                    }

                    restoredTextures.Add((texture, status, record.ContentSha1, record.CdnUrl, record.UploadedAt));
                }
            }

            // Restore models
            foreach (var model in models) {
                ct.ThrowIfCancellationRequested();

                var record = await uploadStateService.GetByResourceIdAsync(model.ID, "Model");

                if (record != null && record.Status == "Uploaded") {
                    string status = "Uploaded";

                    // Check if GLB file changed since upload
                    if (!string.IsNullOrEmpty(record.LocalPath) && File.Exists(record.LocalPath)) {
                        status = CheckFileHashStatus(record.LocalPath, record.ContentSha1);
                    }

                    restoredModels.Add((model, status, record.ContentSha1, record.CdnUrl, record.UploadedAt));
                }
            }

            // Restore materials
            foreach (var material in materials) {
                ct.ThrowIfCancellationRequested();

                var record = await uploadStateService.GetByResourceIdAsync(material.ID, "Material");

                if (record != null && record.Status == "Uploaded") {
                    string status = "Uploaded";

                    // Check if material JSON changed since upload
                    if (!string.IsNullOrEmpty(record.LocalPath) && File.Exists(record.LocalPath)) {
                        status = CheckFileHashStatus(record.LocalPath, record.ContentSha1);
                    }

                    restoredMaterials.Add((material, status, record.ContentSha1, record.CdnUrl, record.UploadedAt));
                }
            }

            if (restoredTextures.Count > 0 || restoredModels.Count > 0 || restoredMaterials.Count > 0) {
                logService.LogInfo($"Restored upload states: {restoredTextures.Count} textures, {restoredModels.Count} models, {restoredMaterials.Count} materials");
            }

        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logService.LogWarn($"Failed to restore upload states: {ex.Message}");
        }

        return new UploadStatesRestoredEventArgs(restoredTextures, restoredModels, restoredMaterials);
    }

    /// <summary>
    /// Checks if file hash matches the uploaded hash.
    /// </summary>
    private static string CheckFileHashStatus(string filePath, string? uploadedHash) {
        if (string.IsNullOrEmpty(uploadedHash)) {
            return "Uploaded";
        }

        try {
            using var stream = File.OpenRead(filePath);
            var currentHash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream)).ToLowerInvariant();
            return string.Equals(currentHash, uploadedHash, StringComparison.OrdinalIgnoreCase)
                ? "Uploaded"
                : "Outdated";
        } catch {
            return "Uploaded"; // If can't check, assume OK
        }
    }

    /// <summary>
    /// Scans the server folder for already exported files and marks resources as "Processed".
    /// </summary>
    private void ScanExportedFiles(
        string projectFolderPath,
        string projectName,
        IReadOnlyList<TextureResource> textures,
        IReadOnlyList<ModelResource> models,
        IReadOnlyList<MaterialResource> materials) {

        try {
            // projectFolderPath уже включает имя проекта: {base}/{projectName}
            var serverPath = Path.Combine(projectFolderPath, "server");
            var mappingPath = Path.Combine(serverPath, "mapping.json");

            logger.Debug($"Scanning for exported files: {mappingPath}");

            if (!File.Exists(mappingPath)) {
                logger.Debug($"No mapping.json found at {mappingPath}");
                return;
            }

            var mappingJson = File.ReadAllText(mappingPath);
            var mapping = System.Text.Json.JsonSerializer.Deserialize<MappingData>(mappingJson, new System.Text.Json.JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

            if (mapping == null) return;

            int processedCount = 0;

            // Mark models as Processed if they exist in mapping
            foreach (var model in models) {
                if (mapping.Models.ContainsKey(model.ID.ToString())) {
                    model.Status = "Processed";
                    processedCount++;
                }
            }

            // Mark materials as Processed if they exist in mapping
            foreach (var material in materials) {
                if (mapping.Materials.ContainsKey(material.ID.ToString())) {
                    material.Status = "Processed";
                    processedCount++;
                }
            }

            // Mark textures as Processed if they exist in mapping
            foreach (var texture in textures) {
                if (mapping.Textures.ContainsKey(texture.ID.ToString())) {
                    texture.Status = "Processed";
                    processedCount++;
                }
            }

            logService.LogInfo($"Scanned exported files: {processedCount} resources marked as Processed");

        } catch (Exception ex) {
            logService.LogWarn($"Failed to scan exported files: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies B2 upload status in background by checking if files exist on server.
    /// Updates resources that are marked as "Uploaded" but not found on B2.
    /// </summary>
    private async Task VerifyB2StatusInBackgroundAsync(
        IReadOnlyList<TextureResource> textures,
        IReadOnlyList<ModelResource> models,
        IReadOnlyList<MaterialResource> materials,
        string projectName) {

        // Skip if B2 is not configured
        if (string.IsNullOrEmpty(AppSettings.Default.B2KeyId) ||
            string.IsNullOrEmpty(AppSettings.Default.B2BucketName)) {
            logger.Debug("B2 verification skipped - not configured");
            return;
        }

        try {
            logService.LogInfo("Starting background B2 verification...");

            // Collect all resources with "Uploaded" status
            var uploadedResources = new List<BaseResource>();
            uploadedResources.AddRange(textures.Where(t => t.UploadStatus == "Uploaded"));
            uploadedResources.AddRange(models.Where(m => m.UploadStatus == "Uploaded"));
            uploadedResources.AddRange(materials.Where(m => m.UploadStatus == "Uploaded"));

            if (uploadedResources.Count == 0) {
                logger.Debug("B2 verification skipped - no uploaded resources");
                return;
            }

            // Get B2 credentials
            if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
                logger.Warn("B2 verification failed - could not decrypt application key");
                B2VerificationCompleted?.Invoke(this, B2VerificationCompletedEventArgs.Failed("Could not decrypt B2 key"));
                return;
            }

            using var b2Service = new B2UploadService();
            var settings = new B2UploadSettings {
                KeyId = AppSettings.Default.B2KeyId,
                ApplicationKey = appKey,
                BucketName = AppSettings.Default.B2BucketName,
                BucketId = AppSettings.Default.B2BucketId,
                PathPrefix = AppSettings.Default.B2PathPrefix
            };

            if (!await b2Service.AuthorizeAsync(settings)) {
                logger.Warn("B2 verification failed - authorization failed");
                B2VerificationCompleted?.Invoke(this, B2VerificationCompletedEventArgs.Failed("B2 authorization failed"));
                return;
            }

            // List all files from B2 for this project
            var b2Files = await b2Service.ListFilesAsync(projectName, 10000);
            // Normalize paths - B2 might return URL-encoded or plain paths
            var b2FilePaths = new HashSet<string>(
                b2Files.Select(f => Uri.UnescapeDataString(f.FileName)),
                StringComparer.OrdinalIgnoreCase);

            var notFoundOnServer = new List<BaseResource>();
            int verifiedCount = 0;

            // Log B2 files for debugging
            logger.Info($"B2 verification: Found {b2FilePaths.Count} files on server");
            foreach (var path in b2FilePaths.Take(10)) {
                logger.Debug($"B2 file: {path}");
            }

            // Create upload state service to persist status changes
            using var uploadStateService = new Data.UploadStateService();
            await uploadStateService.InitializeAsync();

            foreach (var resource in uploadedResources) {
                if (string.IsNullOrEmpty(resource.RemoteUrl)) {
                    logger.Debug($"B2 verification: {resource.Name} has no RemoteUrl, skipping");
                    continue;
                }

                // Extract remote path from URL (handles both full URLs and relative paths)
                var remotePath = ExtractRemotePathFromUrl(resource.RemoteUrl, projectName);
                if (string.IsNullOrEmpty(remotePath)) {
                    logger.Warn($"B2 verification: {resource.Name} - failed to extract path from URL: {resource.RemoteUrl}");
                    continue;
                }

                logger.Debug($"B2 verification: Checking {resource.Name}, path: {remotePath}");

                if (b2FilePaths.Contains(remotePath)) {
                    verifiedCount++;
                    logger.Debug($"B2 verification: {resource.Name} VERIFIED at {remotePath}");
                } else {
                    // File not found on B2 - update status in memory and database
                    resource.UploadStatus = "Not on Server";
                    notFoundOnServer.Add(resource);
                    logger.Warn($"B2 verification: {resource.Name} NOT FOUND. Expected: {remotePath}");

                    // Persist status change to database
                    if (!string.IsNullOrEmpty(resource.Path)) {
                        // For textures, the uploaded file is .ktx2
                        var localPath = resource is TextureResource
                            ? Path.ChangeExtension(resource.Path, ".ktx2")
                            : resource.Path;

                        if (!string.IsNullOrEmpty(localPath)) {
                            var updated = await uploadStateService.UpdateStatusByLocalPathAsync(
                                localPath,
                                "Not on Server",
                                "File not found on B2 during verification");

                            if (updated) {
                                logger.Debug($"B2 verification: Persisted 'Not on Server' status for {resource.Name}");
                            }
                        }
                    }
                }
            }

            if (notFoundOnServer.Count > 0) {
                logService.LogWarn($"B2 verification: {notFoundOnServer.Count} files not found on server");
            } else {
                logService.LogInfo($"B2 verification complete: {verifiedCount} files verified");
            }

            B2VerificationCompleted?.Invoke(this, new B2VerificationCompletedEventArgs(
                notFoundOnServer,
                verifiedCount,
                uploadedResources.Count));

        } catch (Exception ex) {
            logger.Error(ex, "B2 verification failed");
            B2VerificationCompleted?.Invoke(this, B2VerificationCompletedEventArgs.Failed(ex.Message));
        }
    }

    /// <summary>
    /// Extracts remote path from CDN URL and decodes it.
    /// Handles both full URLs (https://cdn.example.com/path) and relative paths (content/file.ktx2).
    /// </summary>
    private static string? ExtractRemotePathFromUrl(string cdnUrl, string projectName) {
        if (string.IsNullOrEmpty(cdnUrl)) return null;

        try {
            string path;

            // Check if it's a full URL or a relative path
            if (Uri.TryCreate(cdnUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https")) {
                // Full URL - extract path from AbsolutePath (already URL-decoded by Uri)
                path = uri.AbsolutePath.TrimStart('/');
            } else {
                // Relative path - use as-is, just decode if needed
                path = Uri.UnescapeDataString(cdnUrl.TrimStart('/'));
            }

            // Normalize path separators
            path = path.Replace('\\', '/');

            // If path doesn't start with project name, prepend it
            if (!path.StartsWith(projectName + "/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(projectName + "\\", StringComparison.OrdinalIgnoreCase)) {
                return $"{projectName}/{path}";
            }

            return path;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Finds a texture by pattern in the specified directory.
    /// </summary>
    private static TextureResource? FindTextureByPattern(
        IEnumerable<TextureResource> textures,
        string directory,
        string namePattern) {

        return textures.FirstOrDefault(t => {
            if (string.IsNullOrEmpty(t.Path)) return false;
            if (Path.GetDirectoryName(t.Path) != directory) return false;

            string textureName = Path.GetFileNameWithoutExtension(t.Path);
            return string.Equals(textureName, namePattern, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Synchronously reads KTX2 header metadata (fast - only ~50 bytes).
    /// </summary>
    private static (int Width, int Height, int MipLevels, string? CompressionFormat) GetKtx2InfoSync(string ktx2Path) {
        using var stream = File.OpenRead(ktx2Path);
        using var reader = new BinaryReader(stream);

        reader.BaseStream.Seek(12, SeekOrigin.Begin);
        uint vkFormat = reader.ReadUInt32();

        reader.BaseStream.Seek(20, SeekOrigin.Begin);
        int width = (int)reader.ReadUInt32();
        int height = (int)reader.ReadUInt32();

        reader.BaseStream.Seek(40, SeekOrigin.Begin);
        int mipLevels = (int)reader.ReadUInt32();
        uint supercompression = reader.ReadUInt32();

        string? compressionFormat = null;
        if (vkFormat == 0) {
            compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
        }

        return (width, height, mipLevels, compressionFormat);
    }
}

#region Event Args and Request Types

/// <summary>
/// Request for loading assets
/// </summary>
public class AssetLoadRequest {
    public string? ProjectFolderPath { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectsBasePath { get; init; }
    public int ProjectId { get; init; }
}

/// <summary>
/// Event args when assets are loaded
/// </summary>
public class AssetsLoadedEventArgs : EventArgs {
    public IReadOnlyList<TextureResource> Textures { get; }
    public IReadOnlyList<ModelResource> Models { get; }
    public IReadOnlyList<MaterialResource> Materials { get; }
    public IReadOnlyDictionary<int, string> FolderPaths { get; }

    public AssetsLoadedEventArgs(
        IReadOnlyList<TextureResource> textures,
        IReadOnlyList<ModelResource> models,
        IReadOnlyList<MaterialResource> materials,
        IReadOnlyDictionary<int, string> folderPaths) {
        Textures = textures;
        Models = models;
        Materials = materials;
        FolderPaths = folderPaths;
    }
}

/// <summary>
/// Event args for loading progress
/// </summary>
public class AssetLoadProgressEventArgs : EventArgs {
    public int Processed { get; }
    public int Total { get; }
    public string? CurrentAsset { get; }

    public AssetLoadProgressEventArgs(int processed, int total, string? currentAsset = null) {
        Processed = processed;
        Total = total;
        CurrentAsset = currentAsset;
    }
}

/// <summary>
/// Event args when ORM textures are detected
/// </summary>
public class ORMTexturesDetectedEventArgs : EventArgs {
    public IReadOnlyList<ORMTextureResource> DetectedORMs { get; }
    public IReadOnlyList<(TextureResource texture, string subGroupName, ORMTextureResource orm)> Associations { get; }
    public int DetectedCount => DetectedORMs.Count;

    public ORMTexturesDetectedEventArgs(
        IReadOnlyList<ORMTextureResource> detectedOrms,
        IReadOnlyList<(TextureResource, string, ORMTextureResource)> associations) {
        DetectedORMs = detectedOrms;
        Associations = associations;
    }
}

/// <summary>
/// Event args when virtual ORM textures are generated
/// </summary>
public class VirtualORMTexturesGeneratedEventArgs : EventArgs {
    public IReadOnlyList<ORMTextureResource> GeneratedORMs { get; }
    public IReadOnlyList<(TextureResource texture, string subGroupName, ORMTextureResource orm)> Associations { get; }
    public int GeneratedCount => GeneratedORMs.Count;

    public VirtualORMTexturesGeneratedEventArgs(
        IReadOnlyList<ORMTextureResource> generatedOrms,
        IReadOnlyList<(TextureResource, string, ORMTextureResource)> associations) {
        GeneratedORMs = generatedOrms;
        Associations = associations;
    }
}

/// <summary>
/// Event args when upload states are restored
/// </summary>
public class UploadStatesRestoredEventArgs : EventArgs {
    public IReadOnlyList<(TextureResource texture, string status, string? hash, string? url, DateTime? uploadedAt)> RestoredTextures { get; }
    public IReadOnlyList<(ModelResource model, string status, string? hash, string? url, DateTime? uploadedAt)> RestoredModels { get; }
    public IReadOnlyList<(MaterialResource material, string status, string? hash, string? url, DateTime? uploadedAt)> RestoredMaterials { get; }
    public int RestoredCount => RestoredTextures.Count + RestoredModels.Count + RestoredMaterials.Count;

    public UploadStatesRestoredEventArgs(
        IReadOnlyList<(TextureResource, string, string?, string?, DateTime?)> restoredTextures,
        IReadOnlyList<(ModelResource, string, string?, string?, DateTime?)>? restoredModels = null,
        IReadOnlyList<(MaterialResource, string, string?, string?, DateTime?)>? restoredMaterials = null) {
        RestoredTextures = restoredTextures;
        RestoredModels = restoredModels ?? Array.Empty<(ModelResource, string, string?, string?, DateTime?)>();
        RestoredMaterials = restoredMaterials ?? Array.Empty<(MaterialResource, string, string?, string?, DateTime?)>();
    }
}

/// <summary>
/// Event args for loading errors
/// </summary>
public class AssetLoadErrorEventArgs : EventArgs {
    public string Title { get; }
    public string Message { get; }

    public AssetLoadErrorEventArgs(string title, string message) {
        Title = title;
        Message = message;
    }
}

/// <summary>
/// Event args for B2 verification completion
/// </summary>
public class B2VerificationCompletedEventArgs : EventArgs {
    /// <summary>
    /// Resources that were marked as uploaded but not found on B2
    /// </summary>
    public IReadOnlyList<BaseResource> NotFoundOnServer { get; }

    /// <summary>
    /// Resources verified as present on B2
    /// </summary>
    public int VerifiedCount { get; }

    /// <summary>
    /// Total resources checked
    /// </summary>
    public int TotalChecked { get; }

    /// <summary>
    /// Error message if verification failed
    /// </summary>
    public string? ErrorMessage { get; }

    public bool Success => string.IsNullOrEmpty(ErrorMessage);

    public B2VerificationCompletedEventArgs(
        IReadOnlyList<BaseResource> notFoundOnServer,
        int verifiedCount,
        int totalChecked,
        string? errorMessage = null) {
        NotFoundOnServer = notFoundOnServer;
        VerifiedCount = verifiedCount;
        TotalChecked = totalChecked;
        ErrorMessage = errorMessage;
    }

    public static B2VerificationCompletedEventArgs Failed(string errorMessage) {
        return new B2VerificationCompletedEventArgs(
            Array.Empty<BaseResource>(),
            0,
            0,
            errorMessage);
    }
}

#endregion

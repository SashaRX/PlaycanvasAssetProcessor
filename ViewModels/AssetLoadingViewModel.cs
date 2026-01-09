using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureConversion.Core;
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
        if (string.IsNullOrEmpty(request?.ProjectFolderPath) || string.IsNullOrEmpty(request.ProjectName)) {
            ErrorOccurred?.Invoke(this, new AssetLoadErrorEventArgs("Invalid Request", "Project folder path or name is empty"));
            return;
        }

        logService.LogInfo("=== AssetLoadingViewModel.LoadAssetsAsync CALLED ===");

        IsLoading = true;
        LoadingStatus = "Loading assets...";
        LoadingProgress = 0;
        LoadingTotal = 0;

        try {
            // Create progress reporter that will marshal to UI thread
            var progress = new Progress<AssetLoadProgress>(p => {
                LoadingProgress = p.Processed;
                LoadingTotal = p.Total;
                LoadingProgressChanged?.Invoke(this, new AssetLoadProgressEventArgs(p.Processed, p.Total, p.CurrentAsset));
            });

            // Run loading on background thread to not block UI
            var result = await Task.Run(async () => {
                return await assetLoadCoordinator.LoadAssetsFromJsonAsync(
                    request.ProjectFolderPath,
                    request.ProjectName,
                    request.ProjectsBasePath,
                    progress,
                    ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

            if (!result.Success) {
                logService.LogError($"Failed to load assets: {result.Error}");
                ErrorOccurred?.Invoke(this, new AssetLoadErrorEventArgs("Load Failed", result.Error ?? "Unknown error"));
                return;
            }

            // Raise event with loaded assets
            AssetsLoaded?.Invoke(this, new AssetsLoadedEventArgs(
                result.Textures,
                result.Models,
                result.Materials,
                result.FolderPaths
            ));

            logService.LogInfo($"Loaded {result.Textures.Count} textures, {result.Models.Count} models, {result.Materials.Count} materials");

            // Post-processing on background thread
            LoadingStatus = "Detecting ORM textures...";
            var ormResult = await Task.Run(() =>
                DetectORMTextures(request.ProjectFolderPath, result.Textures, request.ProjectId), ct).ConfigureAwait(false);
            if (ormResult.DetectedCount > 0) {
                ORMTexturesDetected?.Invoke(this, ormResult);
            }

            LoadingStatus = "Generating virtual ORM textures...";
            var virtualOrmResult = await Task.Run(() =>
                GenerateVirtualORMTextures(result.Textures, request.ProjectId), ct).ConfigureAwait(false);
            if (virtualOrmResult.GeneratedCount > 0) {
                VirtualORMTexturesGenerated?.Invoke(this, virtualOrmResult);
            }

            LoadingStatus = "Restoring upload states...";
            var uploadResult = await Task.Run(async () =>
                await RestoreUploadStatesAsync(result.Textures, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            if (uploadResult.RestoredCount > 0) {
                UploadStatesRestored?.Invoke(this, uploadResult);
            }

            LoadingStatus = "Loading complete";
            logService.LogInfo("=== AssetLoadingViewModel.LoadAssetsAsync COMPLETED ===");

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
    /// Restores upload states from database.
    /// </summary>
    private async Task<UploadStatesRestoredEventArgs> RestoreUploadStatesAsync(
        IReadOnlyList<TextureResource> textures,
        CancellationToken ct) {

        var restoredTextures = new List<(TextureResource texture, string status, string? hash, string? url, DateTime? uploadedAt)>();

        try {
            using var uploadStateService = new Data.UploadStateService();
            await uploadStateService.InitializeAsync();

            foreach (var texture in textures) {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(texture.Path)) continue;

                var ktx2Path = Path.ChangeExtension(texture.Path, ".ktx2");
                if (string.IsNullOrEmpty(ktx2Path) || !File.Exists(ktx2Path)) continue;

                var record = await uploadStateService.GetByLocalPathAsync(ktx2Path);
                if (record != null && record.Status == "Uploaded") {
                    // Check if file has changed since last upload
                    using var stream = File.OpenRead(ktx2Path);
                    var currentHash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream)).ToLowerInvariant();

                    string status = string.Equals(currentHash, record.ContentSha1, StringComparison.OrdinalIgnoreCase)
                        ? "Uploaded"
                        : "Outdated";

                    restoredTextures.Add((texture, status, record.ContentSha1, record.CdnUrl, record.UploadedAt));
                }
            }

            if (restoredTextures.Count > 0) {
                logService.LogInfo($"Restored upload state for {restoredTextures.Count} textures");
            }

        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logService.LogWarn($"Failed to restore upload states: {ex.Message}");
        }

        return new UploadStatesRestoredEventArgs(restoredTextures);
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
    public int RestoredCount => RestoredTextures.Count;

    public UploadStatesRestoredEventArgs(
        IReadOnlyList<(TextureResource, string, string?, string?, DateTime?)> restoredTextures) {
        RestoredTextures = restoredTextures;
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

#endregion

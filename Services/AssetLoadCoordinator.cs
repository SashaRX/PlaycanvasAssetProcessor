using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureConversion.Core;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AssetProcessor.Services;

/// <summary>
/// Coordinates asset loading from local JSON cache.
/// Returns processed assets; UI updates are handled by the caller.
/// </summary>
public sealed class AssetLoadCoordinator : IAssetLoadCoordinator {
    private readonly IProjectAssetService projectAssetService;
    private readonly IAssetResourceService assetResourceService;
    private readonly ILogService logService;

    // Semaphore to limit concurrent asset processing
    private readonly SemaphoreSlim processSemaphore = new(32);


    public AssetLoadCoordinator(
        IProjectAssetService projectAssetService,
        IAssetResourceService assetResourceService,
        ILogService logService) {
        this.projectAssetService = projectAssetService ?? throw new ArgumentNullException(nameof(projectAssetService));
        this.assetResourceService = assetResourceService ?? throw new ArgumentNullException(nameof(assetResourceService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public async Task<AssetLoadResult> LoadAssetsFromJsonAsync(
        string projectFolderPath,
        string projectName,
        string projectsRoot,
        int projectId,
        SharedProgressState? progressState,
        CancellationToken cancellationToken) {
        try {
            if (string.IsNullOrWhiteSpace(projectFolderPath)) {
                return AssetLoadResult.Failed("Project folder path is required");
            }

            if (string.IsNullOrWhiteSpace(projectName)) {
                return AssetLoadResult.Failed("Project name is required");
            }

            if (string.IsNullOrWhiteSpace(projectsRoot)) {
                return AssetLoadResult.Failed("Projects root path is required");
            }

            logService.LogInfo("=== AssetLoadCoordinator: Loading assets from JSON ===");

            // Load JSON from cache - use ConfigureAwait(false) to not capture UI context
            JArray? assetsResponse = await projectAssetService.LoadAssetsFromJsonAsync(
                projectFolderPath, cancellationToken).ConfigureAwait(false);

            if (assetsResponse == null) {
                logService.LogInfo("No local assets_list.json found");
                return AssetLoadResult.Failed("No local assets_list.json found");
            }

            logService.LogInfo($"Loaded {assetsResponse.Count} assets from local JSON cache");

            // Build folder hierarchy
            var folderPaths = new Dictionary<int, string>();
            assetResourceService.BuildFolderHierarchy(assetsResponse, folderPaths);

            // Filter to supported assets (those with file property)
            var supportedAssets = assetsResponse
                .Where(asset => asset["file"] != null)
                .ToList();

            int totalAssets = supportedAssets.Count;

            // Initialize shared progress state (no SynchronizationContext involvement)
            progressState?.Reset();
            progressState?.SetTotal(totalAssets);

            // Process assets concurrently
            var textures = new List<TextureResource>();
            var models = new List<ModelResource>();
            var materials = new List<MaterialResource>();
            var lockObj = new object();

            var parameters = new AssetProcessingParameters(projectsRoot, projectName, folderPaths, 0, projectId);

            var tasks = supportedAssets.Select(async asset => {
                try {
                    await processSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try {
                        var result = await assetResourceService.ProcessAssetAsync(
                            asset, parameters, cancellationToken).ConfigureAwait(false);

                        string? assetName = result?.Resource?.Name ?? asset["name"]?.ToString();

                        if (result != null) {
                            lock (lockObj) {
                                switch (result.ResultType) {
                                    case AssetProcessingResultType.Texture when result.Resource is TextureResource texture:
                                        textures.Add(texture);
                                        break;
                                    case AssetProcessingResultType.Model when result.Resource is ModelResource model:
                                        models.Add(model);
                                        break;
                                    case AssetProcessingResultType.Material when result.Resource is MaterialResource material:
                                        materials.Add(material);
                                        break;
                                }
                            }
                        }

                        // Update shared progress state (no marshalling, just writes to volatile fields)
                        progressState?.IncrementCurrent(assetName);
                    } finally {
                        processSemaphore.Release();
                    }
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    logService.LogError($"Error processing asset: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Mark progress as complete
            progressState?.Complete();

            logService.LogInfo($"Processed {textures.Count} textures, {models.Count} models, {materials.Count} materials");
            logService.LogInfo("=== AssetLoadCoordinator: Loading complete ===");

            return AssetLoadResult.Succeeded(
                textures.AsReadOnly(),
                models.AsReadOnly(),
                materials.AsReadOnly(),
                folderPaths);
        } catch (OperationCanceledException) {
            logService.LogInfo("Asset loading cancelled");
            progressState?.Complete();
            return AssetLoadResult.Failed("Asset loading cancelled");
        } catch (Exception ex) {
            logService.LogError($"Failed to load assets: {ex.Message}");
            progressState?.Complete();
            return AssetLoadResult.Failed($"Failed to load assets: {ex.Message}");
        }
    }

    public IReadOnlyList<ORMTextureResource> GenerateVirtualORMTextures(
        IEnumerable<TextureResource> textures,
        int projectId) {
        logService.LogInfo("=== Generating virtual ORM textures ===");

        var result = new List<ORMTextureResource>();

        try {
            // Group textures by GroupName, excluding existing ORM textures
            var textureGroups = textures
                .Where(t => !t.IsORMTexture && !string.IsNullOrEmpty(t.GroupName))
                .GroupBy(t => t.GroupName)
                .ToList();

            foreach (var group in textureGroups) {
                string groupName = group.Key!;

                // Find ORM components in group
                TextureResource? aoTexture = group.FirstOrDefault(t => t.TextureType == "AO");
                TextureResource? glossTexture = group.FirstOrDefault(t =>
                    t.TextureType == "Gloss" || t.TextureType == "Roughness");
                TextureResource? metallicTexture = group.FirstOrDefault(t => t.TextureType == "Metallic");
                TextureResource? heightTexture = group.FirstOrDefault(t => t.TextureType == "Height");

                // Need at least AO or Gloss for ORM
                if (aoTexture == null && glossTexture == null) {
                    continue;
                }

                // Count available channels
                int channelCount = (aoTexture != null ? 1 : 0) +
                                  (glossTexture != null ? 1 : 0) +
                                  (metallicTexture != null ? 1 : 0) +
                                  (heightTexture != null ? 1 : 0);

                if (channelCount < 2) {
                    continue; // Need at least 2 channels for ORM packing
                }

                // Check if components already have ParentORMTexture
                bool alreadyHasORM = (aoTexture?.ParentORMTexture != null) ||
                                     (glossTexture?.ParentORMTexture != null) ||
                                     (metallicTexture?.ParentORMTexture != null) ||
                                     (heightTexture?.ParentORMTexture != null);

                if (alreadyHasORM) {
                    continue; // ORM already created from existing KTX2 file
                }

                // Determine packing mode
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

                // Clean up group name for ORM name
                string baseName = groupName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)
                    ? groupName[..^4]
                    : groupName;
                string ormName = baseName + suffix;

                // Get resolution from first available source
                var sourceForResolution = aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture;
                int[] resolution = sourceForResolution?.Resolution ?? [0, 0];

                // Get path from first available source
                string? sourcePath = (aoTexture ?? glossTexture ?? metallicTexture ?? heightTexture)?.Path;
                string? directory = !string.IsNullOrEmpty(sourcePath) ? Path.GetDirectoryName(sourcePath) : null;

                // Create virtual ORM texture
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

                // Load persisted settings if available
                ormTexture.LoadSettings();

                // Set SubGroupName and ParentORMTexture for components
                if (aoTexture != null) {
                    aoTexture.SubGroupName = ormName;
                    aoTexture.ParentORMTexture = ormTexture;
                }
                if (glossTexture != null) {
                    glossTexture.SubGroupName = ormName;
                    glossTexture.ParentORMTexture = ormTexture;
                }
                if (metallicTexture != null) {
                    metallicTexture.SubGroupName = ormName;
                    metallicTexture.ParentORMTexture = ormTexture;
                }
                if (heightTexture != null) {
                    heightTexture.SubGroupName = ormName;
                    heightTexture.ParentORMTexture = ormTexture;
                }

                result.Add(ormTexture);

                logService.LogInfo($"  Generated virtual ORM: {ormName} ({packingMode}) - " +
                    $"AO:{(aoTexture != null ? "✓" : "✗")} " +
                    $"Gloss:{(glossTexture != null ? "✓" : "✗")} " +
                    $"Metal:{(metallicTexture != null ? "✓" : "✗")} " +
                    $"Height:{(heightTexture != null ? "✓" : "✗")}");
            }

            logService.LogInfo($"=== Generated {result.Count} virtual ORM textures ===");
        } catch (Exception ex) {
            logService.LogError($"Error generating virtual ORM textures: {ex.Message}");
        }

        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<ORMTextureResource>> DetectExistingORMTexturesAsync(
        string projectFolderPath,
        IEnumerable<TextureResource> existingTextures,
        int projectId,
        CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(projectFolderPath) || !Directory.Exists(projectFolderPath)) {
            return Array.Empty<ORMTextureResource>();
        }

        logService.LogInfo("=== Detecting local ORM textures ===");

        var result = new List<ORMTextureResource>();
        var texturesList = existingTextures.ToList();

        try {
            // Scan all .ktx2 files recursively
            var ktx2Files = Directory.GetFiles(projectFolderPath, "*.ktx2", SearchOption.AllDirectories);

            foreach (var ktx2Path in ktx2Files) {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileNameWithoutExtension(ktx2Path);

                // Check for _og/_ogm/_ogmh patterns
                ChannelPackingMode? packingMode = null;
                string baseName = fileName;

                if (fileName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) {
                    packingMode = ChannelPackingMode.OG;
                    baseName = fileName[..^3];
                } else if (fileName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) {
                    packingMode = ChannelPackingMode.OGM;
                    baseName = fileName[..^4];
                } else if (fileName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) {
                    packingMode = ChannelPackingMode.OGMH;
                    baseName = fileName[..^5];
                }

                if (!packingMode.HasValue) {
                    continue; // Not an ORM texture
                }

                // Find source textures by base name
                string directory = Path.GetDirectoryName(ktx2Path) ?? "";
                TextureResource? aoTexture = FindTextureByPattern(texturesList, directory, baseName + "_ao");
                TextureResource? glossTexture = FindTextureByPattern(texturesList, directory, baseName + "_gloss");
                TextureResource? metallicTexture = FindTextureByPattern(texturesList, directory, baseName + "_metallic")
                                                ?? FindTextureByPattern(texturesList, directory, baseName + "_metalness")
                                                ?? FindTextureByPattern(texturesList, directory, baseName + "_Metalness")
                                                ?? FindTextureByPattern(texturesList, directory, baseName + "_metalic");

                // Create ORMTextureResource
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

                // Load persisted settings
                ormTexture.LoadSettings();

                // Set SubGroupName for source textures
                if (aoTexture != null) {
                    aoTexture.SubGroupName = fileName;
                    aoTexture.ParentORMTexture = ormTexture;
                }
                if (glossTexture != null) {
                    glossTexture.SubGroupName = fileName;
                    glossTexture.ParentORMTexture = ormTexture;
                }
                if (metallicTexture != null) {
                    metallicTexture.SubGroupName = fileName;
                    metallicTexture.ParentORMTexture = ormTexture;
                }

                // Extract file info and metadata from KTX2
                if (File.Exists(ktx2Path)) {
                    var fileInfo = new FileInfo(ktx2Path);
                    ormTexture.CompressedSize = fileInfo.Length;
                    ormTexture.Size = (int)fileInfo.Length;

                    try {
                        var ktxInfo = await GetKtx2InfoAsync(ktx2Path);
                        if (ktxInfo.Width > 0 && ktxInfo.Height > 0) {
                            ormTexture.Resolution = [ktxInfo.Width, ktxInfo.Height];
                            ormTexture.MipmapCount = ktxInfo.MipLevels;
                            if (!string.IsNullOrEmpty(ktxInfo.CompressionFormat)) {
                                ormTexture.CompressionFormat = ktxInfo.CompressionFormat == "UASTC"
                                    ? CompressionFormat.UASTC
                                    : CompressionFormat.ETC1S;
                                logService.LogInfo($"    Extracted metadata: {ktxInfo.Width}x{ktxInfo.Height}, {ktxInfo.MipLevels} mips, {ktxInfo.CompressionFormat}");
                            }
                        }
                    } catch (Exception ex) {
                        logService.LogError($"  Failed to extract KTX2 metadata for {fileName}: {ex.Message}");
                    }
                }

                result.Add(ormTexture);
                logService.LogInfo($"  Loaded ORM texture: {fileName} ({packingMode.Value})");
            }

            logService.LogInfo($"=== Detected {result.Count} ORM textures ===");
        } catch (OperationCanceledException) {
            logService.LogInfo("ORM detection cancelled");
        } catch (Exception ex) {
            logService.LogError($"Error detecting ORM textures: {ex.Message}");
        }

        return result.AsReadOnly();
    }

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

    private static async Task<(int Width, int Height, int MipLevels, string? CompressionFormat)> GetKtx2InfoAsync(string ktx2Path) {
        return await Task.Run(() => {
            using var stream = File.OpenRead(ktx2Path);
            using var reader = new BinaryReader(stream);

            // KTX2 header structure
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
        });
    }
}

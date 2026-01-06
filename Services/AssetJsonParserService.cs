using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.Core;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

/// <summary>
/// Реализация сервиса парсинга JSON ассетов.
/// </summary>
public class AssetJsonParserService : IAssetJsonParserService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly IProjectAssetService _projectAssetService;
    private readonly IAssetResourceService _assetResourceService;
    private readonly ILogService _logService;
    private readonly SemaphoreSlim _semaphore;

    public event EventHandler<AssetProcessedEventArgs>? AssetProcessed;

    public AssetJsonParserService(
        IProjectAssetService projectAssetService,
        IAssetResourceService assetResourceService,
        ILogService logService) {
        _projectAssetService = projectAssetService ?? throw new ArgumentNullException(nameof(projectAssetService));
        _assetResourceService = assetResourceService ?? throw new ArgumentNullException(nameof(assetResourceService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _semaphore = new SemaphoreSlim(AppSettings.Default.GetTexturesSemaphoreLimit);
    }

    public async Task<AssetParsingResult> LoadAndParseAssetsAsync(
        string projectFolderPath,
        string projectName,
        CancellationToken cancellationToken) {
        try {
            _logService.LogInfo("=== LoadAndParseAssetsAsync CALLED ===");

            if (string.IsNullOrEmpty(projectFolderPath) || string.IsNullOrEmpty(projectName)) {
                return new AssetParsingResult {
                    Success = false,
                    ErrorMessage = "Project folder path or name is null or empty"
                };
            }

            JArray? assetsJson = await _projectAssetService.LoadAssetsFromJsonAsync(projectFolderPath, cancellationToken);
            if (assetsJson == null) {
                return new AssetParsingResult {
                    Success = false,
                    ErrorMessage = "Failed to load assets from JSON"
                };
            }

            _logService.LogInfo($"Loaded {assetsJson.Count} assets from local JSON cache");
            return await ParseAssetsAsync(assetsJson, projectFolderPath, projectName, cancellationToken);
        } catch (Exception ex) {
            _logService.LogError($"Error in LoadAndParseAssetsAsync: {ex.Message}");
            return new AssetParsingResult {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<AssetParsingResult> ParseAssetsAsync(
        JArray assetsJson,
        string projectFolderPath,
        string projectName,
        CancellationToken cancellationToken) {
        var textures = new ConcurrentBag<TextureResource>();
        var models = new ConcurrentBag<ModelResource>();
        var materials = new ConcurrentBag<MaterialResource>();

        // Строим иерархию папок
        var folderPaths = BuildFolderHierarchy(assetsJson);
        _assetResourceService.BuildFolderHierarchy(assetsJson, folderPaths);

        // Фильтруем только ассеты с файлами
        var supportedAssets = assetsJson.Where(asset => asset["file"] != null).ToList();
        int totalCount = supportedAssets.Count;
        int processedCount = 0;

        // Параллельная обработка
        var tasks = supportedAssets.Select(async asset => {
            try {
                await _semaphore.WaitAsync(cancellationToken);

                var parameters = new AssetProcessingParameters(
                    AppSettings.Default.ProjectsFolderPath,
                    projectName,
                    folderPaths,
                    0);

                var result = await _assetResourceService.ProcessAssetAsync(asset, parameters, cancellationToken);

                if (result != null) {
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

                int count = Interlocked.Increment(ref processedCount);
                AssetProcessed?.Invoke(this, new AssetProcessedEventArgs {
                    ProcessedCount = count,
                    TotalCount = totalCount,
                    AssetName = asset["name"]?.ToString()
                });
            } catch (Exception ex) {
                _logService.LogError($"Error processing asset: {ex.Message}");
            } finally {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logService.LogInfo($"=== ParseAssetsAsync COMPLETED: {textures.Count} textures, {models.Count} models, {materials.Count} materials ===");

        return new AssetParsingResult {
            Textures = textures.ToList(),
            Models = models.ToList(),
            Materials = materials.ToList(),
            FolderPaths = folderPaths,
            Success = true
        };
    }

    public async Task<IReadOnlyList<ORMTextureResource>> DetectORMTexturesAsync(
        string projectFolderPath,
        IEnumerable<TextureResource> existingTextures) {
        var ormTextures = new List<ORMTextureResource>();

        if (string.IsNullOrEmpty(projectFolderPath) || !Directory.Exists(projectFolderPath)) {
            return ormTextures;
        }

        _logService.LogInfo("=== Detecting local ORM textures ===");

        try {
            var ktx2Files = Directory.GetFiles(projectFolderPath, "*.ktx2", SearchOption.AllDirectories);
            var textureList = existingTextures.ToList();

            foreach (var ktx2Path in ktx2Files) {
                string fileName = Path.GetFileNameWithoutExtension(ktx2Path);

                // Проверяем паттерны _og/_ogm/_ogmh
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

                if (!packingMode.HasValue) {
                    continue;
                }

                string directory = Path.GetDirectoryName(ktx2Path) ?? "";
                var aoTexture = FindTextureByPattern(textureList, directory, baseName + "_ao");
                var glossTexture = FindTextureByPattern(textureList, directory, baseName + "_gloss");
                var metallicTexture = FindTextureByPattern(textureList, directory, baseName + "_metallic")
                                   ?? FindTextureByPattern(textureList, directory, baseName + "_metalness")
                                   ?? FindTextureByPattern(textureList, directory, baseName + "_Metalness") // case variant
                                   ?? FindTextureByPattern(textureList, directory, baseName + "_metalic");

                var ormTexture = new ORMTextureResource {
                    Name = fileName,
                    Path = ktx2Path,
                    PackingMode = packingMode.Value,
                    AOSource = aoTexture,
                    GlossSource = glossTexture,
                    MetallicSource = metallicTexture,
                    Status = "Converted",
                    Extension = ".ktx2"
                };

                // Извлекаем информацию из KTX2
                if (File.Exists(ktx2Path)) {
                    var fileInfo = new FileInfo(ktx2Path);
                    ormTexture.CompressedSize = fileInfo.Length;
                    ormTexture.Size = (int)fileInfo.Length;

                    try {
                        var ktxInfo = await GetKtx2InfoAsync(ktx2Path);
                        if (ktxInfo.Width > 0 && ktxInfo.Height > 0) {
                            ormTexture.Resolution = new[] { ktxInfo.Width, ktxInfo.Height };
                            ormTexture.MipmapCount = ktxInfo.MipLevels;
                            if (!string.IsNullOrEmpty(ktxInfo.CompressionFormat)) {
                                ormTexture.CompressionFormat = ktxInfo.CompressionFormat == "UASTC"
                                    ? CompressionFormat.UASTC
                                    : CompressionFormat.ETC1S;
                            }
                        }
                    } catch (Exception ex) {
                        _logService.LogError($"Failed to extract KTX2 metadata for {fileName}: {ex.Message}");
                    }
                }

                ormTextures.Add(ormTexture);
                _logService.LogInfo($"  Loaded ORM texture: {fileName} ({packingMode.Value})");
            }

            if (ormTextures.Count > 0) {
                _logService.LogInfo($"=== Detected {ormTextures.Count} ORM textures ===");
            } else {
                _logService.LogInfo("  No ORM textures found");
            }
        } catch (Exception ex) {
            _logService.LogError($"Error detecting ORM textures: {ex.Message}");
        }

        return ormTextures;
    }

    public Dictionary<int, string> BuildFolderHierarchy(JArray assetsJson) {
        var folderPaths = new Dictionary<int, string>();

        try {
            var folders = assetsJson.Where(asset => asset["type"]?.ToString() == "folder").ToList();
            var foldersById = new Dictionary<int, JToken>();

            foreach (JToken folder in folders) {
                int? folderId = folder["id"]?.Type == JTokenType.Integer ? (int?)folder["id"] : null;
                if (folderId.HasValue) {
                    foldersById[folderId.Value] = folder;
                }
            }

            string BuildFolderPath(int folderId) {
                if (folderPaths.ContainsKey(folderId)) {
                    return folderPaths[folderId];
                }

                if (!foldersById.ContainsKey(folderId)) {
                    return string.Empty;
                }

                JToken folder = foldersById[folderId];
                string folderName = PathSanitizer.SanitizePath(folder["name"]?.ToString());
                int? parentId = folder["parent"]?.Type == JTokenType.Integer ? (int?)folder["parent"] : null;

                string fullPath;
                if (parentId.HasValue && parentId.Value != 0) {
                    string parentPath = BuildFolderPath(parentId.Value);
                    fullPath = string.IsNullOrEmpty(parentPath) ? folderName : Path.Combine(parentPath, folderName);
                } else {
                    fullPath = folderName;
                }

                fullPath = PathSanitizer.SanitizePath(fullPath);
                folderPaths[folderId] = fullPath;
                return fullPath;
            }

            foreach (var folderId in foldersById.Keys) {
                BuildFolderPath(folderId);
            }

            _logService.LogInfo($"Built folder hierarchy with {folderPaths.Count} folders");
        } catch (Exception ex) {
            _logService.LogError($"Error building folder hierarchy: {ex.Message}");
        }

        return folderPaths;
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

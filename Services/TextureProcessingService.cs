using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;
using NLog;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class TextureProcessingService : ITextureProcessingService {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TextureConversion.Settings.PresetManager CachedPresetManager = new();

    private readonly ITextureConversionPipelineFactory pipelineFactory;

    public TextureProcessingService()
        : this(new TextureConversionPipelineFactory()) {
    }

    public TextureProcessingService(ITextureConversionPipelineFactory pipelineFactory) {
        this.pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
    }

    public async Task<TextureProcessingResult> ProcessTexturesAsync(TextureProcessingRequest request, CancellationToken cancellationToken) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Textures.Count == 0) {
            return new TextureProcessingResult {
                SuccessCount = 0,
                ErrorCount = 0,
                ErrorMessages = Array.Empty<string>(),
                PreviewTexture = null,
                PreviewTexturePath = null
            };
        }

        var globalSettings = TextureConversionSettingsManager.LoadSettings();
        var ktxPath = string.IsNullOrWhiteSpace(globalSettings.KtxExecutablePath)
            ? "ktx"
            : globalSettings.KtxExecutablePath;

        var pipeline = pipelineFactory.Create(ktxPath);

        int successCount = 0;
        int errorCount = 0;
        var errorMessages = new List<string>();
        TextureResource? previewTexture = null;
        string? previewPath = null;

        foreach (var texture in request.Textures) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                if (string.IsNullOrWhiteSpace(texture.Path)) {
                    errorCount++;
                    var errorMsg = $"{texture.Name ?? "Unknown"}: Empty file path";
                    errorMessages.Add(errorMsg);
                    MainWindowHelpers.LogError($"Skipping texture with empty path: {texture.Name ?? "Unknown"}");
                    continue;
                }

                var textureType = TextureResource.DetermineTextureType(texture.Name ?? string.Empty);
                texture.TextureType = textureType;

                var mipProfile = MipGenerationProfile.CreateDefault(MapTextureTypeToCore(textureType));

                var compressionSettingsData = request.SettingsProvider.GetCompressionSettings();
                compressionSettingsData.HistogramAnalysis = request.SettingsProvider.GetHistogramSettings();
                var compressionSettings = compressionSettingsData.ToCompressionSettings(globalSettings);

                var sourceDir = Path.GetDirectoryName(texture.Path) ?? Environment.CurrentDirectory;
                var sourceFileName = Path.GetFileNameWithoutExtension(texture.Path);
                var extension = compressionSettings.OutputFormat == OutputFormat.KTX2 ? ".ktx2" : ".basis";
                var outputPath = Path.Combine(sourceDir, sourceFileName + extension);

                MainWindowHelpers.LogInfo("=== CONVERSION START ===");
                MainWindowHelpers.LogInfo($"  Texture Name: {texture.Name}");
                MainWindowHelpers.LogInfo($"  Source Path: {texture.Path}");
                MainWindowHelpers.LogInfo($"  Output Path: {outputPath}");
                MainWindowHelpers.LogInfo("========================");

                var mipmapOutputDir = request.SettingsProvider.SaveSeparateMipmaps
                    ? Path.Combine(sourceDir, "mipmaps", sourceFileName)
                    : null;

                var toksvigSettings = request.SettingsProvider.GetToksvigSettings(texture.Path);

                var result = await pipeline.ConvertTextureAsync(
                    texture.Path,
                    outputPath,
                    mipProfile,
                    compressionSettings,
                    toksvigSettings,
                    request.SettingsProvider.SaveSeparateMipmaps,
                    mipmapOutputDir);

                if (!result.Success) {
                    texture.Status = "Error";
                    errorCount++;
                    var errorMsg = $"{texture.Name}: {result.Error ?? "Unknown error"}";
                    errorMessages.Add(errorMsg);
                    MainWindowHelpers.LogError($"✗ Failed to convert {texture.Name}: {result.Error}");
                    continue;
                }

                texture.CompressionFormat = compressionSettings.CompressionFormat.ToString();
                texture.MipmapCount = result.MipLevels;
                texture.Status = "Converted";

                if (result.ToksvigApplied) {
                    texture.ToksvigEnabled = true;
                    texture.NormalMapPath = result.NormalMapUsed;
                }

                if (texture.ToksvigEnabled && string.IsNullOrWhiteSpace(texture.NormalMapPath)) {
                    texture.ToksvigEnabled = false;
                }

                if (string.IsNullOrEmpty(texture.PresetName) || texture.PresetName == "(Auto)") {
                    var matchedPreset = CachedPresetManager.FindPresetByFileName(texture.Name ?? string.Empty);
                    texture.PresetName = matchedPreset?.Name ?? request.SettingsProvider.PresetName ?? "(Custom)";
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

                var (fileFound, fileSize, actualPath) = TryResolveOutputFile(outputPath, extension);

                if (fileFound && fileSize > 0) {
                    texture.CompressedSize = fileSize;
                    MainWindowHelpers.LogInfo($"✓ Successfully converted {texture.Name}");
                    MainWindowHelpers.LogInfo($"  Mipmaps: {result.MipLevels}, Size: {fileSize / 1024.0:F1} KB, Path: {actualPath}");
                } else {
                    MainWindowHelpers.LogError("✗ OUTPUT FILE NOT FOUND OR EMPTY!");
                    MainWindowHelpers.LogError($"  Expected: {outputPath}");
                    texture.CompressedSize = 0;
                }

                successCount++;

                if (request.SelectedTexture != null && ReferenceEquals(request.SelectedTexture, texture)) {
                    previewTexture = texture;
                    previewPath = actualPath;
                }
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                texture.Status = "Error";
                errorCount++;
                var errorMsg = $"{texture.Name}: {ex.Message}";
                errorMessages.Add(errorMsg);
                MainWindowHelpers.LogError($"✗ Exception processing {texture.Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
        }

        return new TextureProcessingResult {
            SuccessCount = successCount,
            ErrorCount = errorCount,
            ErrorMessages = errorMessages,
            PreviewTexture = previewTexture,
            PreviewTexturePath = previewPath
        };
    }

    public async Task<TexturePreviewResult?> LoadKtxPreviewAsync(TextureResource texture, CancellationToken cancellationToken) {
        if (texture == null) {
            throw new ArgumentNullException(nameof(texture));
        }

        var ktxPath = GetExistingKtx2Path(texture.Path);
        if (ktxPath == null) {
            Logger.Info($"KTX2 file not found for: {texture.Path}");
            return null;
        }

        var textureData = await Task.Run(() => Ktx2TextureLoader.LoadFromFile(ktxPath), cancellationToken);

        bool shouldEnableNormal = false;
        string? autoEnableReason = null;

        if (textureData.NormalLayoutMetadata != null) {
            shouldEnableNormal = true;
            autoEnableReason = $"KTX2 normal map with metadata (layout: {textureData.NormalLayoutMetadata.Layout})";
        } else if (string.Equals(texture.TextureType, "normal", StringComparison.OrdinalIgnoreCase)) {
            shouldEnableNormal = true;
            autoEnableReason = "KTX2 normal map detected by TextureType (no metadata)";
        }

        return new TexturePreviewResult {
            KtxPath = ktxPath,
            TextureData = textureData,
            ShouldEnableNormalReconstruction = shouldEnableNormal,
            AutoEnableReason = autoEnableReason
        };
    }

    public TextureAutoDetectResult AutoDetectPresets(IEnumerable<TextureResource> textures, ITextureConversionSettingsProvider settingsProvider) {
        if (textures == null) {
            throw new ArgumentNullException(nameof(textures));
        }

        if (settingsProvider == null) {
            throw new ArgumentNullException(nameof(settingsProvider));
        }

        int matchedCount = 0;
        int notMatchedCount = 0;

        foreach (var texture in textures) {
            if (texture == null || string.IsNullOrEmpty(texture.Name)) {
                continue;
            }

            var preset = CachedPresetManager.FindPresetByFileName(texture.Name);
            if (preset != null) {
                texture.PresetName = preset.Name;
                matchedCount++;
            } else {
                notMatchedCount++;
            }
        }

        return new TextureAutoDetectResult {
            MatchedCount = matchedCount,
            NotMatchedCount = notMatchedCount
        };
    }

    private static TextureConversion.Core.TextureType MapTextureTypeToCore(string textureType) {
        return textureType.ToLower(CultureInfo.InvariantCulture) switch {
            "albedo" => TextureConversion.Core.TextureType.Albedo,
            "normal" => TextureConversion.Core.TextureType.Normal,
            "roughness" => TextureConversion.Core.TextureType.Roughness,
            "metallic" => TextureConversion.Core.TextureType.Metallic,
            "ao" => TextureConversion.Core.TextureType.AmbientOcclusion,
            "emissive" => TextureConversion.Core.TextureType.Emissive,
            "gloss" => TextureConversion.Core.TextureType.Gloss,
            "height" => TextureConversion.Core.TextureType.Height,
            _ => TextureConversion.Core.TextureType.Generic
        };
    }

    private static (bool fileFound, long fileSize, string? actualPath) TryResolveOutputFile(string expectedPath, string extension) {
        if (File.Exists(expectedPath)) {
            var fileInfo = new FileInfo(expectedPath);
            fileInfo.Refresh();
            return (true, fileInfo.Length, expectedPath);
        }

        var directory = Path.GetDirectoryName(expectedPath);
        var sourceFileName = Path.GetFileNameWithoutExtension(expectedPath);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
            return (false, 0, null);
        }

        var allFiles = Directory.GetFiles(directory, $"*{extension}");
        foreach (var file in allFiles) {
            if (Path.GetFileNameWithoutExtension(file).Equals(sourceFileName, StringComparison.OrdinalIgnoreCase)) {
                var fileInfo = new FileInfo(file);
                fileInfo.Refresh();
                return (true, fileInfo.Length, file);
            }
        }

        return (false, 0, null);
    }

    private static string? GetExistingKtx2Path(string? sourcePath) {
        if (string.IsNullOrEmpty(sourcePath)) {
            return null;
        }

        sourcePath = SanitizePath(sourcePath);

        string? directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(directory)) {
            return null;
        }

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string normalizedBaseName = TextureResource.ExtractBaseTextureName(baseName);

        foreach (var extension in new[] { ".ktx2", ".ktx" }) {
            string directPath = Path.Combine(directory, baseName + extension);
            if (File.Exists(directPath)) {
                return directPath;
            }

            if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
                !normalizedBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
                string normalizedDirectPath = Path.Combine(directory, normalizedBaseName + extension);
                if (File.Exists(normalizedDirectPath)) {
                    return normalizedDirectPath;
                }
            }
        }

        string? sameDirectoryMatch = TryFindKtx2InDirectory(directory, baseName, normalizedBaseName, SearchOption.TopDirectoryOnly);
        if (!string.IsNullOrEmpty(sameDirectoryMatch)) {
            return sameDirectoryMatch;
        }

        string? defaultOutputRoot = ResolveDefaultKtxSearchRoot(directory);
        if (!string.IsNullOrEmpty(defaultOutputRoot)) {
            string? outputMatch = TryFindKtx2InDirectory(defaultOutputRoot, baseName, normalizedBaseName, SearchOption.AllDirectories);
            if (!string.IsNullOrEmpty(outputMatch)) {
                return outputMatch;
            }
        }

        return null;
    }

    private static string SanitizePath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        return path.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }

    private static string? TryFindKtx2InDirectory(string directory, string baseName, string normalizedBaseName, SearchOption searchOption) {
        if (!Directory.Exists(directory)) {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.ktx2", searchOption)) {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(fileNameWithoutExtension, baseName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileNameWithoutExtension, normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                return file;
            }
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.ktx", searchOption)) {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(fileNameWithoutExtension, baseName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileNameWithoutExtension, normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                return file;
            }
        }

        return null;
    }

    private static string? ResolveDefaultKtxSearchRoot(string directory) {
        if (string.IsNullOrEmpty(directory)) {
            return null;
        }

        var parentDirectory = Directory.GetParent(directory);
        if (parentDirectory == null) {
            return null;
        }

        var candidate = Path.Combine(parentDirectory.FullName, "ktx_output");
        return Directory.Exists(candidate) ? candidate : null;
    }
}

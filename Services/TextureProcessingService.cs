using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;
using AssetProcessor.Settings;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class TextureProcessingService : ITextureProcessingService {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TextureConversion.Settings.PresetManager CachedPresetManager = new();

    private readonly ITextureConversionPipelineFactory pipelineFactory;
    private readonly ILogService logService;

    public TextureProcessingService(ILogService logService)
        : this(new TextureConversionPipelineFactory(), logService) {
    }

    public TextureProcessingService(ITextureConversionPipelineFactory pipelineFactory, ILogService logService) {
        this.pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
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
                    logService.LogError($"Skipping texture with empty path: {texture.Name ?? "Unknown"}");
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

                logService.LogInfo("=== CONVERSION START ===");
                logService.LogInfo($"  Texture Name: {texture.Name}");
                logService.LogInfo($"  Source Path: {texture.Path}");
                logService.LogInfo($"  Output Path: {outputPath}");
                logService.LogInfo("========================");

                var saveSeparateMipmaps = request.SettingsProvider.SaveSeparateMipmaps;
                var mipmapOutputDir = saveSeparateMipmaps
                    ? Path.Combine(sourceDir, "mipmaps", sourceFileName)
                    : null;

                var toksvigSettings = request.SettingsProvider.GetToksvigSettings(texture.Path);

                var result = await Task.Run(
                    async () => await pipeline.ConvertTextureAsync(
                        texture.Path,
                        outputPath,
                        mipProfile,
                        compressionSettings,
                        toksvigSettings,
                        saveSeparateMipmaps,
                        mipmapOutputDir),
                    cancellationToken);

                if (!result.Success) {
                    texture.Status = "Error";
                    errorCount++;
                    var errorMsg = $"{texture.Name}: {result.Error ?? "Unknown error"}";
                    errorMessages.Add(errorMsg);
                    logService.LogError($"✗ Failed to convert {texture.Name}: {result.Error}");
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
                    var providerPreset = request.SettingsProvider.PresetName;
                    var matchedPreset = CachedPresetManager.FindPresetByFileName(texture.Name ?? string.Empty);
                    texture.PresetName = providerPreset ?? matchedPreset?.Name ?? "(Custom)";
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

                var (fileFound, fileSize, actualPath) = TryResolveOutputFile(outputPath, extension);

                if (fileFound && fileSize > 0) {
                    texture.CompressedSize = fileSize;
                    logService.LogInfo($"✓ Successfully converted {texture.Name}");
                    logService.LogInfo($"  Mipmaps: {result.MipLevels}, Size: {fileSize / 1024.0:F1} KB, Path: {actualPath}");
                } else {
                    logService.LogError("✗ OUTPUT FILE NOT FOUND OR EMPTY!");
                    logService.LogError($"  Expected: {outputPath}");
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
                logService.LogError($"✗ Exception processing {texture.Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
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

    private string? GetExistingKtx2Path(string? sourcePath) {
        return KtxPathResolver.FindExistingKtxPath(
            sourcePath,
            AppSettings.Default.ProjectsFolderPath,
            () => TextureConversionSettingsManager.LoadSettings(),
            logService);
    }
}

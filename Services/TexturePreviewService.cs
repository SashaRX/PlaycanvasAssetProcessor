using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public class TexturePreviewService : ITexturePreviewService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly List<KtxMipLevel> currentKtxMipmaps = new();
    private readonly Dictionary<string, BitmapImage> imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KtxPreviewCacheEntry> ktxPreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogService logService;
    private GlobalTextureConversionSettings? globalTextureSettings;

    public TexturePreviewService(ILogService logService) {
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public bool IsKtxPreviewActive { get; set; }
    public int CurrentMipLevel { get; set; }
    public bool IsUpdatingMipLevel { get; set; }
    public TexturePreviewSourceMode CurrentPreviewSourceMode { get; set; } = TexturePreviewSourceMode.Source;
    public bool IsSourcePreviewAvailable { get; set; }
    public bool IsKtxPreviewAvailable { get; set; }
    public bool IsUserPreviewSelection { get; set; }
    public bool IsUpdatingPreviewSourceControls { get; set; }
    public string? CurrentLoadedTexturePath { get; set; }
    public string? CurrentLoadedKtx2Path { get; set; }
    public TextureResource? CurrentSelectedTexture { get; set; }
    public string? CurrentActiveChannelMask { get; set; }
    public BitmapSource? OriginalFileBitmapSource { get; set; }
    public BitmapSource? OriginalBitmapSource { get; set; }
    public double PreviewReferenceWidth { get; set; }
    public double PreviewReferenceHeight { get; set; }
    public bool IsD3D11RenderLoopEnabled { get; set; } = true;
    public bool IsUsingD3D11Renderer { get; set; } = true;
    public IList<KtxMipLevel> CurrentKtxMipmaps => currentKtxMipmaps;

    public void ResetPreviewState() {
        IsKtxPreviewActive = false;
        CurrentMipLevel = 0;
        currentKtxMipmaps.Clear();
        OriginalBitmapSource = null;
        OriginalFileBitmapSource = null;
        CurrentPreviewSourceMode = TexturePreviewSourceMode.Source;
        IsSourcePreviewAvailable = false;
        IsKtxPreviewAvailable = false;
        IsUserPreviewSelection = false;
        PreviewReferenceWidth = 0;
        PreviewReferenceHeight = 0;
    }

    public async Task SwitchRendererAsync(TexturePreviewContext context, bool useD3D11) {
        ArgumentNullException.ThrowIfNull(context);

        if (useD3D11) {
            IsUsingD3D11Renderer = true;
            context.D3D11TextureViewer.Visibility = Visibility.Visible;
            context.WpfTexturePreviewImage.Visibility = Visibility.Collapsed;
            context.LogInfo("Switched to D3D11 preview renderer");

            if (IsKtxPreviewAvailable && CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                context.ShowMipmapControls();
            }

            if (CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2 && !string.IsNullOrEmpty(CurrentLoadedKtx2Path)) {
                if (context.IsKtx2Loading(CurrentLoadedKtx2Path)) {
                    context.LogInfo($"KTX2 file already loading, skipping reload in SwitchPreviewRenderer: {CurrentLoadedKtx2Path}");
                } else {
                    try {
                        await context.LoadKtx2ToD3D11ViewerAsync(CurrentLoadedKtx2Path);
                        context.LogInfo($"Reloaded KTX2 to D3D11 viewer: {CurrentLoadedKtx2Path}");
                    } catch (Exception ex) {
                        context.LogError(ex, "Failed to reload KTX2 to D3D11 viewer");
                    }
                }
            } else if (CurrentPreviewSourceMode == TexturePreviewSourceMode.Source && OriginalFileBitmapSource != null) {
                try {
                    bool isSRGB = context.IsSRGBTexture(CurrentSelectedTexture);
                    context.LoadTextureToD3D11Viewer(OriginalFileBitmapSource, isSRGB);
                    context.LogInfo($"Reloaded Source PNG to D3D11 viewer, sRGB={isSRGB}");
                } catch (Exception ex) {
                    context.LogError(ex, "Failed to reload Source PNG to D3D11 viewer");
                }
            }
        } else {
            IsUsingD3D11Renderer = false;
            context.D3D11TextureViewer.Visibility = Visibility.Collapsed;
            context.WpfTexturePreviewImage.Visibility = Visibility.Visible;
            context.LogInfo("Switched to WPF preview renderer");

            if (IsKtxPreviewAvailable) {
                context.ShowMipmapControls();
            }

            if (!string.IsNullOrEmpty(CurrentLoadedTexturePath) && File.Exists(CurrentLoadedTexturePath)) {
                try {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(CurrentLoadedTexturePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    context.WpfTexturePreviewImage.Source = context.PrepareForWpfDisplay(bitmap);
                    context.LogInfo($"Loaded source texture to WPF Image: {CurrentLoadedTexturePath}");
                } catch (Exception ex) {
                    context.LogError(ex, "Failed to load texture to WPF Image");
                    context.WpfTexturePreviewImage.Source = null;
                }
            } else {
                context.LogWarn("No source texture path available for WPF preview");
                context.WpfTexturePreviewImage.Source = null;
            }
        }
    }

    public BitmapImage? GetCachedImage(string texturePath) {
        ArgumentException.ThrowIfNullOrEmpty(texturePath);
        return imageCache.TryGetValue(texturePath, out BitmapImage? bitmapImage) ? bitmapImage : null;
    }

    public void CacheImage(string texturePath, BitmapImage bitmapImage) {
        ArgumentException.ThrowIfNullOrEmpty(texturePath);
        ArgumentNullException.ThrowIfNull(bitmapImage);

        if (!imageCache.ContainsKey(texturePath)) {
            imageCache[texturePath] = bitmapImage;

            if (imageCache.Count > 50) {
                string firstKey = imageCache.Keys.First();
                imageCache.Remove(firstKey);
            }
        }
    }

    public BitmapImage? LoadOptimizedImage(string path, int maxSize) {
        try {
            BitmapImage bitmapImage = new();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(path);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;

            using (var imageStream = new FileStream(path, FileMode.Open, FileAccess.Read)) {
                var decoder = BitmapDecoder.Create(imageStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                int width = decoder.Frames[0].PixelWidth;
                int height = decoder.Frames[0].PixelHeight;

                if (width > maxSize || height > maxSize) {
                    double scale = Math.Min((double)maxSize / width, (double)maxSize / height);
                    bitmapImage.DecodePixelWidth = (int)(width * scale);
                    bitmapImage.DecodePixelHeight = (int)(height * scale);
                }
            }

            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        } catch (Exception ex) {
            logService.LogError($"Error loading optimized image from {path}: {ex.Message}");
            return null;
        }
    }

    public string? GetExistingKtx2Path(string? sourcePath, string? projectFolderPath) {
        return KtxPathResolver.FindExistingKtxPath(
            sourcePath,
            projectFolderPath,
            () => globalTextureSettings ??= TextureConversionSettingsManager.LoadSettings(),
            logService);
    }

    public async Task<List<KtxMipLevel>> LoadKtx2MipmapsAsync(string ktxPath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo fileInfo = new(ktxPath);
        DateTime lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;

        if (ktxPreviewCache.TryGetValue(ktxPath, out KtxPreviewCacheEntry? cacheEntry) && cacheEntry.LastWriteTimeUtc == lastWriteTimeUtc) {
            // Only return cache hit if it has mipmaps (don't cache failures)
            if (cacheEntry.Mipmaps.Count > 0) {
                logService.LogInfo($"[LoadKtx2MipmapsAsync] Cache hit: {Path.GetFileName(ktxPath)}, {cacheEntry.Mipmaps.Count} mipmaps");
                return cacheEntry.Mipmaps;
            } else {
                logService.LogInfo($"[LoadKtx2MipmapsAsync] Cache has empty result, retrying extraction: {Path.GetFileName(ktxPath)}");
                ktxPreviewCache.Remove(ktxPath);
            }
        }

        return await ExtractKtxMipmapsAsync(ktxPath, lastWriteTimeUtc, cancellationToken);
    }

    private async Task<List<KtxMipLevel>> ExtractKtxMipmapsAsync(string ktxPath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        string ktxToolPath = GetKtxToolExecutablePath();
        string tempDirectory = Path.Combine(Path.GetTempPath(), "PlaycanvasAssetProcessor", "Preview", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);

        try {
            if (!string.IsNullOrEmpty(Path.GetDirectoryName(ktxToolPath)) && !File.Exists(ktxToolPath)) {
                throw new FileNotFoundException($"Не найден исполняемый файл ktx по пути '{ktxToolPath}'. Установите KTX-Software и настройте PATH.", ktxToolPath);
            }

            string outputBaseName = Path.Combine(tempDirectory, "mip");

            ProcessStartInfo startInfo = new() {
                FileName = ktxToolPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("extract");
            startInfo.ArgumentList.Add("--level");
            startInfo.ArgumentList.Add("all");
            startInfo.ArgumentList.Add("--transcode");
            startInfo.ArgumentList.Add("rgba8");
            startInfo.ArgumentList.Add(ktxPath);
            startInfo.ArgumentList.Add(outputBaseName);

            string commandLine = $"{ktxToolPath} {string.Join(" ", startInfo.ArgumentList.Select(arg => arg.Contains(' ') ? $"\"{arg}\"" : arg))}";
            logger.Info($"[KTX_EXTRACT] Executing: {commandLine}");
            logger.Info($"[KTX_EXTRACT] Input file exists: {File.Exists(ktxPath)}, size: {new FileInfo(ktxPath).Length} bytes");
            logService.LogInfo($"Executing command: {commandLine}");
            logService.LogInfo($"Working directory: {tempDirectory}");
            logService.LogInfo($"Input file exists: {File.Exists(ktxPath)}");
            logService.LogInfo($"Input file size: {new FileInfo(ktxPath).Length} bytes");
            logService.LogInfo($"Output base path: {outputBaseName}");
            logService.LogInfo($"Output directory exists: {Directory.Exists(tempDirectory)}");

            using Process process = new() { StartInfo = startInfo };
            try {
                if (!process.Start()) {
                    throw new InvalidOperationException("Не удалось запустить ktx для извлечения содержимого KTX2.");
                }
            } catch (Win32Exception ex) {
                throw new InvalidOperationException("Не удалось запустить ktx для извлечения содержимого KTX2. Установите KTX-Software и добавьте в PATH.", ex);
            } catch (Exception ex) {
                throw new InvalidOperationException("Не удалось запустить ktx для извлечения содержимого KTX2.", ex);
            }

            // Read stdout and stderr in parallel to prevent deadlock
            // (process can block if buffer fills before parent reads)
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string stdOutput = stdOutTask.Result;
            string stdError = stdErrTask.Result;

            if (process.ExitCode != 0) {
                logger.Warn($"[KTX_EXTRACT] Exit code: {process.ExitCode}, stderr: {stdError}");
                logService.LogWarn($"ktx exit code: {process.ExitCode}, stderr: {stdError}, stdout: {stdOutput}");
                throw new InvalidOperationException($"ktx exited with code {process.ExitCode}. Details logged.");
            }

            logger.Info($"[KTX_EXTRACT] Success. stdout: {stdOutput}");
            logService.LogInfo($"ktx extract completed successfully. stdout: {stdOutput}");
            if (!string.IsNullOrEmpty(stdError)) {
                logger.Info($"[KTX_EXTRACT] stderr (non-fatal): {stdError}");
                logService.LogInfo($"ktx extract stderr (non-fatal): {stdError}");
            }

            // List all files in temp directory to diagnose output format
            var filesInTempDir = Directory.GetFiles(tempDirectory, "*.png", SearchOption.AllDirectories);
            logger.Info($"[KTX_EXTRACT] PNG files created: {filesInTempDir.Length}");
            foreach (var file in filesInTempDir) {
                logger.Info($"[KTX_EXTRACT]   - {Path.GetFileName(file)}");
            }
            logService.LogInfo($"PNG files created ({filesInTempDir.Length} files):");
            foreach (var file in filesInTempDir) {
                logService.LogInfo($"  - {file}");
            }

            List<KtxMipLevel> mipmaps = new();

            // Parse PNG files from temp directory - ktx extract creates output_level{N}.png
            // Match pattern: *_level{N}.png or *.png (single level)
            var levelPattern = new System.Text.RegularExpressions.Regex(@"_level(\d+)\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var pngFile in filesInTempDir) {
                string fileName = Path.GetFileName(pngFile);
                var match = levelPattern.Match(fileName);

                if (match.Success) {
                    int level = int.Parse(match.Groups[1].Value);
                    logger.Info($"[KTX_EXTRACT] Found mipmap: {fileName} (level {level})");
                    logService.LogInfo($"Found mipmap file: {pngFile} (level {level})");
                    mipmaps.Add(CreateMipLevel(pngFile, level));
                }
            }

            // If no level-suffixed files found, try files without level suffix (single mip)
            if (mipmaps.Count == 0 && filesInTempDir.Length > 0) {
                // Take the first PNG file as level 0
                string singleFile = filesInTempDir[0];
                logger.Info($"[KTX_EXTRACT] No level pattern found, using single file: {Path.GetFileName(singleFile)}");
                logService.LogInfo($"Found single mipmap file (no level suffix): {singleFile}");
                mipmaps.Add(CreateMipLevel(singleFile, 0));
            }

            logger.Info($"[KTX_EXTRACT] Total mipmaps found: {mipmaps.Count}");
            logService.LogInfo($"Total mipmaps found: {mipmaps.Count}");

            mipmaps.Sort((a, b) => a.Level.CompareTo(b.Level));
            ktxPreviewCache[ktxPath] = new KtxPreviewCacheEntry {
                LastWriteTimeUtc = lastWriteTimeUtc,
                Mipmaps = mipmaps
            };

            return mipmaps;
        } finally {
            try {
                Directory.Delete(tempDirectory, true);
            } catch (Exception ex) {
                logService.LogDebug($"Не удалось удалить временный каталог: {tempDirectory}. {ex.Message}");
            }
        }
    }

    private static KtxMipLevel CreateMipLevel(string filePath, int level) {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(filePath);
        bitmap.EndInit();
        bitmap.Freeze();

        return new KtxMipLevel {
            Level = level,
            Bitmap = bitmap,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight
        };
    }

    private static string GetKtxToolExecutablePath() {
        var settings = TextureConversionSettingsManager.LoadSettings();
        return string.IsNullOrWhiteSpace(settings.KtxExecutablePath) ? "ktx" : settings.KtxExecutablePath;
    }
}

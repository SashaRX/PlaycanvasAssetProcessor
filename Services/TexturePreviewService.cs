using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.TextureViewer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public class TexturePreviewService : ITexturePreviewService {
    private readonly List<KtxMipLevel> currentKtxMipmaps = new();
    private readonly Dictionary<string, BitmapImage> imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KtxPreviewCacheEntry> ktxPreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex MipLevelRegex = new(@"(?:_level|_mip|_)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        if (string.IsNullOrEmpty(sourcePath)) {
            return null;
        }

        sourcePath = PathSanitizer.SanitizePath(sourcePath);

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

        string? parentDirectory = Directory.GetParent(directory)?.FullName;
        if (!string.IsNullOrEmpty(parentDirectory)) {
            string? parentMatch = TryFindKtx2InDirectory(parentDirectory, baseName, normalizedBaseName, SearchOption.TopDirectoryOnly);
            if (!string.IsNullOrEmpty(parentMatch)) {
                return parentMatch;
            }
        }

        string? projectDirectory = Directory.GetParent(directory)?.Parent?.FullName;
        if (!string.IsNullOrEmpty(projectDirectory)) {
            string? projectMatch = TryFindKtx2InDirectory(projectDirectory, baseName, normalizedBaseName, SearchOption.TopDirectoryOnly);
            if (!string.IsNullOrEmpty(projectMatch)) {
                return projectMatch;
            }
        }

        string? defaultOutputRoot = ResolveDefaultKtxSearchRoot(directory, projectFolderPath);
        if (!string.IsNullOrEmpty(defaultOutputRoot)) {
            string? outputMatch = TryFindKtx2InDirectory(defaultOutputRoot, baseName, normalizedBaseName, SearchOption.AllDirectories);
            if (!string.IsNullOrEmpty(outputMatch)) {
                return outputMatch;
            }
        }

        string? anyMatch = TryFindKtx2InDirectory(directory, baseName, normalizedBaseName, SearchOption.AllDirectories);
        if (!string.IsNullOrEmpty(anyMatch)) {
            return anyMatch;
        }

        string? normalizedMatch = TryFindNormalizedKtx(directory, normalizedBaseName);
        if (!string.IsNullOrEmpty(normalizedMatch)) {
            return normalizedMatch;
        }

        return null;
    }

    public async Task<List<KtxMipLevel>> LoadKtx2MipmapsAsync(string ktxPath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo fileInfo = new(ktxPath);
        DateTime lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;

        if (ktxPreviewCache.TryGetValue(ktxPath, out KtxPreviewCacheEntry? cacheEntry) && cacheEntry.LastWriteTimeUtc == lastWriteTimeUtc) {
            return cacheEntry.Mipmaps;
        }

        return await ExtractKtxMipmapsAsync(ktxPath, lastWriteTimeUtc, cancellationToken);
    }

    private string? TryFindKtx2InDirectory(string directory, string baseName, string normalizedBaseName, SearchOption searchOption) {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
            return null;
        }

        string? bestMatch = null;
        DateTime bestTime = DateTime.MinValue;
        int bestScore = -1;

        try {
            foreach (var pattern in new[] { "*.ktx2", "*.ktx" }) {
                foreach (string file in Directory.EnumerateFiles(directory, pattern, searchOption)) {
                    DateTime writeTime = File.GetLastWriteTimeUtc(file);

                    int score = GetKtxMatchScore(Path.GetFileNameWithoutExtension(file), baseName, normalizedBaseName);
                    if (score < 0) {
                        continue;
                    }

                    if (score > bestScore || (score == bestScore && writeTime > bestTime)) {
                        bestScore = score;
                        bestTime = writeTime;
                        bestMatch = file;
                    }
                }
            }
        } catch (UnauthorizedAccessException ex) {
            logService.LogDebug($"Нет доступа к каталогу {directory} при поиске KTX2: {ex.Message}");
            return null;
        } catch (DirectoryNotFoundException) {
            return null;
        } catch (IOException ex) {
            logService.LogDebug($"Ошибка при перечислении каталога {directory} для поиска KTX2: {ex.Message}");
            return null;
        }

        return bestMatch;
    }

    private static int GetKtxMatchScore(string candidateName, string baseName, string normalizedBaseName) {
        if (string.IsNullOrWhiteSpace(candidateName)) {
            return -1;
        }

        if (candidateName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
            return 500;
        }

        if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
            candidateName.Equals(normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
            return 450;
        }

        return -1;
    }

    private string? TryFindNormalizedKtx(string directory, string normalizedBaseName) {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(normalizedBaseName)) {
            return null;
        }

        try {
            foreach (string file in Directory.EnumerateFiles(directory)) {
                string name = Path.GetFileNameWithoutExtension(file);

                Match match = MipLevelRegex.Match(name);
                if (match.Success) {
                    name = name[..match.Index];
                }

                if (name.Equals(normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                    return file;
                }
            }
        } catch (IOException ex) {
            logService.LogDebug($"Ошибка при нормализованном поиске KTX2 в {directory}: {ex.Message}");
        }

        return null;
    }

    private string? ResolveDefaultKtxSearchRoot(string sourceDirectory, string? projectFolderPath) {
        try {
            globalTextureSettings ??= TextureConversionSettingsManager.LoadSettings();
        } catch (Exception ex) {
            logService.LogDebug($"Не удалось загрузить глобальные настройки текстур для поиска KTX2: {ex.Message}");
            return null;
        }

        string? configuredDirectory = globalTextureSettings?.DefaultOutputDirectory;
        if (string.IsNullOrWhiteSpace(configuredDirectory)) {
            return null;
        }

        List<string> candidates = new();

        if (Path.IsPathRooted(configuredDirectory)) {
            candidates.Add(configuredDirectory);
        } else {
            candidates.Add(Path.Combine(sourceDirectory, configuredDirectory));

            if (!string.IsNullOrEmpty(projectFolderPath)) {
                candidates.Add(Path.Combine(projectFolderPath!, configuredDirectory));
            }
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (Directory.Exists(candidate)) {
                return candidate;
            }
        }

        return null;
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

            string stdOutput = await process.StandardOutput.ReadToEndAsync();
            string stdError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0) {
                logService.LogWarn($"ktx exit code: {process.ExitCode}, stderr: {stdError}, stdout: {stdOutput}");
                throw new InvalidOperationException($"ktx exited with code {process.ExitCode}. Details logged.");
            }

            List<KtxMipLevel> mipmaps = new();
            int level = 0;
            while (true) {
                string pngPath = $"{outputBaseName}_level{level}.png";
                if (!File.Exists(pngPath)) {
                    break;
                }

                mipmaps.Add(CreateMipLevel(pngPath, level));
                level++;
            }

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

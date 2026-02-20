using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Settings;
using System;
using System.Collections.Generic;
using System.IO;

namespace AssetProcessor.Services;

internal static class KtxPathResolver {
    public static string? FindExistingKtxPath(
        string? sourcePath,
        string? projectFolderPath,
        Func<GlobalTextureConversionSettings?> settingsProvider,
        ILogService? logService) {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return null;
        }

        sourcePath = PathSanitizer.SanitizePath(sourcePath);
        if (string.IsNullOrEmpty(sourcePath)) {
            return null;
        }

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

        string? sameDirectoryMatch = TryFindKtx2InDirectory(directory, baseName, normalizedBaseName, SearchOption.TopDirectoryOnly, logService);
        if (!string.IsNullOrEmpty(sameDirectoryMatch)) {
            return sameDirectoryMatch;
        }

        string? defaultOutputRoot = ResolveDefaultKtxSearchRoot(directory, projectFolderPath, settingsProvider, logService);
        if (!string.IsNullOrEmpty(defaultOutputRoot)) {
            string? outputMatch = TryFindKtx2InDirectory(defaultOutputRoot, baseName, normalizedBaseName, SearchOption.AllDirectories, logService);
            if (!string.IsNullOrEmpty(outputMatch)) {
                return outputMatch;
            }
        }

        // Search in server/assets/content (model export pipeline output)
        if (!string.IsNullOrWhiteSpace(projectFolderPath)) {
            string sanitizedProject = PathSanitizer.SanitizePath(projectFolderPath);
            if (!string.IsNullOrEmpty(sanitizedProject)) {
                string serverContentPath = Path.Combine(sanitizedProject, "server", "assets", "content");
                string? serverMatch = TryFindKtx2InDirectory(serverContentPath, baseName, normalizedBaseName, SearchOption.AllDirectories, logService);
                if (!string.IsNullOrEmpty(serverMatch)) {
                    return serverMatch;
                }
            }
        }

        return null;
    }

    private static string? TryFindKtx2InDirectory(
        string directory,
        string baseName,
        string normalizedBaseName,
        SearchOption searchOption,
        ILogService? logService) {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
            return null;
        }

        try {
            foreach (string file in Directory.EnumerateFiles(directory, "*.ktx2", searchOption)) {
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                    return file;
                }
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.ktx", searchOption)) {
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                    return file;
                }
            }
        } catch (UnauthorizedAccessException ex) {
            logService?.LogDebug($"Нет доступа к каталогу {directory} при поиске KTX2: {ex.Message}");
        } catch (DirectoryNotFoundException) {
            // Каталог был удалён, игнорируем
        } catch (IOException ex) {
            logService?.LogDebug($"Ошибка при перечислении каталога {directory} для поиска KTX2: {ex.Message}");
        }

        return null;
    }

    private static string? ResolveDefaultKtxSearchRoot(
        string sourceDirectory,
        string? projectFolderPath,
        Func<GlobalTextureConversionSettings?> settingsProvider,
        ILogService? logService) {
        if (string.IsNullOrEmpty(sourceDirectory)) {
            return null;
        }

        GlobalTextureConversionSettings? settings = null;
        try {
            settings = settingsProvider();
        } catch (Exception ex) {
            logService?.LogDebug($"Не удалось загрузить глобальные настройки текстур для поиска KTX2: {ex.Message}");
        }

        string configuredDirectory = string.IsNullOrWhiteSpace(settings?.DefaultOutputDirectory)
            ? TextureConversionSettingsManager.CreateDefaultSettings().DefaultOutputDirectory
            : settings!.DefaultOutputDirectory!;

        if (string.IsNullOrWhiteSpace(configuredDirectory)) {
            return null;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Path.IsPathRooted(configuredDirectory)) {
            candidates.Add(configuredDirectory);
        } else {
            candidates.Add(Path.Combine(sourceDirectory, configuredDirectory));

            if (!string.IsNullOrWhiteSpace(projectFolderPath)) {
                string sanitizedProjectFolder = PathSanitizer.SanitizePath(projectFolderPath);
                if (!string.IsNullOrWhiteSpace(sanitizedProjectFolder)) {
                    candidates.Add(Path.Combine(sanitizedProjectFolder, configuredDirectory));
                }
            }
        }

        foreach (string candidate in candidates) {
            string? normalized = TryGetFullPath(candidate) ?? candidate;
            if (!string.IsNullOrEmpty(normalized) && Directory.Exists(normalized)) {
                return normalized;
            }
        }

        return null;
    }

    private static string? TryGetFullPath(string path) {
        try {
            return Path.GetFullPath(path);
        } catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return null;
        }
    }
}

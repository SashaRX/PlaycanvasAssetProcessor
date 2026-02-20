using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetProcessor.Upload;
using AssetProcessor.ViewModels;

namespace AssetProcessor.Services;

public sealed class AssetWorkflowCoordinator : IAssetWorkflowCoordinator {
    public string? ExtractRelativePathFromUrl(string? url) {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return uri.AbsolutePath.TrimStart('/');
            }

            return url.Replace('\\', '/').TrimStart('/');
        } catch {
            return url.Replace('\\', '/').TrimStart('/');
        }
    }

    public int ResetStatusesForDeletedPaths(IEnumerable<string> deletedPaths, IEnumerable<BaseResource> resources) {
        var normalizedPaths = deletedPaths
            .Select(p => p.Replace('\\', '/').ToLowerInvariant())
            .ToHashSet();

        return ResetStatusesByPathSet(normalizedPaths, resources);
    }

    public int ResetStatusesForDeletedCollections(IEnumerable<string> deletedPaths, params IEnumerable<BaseResource>[] resourceCollections) {
        return resourceCollections.Sum(resources => ResetStatusesForDeletedPaths(deletedPaths, resources));
    }

    public DeletedPathsSyncResult SyncDeletedPaths(IEnumerable<string>? deletedPaths, params IEnumerable<BaseResource>[] resourceCollections) {
        if (deletedPaths == null) {
            return new DeletedPathsSyncResult { HasDeletedPaths = false, DeletedPathCount = 0, ResetCount = 0 };
        }

        var paths = deletedPaths as ICollection<string> ?? deletedPaths.ToList();
        if (paths.Count == 0) {
            return new DeletedPathsSyncResult { HasDeletedPaths = false, DeletedPathCount = 0, ResetCount = 0 };
        }

        return new DeletedPathsSyncResult {
            HasDeletedPaths = true,
            DeletedPathCount = paths.Count,
            ResetCount = ResetStatusesForDeletedCollections(paths, resourceCollections)
        };
    }

    public int VerifyStatusesAgainstServerPaths(HashSet<string> serverPaths, IEnumerable<BaseResource> resources) {
        int resetCount = 0;

        foreach (var resource in resources) {
            if (resource.UploadStatus != "Uploaded" || string.IsNullOrEmpty(resource.RemoteUrl)) continue;

            var remotePath = ExtractRelativePathFromUrl(resource.RemoteUrl);
            if (remotePath != null && serverPaths.Contains(remotePath)) continue;

            ResetUploadStatus(resource);
            resetCount++;
        }

        return resetCount;
    }


    public int VerifyStatusesAgainstServerCollections(HashSet<string> serverPaths, params IEnumerable<BaseResource>[] resourceCollections) {
        return resourceCollections.Sum(resources => VerifyStatusesAgainstServerPaths(serverPaths, resources));
    }

    public async Task<ServerAssetDeleteResult> DeleteServerAssetAsync(
        ServerAssetViewModel asset,
        string keyId,
        string bucketName,
        string bucketId,
        Func<string?> getApplicationKey,
        Func<IB2UploadService> createB2Service,
        Func<Task> refreshServerAssetsAsync,
        Action<string>? onInfo = null,
        Action<string>? onError = null) {

        var appKey = getApplicationKey();
        if (string.IsNullOrWhiteSpace(appKey)) {
            const string message = "Failed to decrypt B2 application key.";
            onError?.Invoke(message);
            return new ServerAssetDeleteResult {
                Success = false,
                RequiresValidCredentials = true,
                ErrorMessage = message
            };
        }

        var settings = new B2UploadSettings {
            KeyId = keyId,
            ApplicationKey = appKey,
            BucketName = bucketName,
            BucketId = bucketId
        };

        var b2Service = createB2Service();
        try {
            var authorized = await b2Service.AuthorizeAsync(settings);
            if (!authorized) {
                var message = $"Failed to authorize B2 client for deleting: {asset.RemotePath}";
                onError?.Invoke(message);
                return new ServerAssetDeleteResult { Success = false, ErrorMessage = message };
            }

            var deleted = await b2Service.DeleteFileAsync(asset.RemotePath);
            if (!deleted) {
                var message = $"Failed to delete: {asset.RemotePath}";
                onError?.Invoke(message);
                return new ServerAssetDeleteResult { Success = false, ErrorMessage = message };
            }

            onInfo?.Invoke($"Deleted: {asset.RemotePath}");
            await refreshServerAssetsAsync();

            return new ServerAssetDeleteResult {
                Success = true,
                RefreshedAfterDelete = true
            };
        } finally {
            if (b2Service is IDisposable disposable) {
                disposable.Dispose();
            }
        }
    }


    public int ResetAllUploadStatuses(IEnumerable<BaseResource> resources) {
        int resetCount = 0;

        foreach (var resource in resources) {
            if (string.IsNullOrEmpty(resource.UploadStatus)) {
                continue;
            }

            ResetUploadStatus(resource);
            resetCount++;
        }

        return resetCount;
    }

    public int ResetAllUploadStatusesCollections(params IEnumerable<BaseResource>[] resourceCollections) {
        return resourceCollections.Sum(ResetAllUploadStatuses);
    }

    public ServerStatusSyncResult SyncStatusesWithServer(HashSet<string>? serverPaths, params IEnumerable<BaseResource>[] resourceCollections) {
        if (serverPaths == null || serverPaths.Count == 0) {
            return new ServerStatusSyncResult {
                ServerWasEmpty = true,
                ResetCount = ResetAllUploadStatusesCollections(resourceCollections)
            };
        }

        return new ServerStatusSyncResult {
            ServerWasEmpty = false,
            ResetCount = VerifyStatusesAgainstServerCollections(serverPaths, resourceCollections)
        };
    }

    public ResourceNavigationResult ResolveNavigationTarget(
        string fileName,
        IEnumerable<TextureResource> textures,
        IEnumerable<ModelResource> models,
        IEnumerable<MaterialResource> materials) {

        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        (string baseNameWithoutSuffix, bool isOrmFile) = ParseOrmSuffix(baseName);

        var texture = textures.FirstOrDefault(t => IsTextureMatch(t, baseName, baseNameWithoutSuffix, fileName));
        if (texture != null) {
            return new ResourceNavigationResult { Texture = texture, IsOrmFile = isOrmFile };
        }

        if (isOrmFile) {
            var textureInGroup = textures.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.GroupName) &&
                t.GroupName.Equals(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(t.SubGroupName));

            if (textureInGroup != null) {
                return new ResourceNavigationResult { OrmGroupTexture = textureInGroup, IsOrmFile = true };
            }
        }

        var model = models.FirstOrDefault(m =>
            m.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true ||
            (m.Path != null && m.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
        if (model != null) {
            return new ResourceNavigationResult { Model = model, IsOrmFile = isOrmFile };
        }

        var material = materials.FirstOrDefault(m =>
            m.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true ||
            (m.Path != null && m.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));

        return new ResourceNavigationResult { Material = material, IsOrmFile = isOrmFile };
    }

    private static bool IsTextureMatch(TextureResource texture, string baseName, string baseNameWithoutSuffix, string fileName) {
        if (texture.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true) return true;
        if (texture.Path != null && texture.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) return true;

        if (texture is not ORMTextureResource orm) return false;

        if (!string.IsNullOrEmpty(orm.SettingsKey)) {
            var settingsKeyBase = orm.SettingsKey.StartsWith("orm_", StringComparison.OrdinalIgnoreCase)
                ? orm.SettingsKey[4..]
                : orm.SettingsKey;
            if (baseName.Equals(orm.SettingsKey, StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals(settingsKeyBase, StringComparison.OrdinalIgnoreCase) ||
                baseNameWithoutSuffix.Equals(settingsKeyBase, StringComparison.OrdinalIgnoreCase) ||
                settingsKeyBase.Contains(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(orm.Path)) {
            var ormPathBaseName = System.IO.Path.GetFileNameWithoutExtension(orm.Path);
            if (baseName.Equals(ormPathBaseName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        var cleanName = texture.Name?.Replace("[ORM Texture - Not Packed]", "").Trim();
        if (!string.IsNullOrEmpty(cleanName) &&
            (cleanName.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
             cleanName.Equals(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase) ||
             baseNameWithoutSuffix.Contains(cleanName, StringComparison.OrdinalIgnoreCase) ||
             cleanName.Contains(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        if (orm.AOSource?.Name != null && baseNameWithoutSuffix.Contains(orm.AOSource.Name.Replace("_ao", "").Replace("_AO", ""), StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return orm.GlossSource?.Name != null &&
               baseNameWithoutSuffix.Contains(
                   orm.GlossSource.Name.Replace("_gloss", "").Replace("_Gloss", "").Replace("_roughness", "").Replace("_Roughness", ""),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static (string BaseNameWithoutSuffix, bool IsOrmFile) ParseOrmSuffix(string baseName) {
        if (baseName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) return (baseName[..^3], true);
        if (baseName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) return (baseName[..^4], true);
        if (baseName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) return (baseName[..^5], true);
        return (baseName, false);
    }

    private int ResetStatusesByPathSet(HashSet<string> paths, IEnumerable<BaseResource> resources) {
        int resetCount = 0;

        foreach (var resource in resources) {
            if (string.IsNullOrEmpty(resource.RemoteUrl)) continue;

            var remotePath = ExtractRelativePathFromUrl(resource.RemoteUrl);
            if (remotePath == null || !paths.Contains(remotePath.ToLowerInvariant())) continue;

            ResetUploadStatus(resource);
            resetCount++;
        }

        return resetCount;
    }

    private static void ResetUploadStatus(BaseResource resource) {
        resource.UploadStatus = null;
        resource.UploadedHash = null;
        resource.RemoteUrl = null;
        resource.LastUploadedAt = null;
    }
}

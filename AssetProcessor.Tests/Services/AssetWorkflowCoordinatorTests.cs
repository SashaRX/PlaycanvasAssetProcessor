using AssetProcessor.Resources;
using AssetProcessor.Services;
using Xunit;
using AssetProcessor.Upload;
using System.Collections.Generic;

namespace AssetProcessor.Tests.Services;

public class AssetWorkflowCoordinatorTests {
    [Fact]
    public void ResetStatusesForDeletedPaths_ResetsMatchingResource() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { RemoteUrl = "https://cdn/project/textures/a.ktx2", UploadStatus = "Uploaded", UploadedHash = "x" };

        var reset = sut.ResetStatusesForDeletedPaths(["project/textures/a.ktx2"], [texture]);

        Assert.Equal(1, reset);
        Assert.Null(texture.UploadStatus);
        Assert.Null(texture.RemoteUrl);
    }

    [Fact]
    public void VerifyStatusesAgainstServerPaths_ResetsMissingUploadedResources() {
        var sut = new AssetWorkflowCoordinator();
        var model = new ModelResource { RemoteUrl = "content/models/ship.glb", UploadStatus = "Uploaded" };

        var reset = sut.VerifyStatusesAgainstServerPaths(["content/models/tree.glb"], [model]);

        Assert.Equal(1, reset);
        Assert.Null(model.UploadStatus);
    }


    [Fact]
    public void ResetStatusesForDeletedCollections_SumsAcrossCollections() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { RemoteUrl = "https://cdn/project/textures/a.ktx2", UploadStatus = "Uploaded" };
        var model = new ModelResource { RemoteUrl = "https://cdn/project/models/a.glb", UploadStatus = "Uploaded" };

        var reset = sut.ResetStatusesForDeletedCollections(
            ["project/textures/a.ktx2", "project/models/a.glb"],
            [texture],
            [model]);

        Assert.Equal(2, reset);
        Assert.Null(texture.UploadStatus);
        Assert.Null(model.UploadStatus);
    }

    [Fact]
    public void VerifyStatusesAgainstServerCollections_SumsAcrossCollections() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { RemoteUrl = "content/textures/a.ktx2", UploadStatus = "Uploaded" };
        var model = new ModelResource { RemoteUrl = "content/models/a.glb", UploadStatus = "Uploaded" };

        var reset = sut.VerifyStatusesAgainstServerCollections(
            ["content/textures/a.ktx2"],
            [texture],
            [model]);

        Assert.Equal(1, reset);
        Assert.Equal("Uploaded", texture.UploadStatus);
        Assert.Null(model.UploadStatus);
    }


    [Fact]
    public void ResetAllUploadStatusesCollections_SumsAcrossCollections() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { UploadStatus = "Uploaded", RemoteUrl = "assets/a.ktx2" };
        var material = new MaterialResource { UploadStatus = "Uploaded", RemoteUrl = "assets/mat.json" };
        var model = new ModelResource { UploadStatus = null, RemoteUrl = "assets/model.glb" };

        var reset = sut.ResetAllUploadStatusesCollections([texture], [material], [model]);

        Assert.Equal(2, reset);
        Assert.Null(texture.UploadStatus);
        Assert.Null(material.UploadStatus);
        Assert.Null(model.UploadStatus);
    }

    [Fact]
    public void ResolveNavigationTarget_ReturnsTexture_WhenNameMatches() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { Name = "wall_diffuse" };

        var result = sut.ResolveNavigationTarget("wall_diffuse.png", [texture], [], []);

        Assert.Same(texture, result.Texture);
        Assert.False(result.IsOrmFile);
    }

    [Fact]
    public void ResolveNavigationTarget_ReturnsOrmGroupTexture_ForOrmSuffix() {
        var sut = new AssetWorkflowCoordinator();
        var groupedTexture = new TextureResource { GroupName = "oldMailbox", SubGroupName = "orm_oldMailbox" };

        var result = sut.ResolveNavigationTarget("oldMailbox_ogm.ktx2", [groupedTexture], [], []);

        Assert.True(result.IsOrmFile);
        Assert.Same(groupedTexture, result.OrmGroupTexture);
    }


    [Fact]
    public void ResetAllUploadStatuses_ResetsOnlyResourcesWithStatus() {
        var sut = new AssetWorkflowCoordinator();
        var uploaded = new TextureResource { UploadStatus = "Uploaded", UploadedHash = "abc", RemoteUrl = "assets/a.ktx2" };
        var notUploaded = new TextureResource { UploadStatus = null, UploadedHash = "keep", RemoteUrl = "assets/b.ktx2" };

        var reset = sut.ResetAllUploadStatuses([uploaded, notUploaded]);

        Assert.Equal(1, reset);
        Assert.Null(uploaded.UploadStatus);
        Assert.Null(uploaded.UploadedHash);
        Assert.Null(uploaded.RemoteUrl);
        Assert.Null(uploaded.LastUploadedAt);
        Assert.Null(notUploaded.UploadStatus);
        Assert.Equal("keep", notUploaded.UploadedHash);
        Assert.Equal("assets/b.ktx2", notUploaded.RemoteUrl);
    }


    [Fact]
    public void SyncStatusesWithServer_ReturnsResetAll_WhenServerEmpty() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { UploadStatus = "Uploaded", RemoteUrl = "assets/a.ktx2" };

        var result = sut.SyncStatusesWithServer(null, [texture]);

        Assert.True(result.ServerWasEmpty);
        Assert.Equal(1, result.ResetCount);
        Assert.Null(texture.UploadStatus);
    }

    [Fact]
    public void SyncStatusesWithServer_VerifiesStatuses_WhenServerHasPaths() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { UploadStatus = "Uploaded", RemoteUrl = "content/textures/a.ktx2" };
        var model = new ModelResource { UploadStatus = "Uploaded", RemoteUrl = "content/models/a.glb" };

        var result = sut.SyncStatusesWithServer(["content/textures/a.ktx2"], [texture], [model]);

        Assert.False(result.ServerWasEmpty);
        Assert.Equal(1, result.ResetCount);
        Assert.Equal("Uploaded", texture.UploadStatus);
        Assert.Null(model.UploadStatus);
    }

    [Fact]
    public async Task DeleteServerAssetAsync_ReturnsCredentialsError_WhenAppKeyMissing() {
        var sut = new AssetWorkflowCoordinator();
        var asset = new AssetProcessor.ViewModels.ServerAssetViewModel { RemotePath = "project/textures/a.ktx2" };

        var result = await sut.DeleteServerAssetAsync(
            asset,
            keyId: "id",
            bucketName: "bucket",
            bucketId: "bucket-id",
            getApplicationKey: () => null,
            createB2Service: () => new FakeB2UploadService(),
            refreshServerAssetsAsync: () => Task.CompletedTask);

        Assert.False(result.Success);
        Assert.True(result.RequiresValidCredentials);
    }

    [Fact]
    public async Task DeleteServerAssetAsync_DeletesAndRefreshes_WhenRequestSucceeds() {
        var sut = new AssetWorkflowCoordinator();
        var asset = new AssetProcessor.ViewModels.ServerAssetViewModel { RemotePath = "project/textures/a.ktx2" };
        var fakeB2 = new FakeB2UploadService { AuthorizeResult = true, DeleteResult = true };
        var refreshed = false;

        var result = await sut.DeleteServerAssetAsync(
            asset,
            keyId: "id",
            bucketName: "bucket",
            bucketId: "bucket-id",
            getApplicationKey: () => "secret",
            createB2Service: () => fakeB2,
            refreshServerAssetsAsync: () => {
                refreshed = true;
                return Task.CompletedTask;
            });

        Assert.True(result.Success);
        Assert.True(result.RefreshedAfterDelete);
        Assert.True(refreshed);
        Assert.Equal("project/textures/a.ktx2", fakeB2.DeletedRemotePath);
    }


    private sealed class FakeB2UploadService : IB2UploadService, IDisposable {
        public bool AuthorizeResult { get; set; }
        public bool DeleteResult { get; set; }
        public string? DeletedRemotePath { get; private set; }

        public B2UploadSettings? Settings { get; private set; }
        public bool IsAuthorized => AuthorizeResult;

        public Task<bool> AuthorizeAsync(B2UploadSettings settings, CancellationToken cancellationToken = default) {
            Settings = settings;
            return Task.FromResult(AuthorizeResult);
        }

        public Task<bool> DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default) {
            DeletedRemotePath = remotePath;
            return Task.FromResult(DeleteResult);
        }

        public void Dispose() { }

        public Task<B2UploadResult> UploadFileAsync(string localPath, string remotePath, string? contentType = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<B2BatchUploadResult> UploadBatchAsync(IEnumerable<(string LocalPath, string RemotePath)> files, IProgress<B2UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<B2BatchUploadResult> UploadDirectoryAsync(string localDirectory, string remotePrefix, string searchPattern = "*", bool recursive = true, IProgress<B2UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<B2FileInfo?> GetFileInfoAsync(string remotePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(int deleted, int failed)> DeleteFolderAsync(string folderPath, IProgress<(int current, int total, string fileName)>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<B2FileInfo>> ListFilesAsync(string prefix, int maxCount = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}

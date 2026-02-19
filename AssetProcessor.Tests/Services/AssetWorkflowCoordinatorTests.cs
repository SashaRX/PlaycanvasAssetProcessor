using AssetProcessor.Resources;
using AssetProcessor.Services;

namespace AssetProcessor.Tests.Services;

public class AssetWorkflowCoordinatorTests {
    [Fact]
    public void ResetStatusesForDeletedPaths_ResetsMatchingResource() {
        var sut = new AssetWorkflowCoordinator();
        var texture = new TextureResource { RemoteUrl = "https://cdn/site/project/textures/a.ktx2", UploadStatus = "Uploaded", UploadedHash = "x" };

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
}

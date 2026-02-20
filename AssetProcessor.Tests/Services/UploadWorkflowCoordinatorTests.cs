using AssetProcessor.Resources;
using AssetProcessor.Services;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class UploadWorkflowCoordinatorTests {

    [Fact]
    public void ValidateB2Configuration_ReturnsInvalid_WhenRequiredValuesMissing() {
        var sut = new UploadWorkflowCoordinator();

        var result = sut.ValidateB2Configuration(keyId: null, bucketName: "bucket");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateB2Configuration_ReturnsValid_WhenRequiredValuesProvided() {
        var sut = new UploadWorkflowCoordinator();

        var result = sut.ValidateB2Configuration(keyId: "id", bucketName: "bucket");

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CollectConvertedTextures_ReturnsOnlyExistingKtx2Files() {
        var sut = new UploadWorkflowCoordinator();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try {
            var src = Path.Combine(dir, "albedo.png");
            var ktx = Path.Combine(dir, "albedo.ktx2");
            File.WriteAllText(src, "x");
            File.WriteAllText(ktx, "x");

            var result = sut.CollectConvertedTextures([new TextureResource { Path = src }]);

            Assert.Single(result);
            Assert.Equal(ktx, result[0].Ktx2Path);
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildUploadFilePairs_StripsAssetsPrefix() {
        var sut = new UploadWorkflowCoordinator();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var serverPath = Path.Combine(dir, "server");
        var filePath = Path.Combine(serverPath, "assets", "content", "models", "ship.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "x");

        try {
            var result = sut.BuildUploadFilePairs([filePath], serverPath);

            Assert.Single(result);
            Assert.Equal("content/models/ship.glb", result[0].RemotePath);
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ApplyUploadStatuses_UpdatesMatchingResources() {
        var sut = new UploadWorkflowCoordinator();
        var resources = new List<ModelResource> {
            new() { ID = 10, Name = "ship" }
        };

        sut.ApplyUploadStatuses(new Dictionary<int, (string CdnUrl, string Hash)> {
            [10] = ("https://cdn/ship.glb", "sha1")
        }, resources);

        Assert.Equal("Uploaded", resources[0].UploadStatus);
        Assert.Equal("https://cdn/ship.glb", resources[0].RemoteUrl);
        Assert.Equal("sha1", resources[0].UploadedHash);
        Assert.NotNull(resources[0].LastUploadedAt);
    }
}

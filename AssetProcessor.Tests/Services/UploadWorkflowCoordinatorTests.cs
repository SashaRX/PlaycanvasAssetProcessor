using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Upload;
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
    public void BuildUploadResultMessage_IncludesTrimmedErrors() {
        var sut = new UploadWorkflowCoordinator();
        var result = new B2BatchUploadResult {
            SuccessCount = 2,
            SkippedCount = 1,
            FailedCount = 3,
            Duration = TimeSpan.FromSeconds(12.3),
            Errors = new List<string> { "e1", "e2", "e3" }
        };

        var message = sut.BuildUploadResultMessage(result, mappingUploaded: 1, maxErrorsToShow: 2);

        Assert.Contains("Uploaded: 3", message);
        Assert.Contains("mapping.json: uploaded", message);
        Assert.Contains("• e1", message);
        Assert.Contains("• e2", message);
        Assert.Contains("...and 1 more", message);
    }


    [Fact]
    public void ApplyAllUploadStatuses_UpdatesAllResourceCollections() {
        var sut = new UploadWorkflowCoordinator();
        var models = new List<ModelResource> { new() { ID = 1, Name = "m" } };
        var materials = new List<MaterialResource> { new() { ID = 2, Name = "mat" } };
        var textures = new List<TextureResource> { new() { ID = 3, Name = "tex" } };

        var updates = new UploadStatusUpdates();
        updates.Models[1] = ("https://cdn/m.glb", "h1");
        updates.Materials[2] = ("https://cdn/mat.json", "h2");
        updates.Textures[3] = ("https://cdn/tex.ktx2", "h3");

        sut.ApplyAllUploadStatuses(updates, models, materials, textures);

        Assert.Equal("Uploaded", models[0].UploadStatus);
        Assert.Equal("Uploaded", materials[0].UploadStatus);
        Assert.Equal("Uploaded", textures[0].UploadStatus);
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

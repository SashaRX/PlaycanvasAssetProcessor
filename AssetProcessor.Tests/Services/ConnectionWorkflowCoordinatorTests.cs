using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services;

namespace AssetProcessor.Tests.Services;

public class ConnectionWorkflowCoordinatorTests {
    [Fact]
    public async Task EvaluateRefreshAsync_ReturnsNeedsDownload_WhenUpdatesExist() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.EvaluateRefreshAsync(() => Task.FromResult(true), () => false);

        Assert.Equal(ConnectionState.NeedsDownload, result.State);
        Assert.True(result.HasUpdates);
        Assert.Equal("Updates available! Click Download to get them.", result.Message);
    }

    [Fact]
    public async Task EvaluateRefreshAsync_ReturnsNeedsDownloadWithMissingMessage_WhenOnlyMissingFiles() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.EvaluateRefreshAsync(() => Task.FromResult(false), () => true);

        Assert.Equal(ConnectionState.NeedsDownload, result.State);
        Assert.True(result.HasMissingFiles);
        Assert.Equal("Missing files found! Click Download to get them.", result.Message);
    }

    [Fact]
    public async Task EvaluatePostDownloadAsync_ReturnsUpToDate_WhenNoUpdatesOrMissingFiles() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.EvaluatePostDownloadAsync(() => Task.FromResult(false), () => false);

        Assert.Equal(ConnectionState.UpToDate, result.State);
        Assert.Equal("Project is up to date!", result.Message);
    }
}

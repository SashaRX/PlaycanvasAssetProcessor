using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services;

namespace AssetProcessor.Tests.Services;

public class ConnectionWorkflowCoordinatorTests {
    [Fact]
    public async Task DetermineRefreshStateAsync_ReturnsNeedsDownload_WhenUpdatesExist() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.DetermineRefreshStateAsync(() => Task.FromResult(true), () => false);

        Assert.Equal(ConnectionState.NeedsDownload, result);
    }

    [Fact]
    public async Task DeterminePostDownloadStateAsync_ReturnsUpToDate_WhenNoUpdatesOrMissingFiles() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.DeterminePostDownloadStateAsync(() => Task.FromResult(false), () => false);

        Assert.Equal(ConnectionState.UpToDate, result);
    }
}

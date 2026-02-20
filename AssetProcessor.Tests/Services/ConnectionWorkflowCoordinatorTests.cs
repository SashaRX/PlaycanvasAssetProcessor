using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services;
using System.Collections.Generic;
using Xunit;

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

    [Fact]
    public void EvaluateProjectState_ReturnsNeedsDownload_WhenAssetsListMissing() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = sut.EvaluateProjectState(
            hasProjectFolder: true,
            hasProjectName: true,
            assetsListExists: false,
            hasUpdates: false,
            hasMissingFiles: false);

        Assert.Equal(ConnectionState.NeedsDownload, result.State);
        Assert.True(result.HasMissingFiles);
    }

    [Fact]
    public void EvaluateProjectState_ReturnsUpToDate_WhenProjectReadyAndNoUpdates() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = sut.EvaluateProjectState(
            hasProjectFolder: true,
            hasProjectName: true,
            assetsListExists: true,
            hasUpdates: false,
            hasMissingFiles: false);

        Assert.Equal(ConnectionState.UpToDate, result.State);
        Assert.False(result.HasUpdates);
        Assert.False(result.HasMissingFiles);
    }

    [Fact]
    public void EvaluateProjectState_ReturnsNeedsDownload_WhenProjectReadyAndHasUpdates() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = sut.EvaluateProjectState(
            hasProjectFolder: true,
            hasProjectName: true,
            assetsListExists: true,
            hasUpdates: true,
            hasMissingFiles: false);

        Assert.Equal(ConnectionState.NeedsDownload, result.State);
        Assert.True(result.HasUpdates);
    }

    [Fact]
    public void EvaluateSmartLoadState_ReturnsDisconnected_WhenSelectionMissing() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = sut.EvaluateSmartLoadState(
            hasSelection: false,
            hasProjectPath: true,
            assetsLoaded: true,
            updatesCheckSucceeded: true,
            hasUpdates: false);

        Assert.Equal(ConnectionState.Disconnected, result);
    }

    [Fact]
    public void EvaluateSmartLoadState_ReturnsUpToDate_WhenAssetsLoadedAndNoUpdates() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = sut.EvaluateSmartLoadState(
            hasSelection: true,
            hasProjectPath: true,
            assetsLoaded: true,
            updatesCheckSucceeded: true,
            hasUpdates: false);

        Assert.Equal(ConnectionState.UpToDate, result);
    }

    [Fact]
    public void ResolveSelectedProjectId_ReturnsPreferred_WhenExistsInList() {
        var sut = new ConnectionWorkflowCoordinator();
        var projects = new List<KeyValuePair<string, string>> {
            new("p1", "Project 1"),
            new("p2", "Project 2")
        };

        var selected = sut.ResolveSelectedProjectId(projects, "p2");

        Assert.Equal("p2", selected);
    }

    [Fact]
    public void ResolveSelectedProjectId_ReturnsFirst_WhenPreferredMissing() {
        var sut = new ConnectionWorkflowCoordinator();
        var projects = new List<KeyValuePair<string, string>> {
            new("p1", "Project 1"),
            new("p2", "Project 2")
        };

        var selected = sut.ResolveSelectedProjectId(projects, "unknown");

        Assert.Equal("p1", selected);
    }
}

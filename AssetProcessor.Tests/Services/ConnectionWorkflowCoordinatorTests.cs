using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
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
        Assert.Equal("updates available", result.ProjectStateReason);
        Assert.Equal("Updates available! Click Download to get them.", result.Message);
    }

    [Fact]
    public async Task EvaluateRefreshAsync_ReturnsNeedsDownloadWithMissingMessage_WhenOnlyMissingFiles() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.EvaluateRefreshAsync(() => Task.FromResult(false), () => true);

        Assert.Equal(ConnectionState.NeedsDownload, result.State);
        Assert.True(result.HasMissingFiles);
        Assert.True(result.HasRequiredProjectData);
        Assert.Equal("missing files", result.ProjectStateReason);
        Assert.Equal("Missing files found! Click Download to get them.", result.Message);
    }


    [Fact]
    public async Task EvaluateRefreshAsync_ReturnsCombinedReason_WhenUpdatesAndMissingFiles() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = await sut.EvaluateRefreshAsync(() => Task.FromResult(true), () => true);

        Assert.Equal(ConnectionState.NeedsDownload, result.State);
        Assert.True(result.HasUpdates);
        Assert.True(result.HasMissingFiles);
        Assert.Equal("updates available and missing files", result.ProjectStateReason);
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
        Assert.False(result.HasRequiredProjectData);
        Assert.Equal("missing files or project is not downloaded", result.ProjectStateReason);
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
        Assert.Equal("up to date", result.ProjectStateReason);
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
        Assert.Equal("updates available", result.ProjectStateReason);
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
    public void SelectProject_ReturnsPreferred_WhenExistsInList() {
        var sut = new ConnectionWorkflowCoordinator();
        var projects = new List<KeyValuePair<string, string>> {
            new("p1", "Project 1"),
            new("p2", "Project 2")
        };

        var result = sut.SelectProject(projects, "p2");

        Assert.True(result.HasProjects);
        Assert.Equal("p2", result.SelectedProjectId);
    }

    [Fact]
    public void SelectProject_ReturnsEmpty_WhenProjectsMissing() {
        var sut = new ConnectionWorkflowCoordinator();
        var projects = new List<KeyValuePair<string, string>>();

        var result = sut.SelectProject(projects, "p2");

        Assert.False(result.HasProjects);
        Assert.Null(result.SelectedProjectId);
    }

    [Fact]
    public void SelectProject_ReturnsFirst_WhenPreferredMissing() {
        var sut = new ConnectionWorkflowCoordinator();
        var projects = new List<KeyValuePair<string, string>> {
            new("p1", "Project 1"),
            new("p2", "Project 2")
        };

        var result = sut.SelectProject(projects, "unknown");

        Assert.True(result.HasProjects);
        Assert.Equal("p1", result.SelectedProjectId);
    }

    [Fact]
    public void BuildProjectsBinding_ReturnsProjectsAndSelection() {
        var sut = new ConnectionWorkflowCoordinator();
        var projects = new List<KeyValuePair<string, string>> {
            new("p1", "Project 1"),
            new("p2", "Project 2")
        };

        var result = sut.BuildProjectsBinding(projects, "p2");

        Assert.True(result.HasProjects);
        Assert.Equal("p2", result.SelectedProjectId);
        Assert.Equal(2, result.Projects.Count);
    }

    [Fact]
    public void BuildProjectsBinding_ReturnsEmpty_WhenProjectsMissing() {
        var sut = new ConnectionWorkflowCoordinator();

        var result = sut.BuildProjectsBinding([], "p2");

        Assert.False(result.HasProjects);
        Assert.Null(result.SelectedProjectId);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public void ValidateProjectsLoad_ReturnsInvalid_WhenUserIdMissing() {
        var sut = new ConnectionWorkflowCoordinator();
        var projectsResult = new ProjectSelectionResult(new Dictionary<string, string>(), null, string.Empty, "user");

        var result = sut.ValidateProjectsLoad(projectsResult);

        Assert.False(result.IsValid);
        Assert.Equal("User ID is null or empty", result.ErrorMessage);
    }

    [Fact]
    public void ValidateProjectsLoad_ReturnsHasProjectsFalse_WhenProjectsEmpty() {
        var sut = new ConnectionWorkflowCoordinator();
        var projectsResult = new ProjectSelectionResult(new Dictionary<string, string>(), null, "u1", "user");

        var result = sut.ValidateProjectsLoad(projectsResult);

        Assert.True(result.IsValid);
        Assert.False(result.HasProjects);
        Assert.Equal("u1", result.UserId);
    }
}

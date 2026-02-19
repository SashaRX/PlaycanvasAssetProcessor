using AssetProcessor.Resources;
using AssetProcessor.Services;
using System.Collections.ObjectModel;
using System.Windows.Data;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class PreviewWorkflowCoordinatorTests {
    [Fact]
    public void ApplyTextureGrouping_AddsGroupDescriptions_WhenEnabled() {
        var sut = new PreviewWorkflowCoordinator();
        var data = new ObservableCollection<object>();
        var view = CollectionViewSource.GetDefaultView(data);

        sut.ApplyTextureGrouping(view, true);

        Assert.Equal(2, view.GroupDescriptions.Count);
    }

    [Fact]
    public async Task LoadOrmPreviewAsync_RequestsHistogram_WhenD3D11LoadSucceeded() {
        var sut = new PreviewWorkflowCoordinator();
        var texture = new ORMTextureResource { Name = "orm" };

        var result = await sut.LoadOrmPreviewAsync(
            texture,
            isUsingD3D11Renderer: true,
            tryLoadKtx2ToD3D11Async: (_, _) => Task.FromResult(true),
            tryLoadKtx2PreviewAsync: (_, _) => Task.FromResult(false),
            CancellationToken.None);

        Assert.True(result.Loaded);
        Assert.True(result.ShouldExtractHistogram);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task LoadOrmPreviewAsync_FallsBackToPng_WhenD3D11Failed() {
        var sut = new PreviewWorkflowCoordinator();
        var texture = new ORMTextureResource { Name = "orm" };

        var result = await sut.LoadOrmPreviewAsync(
            texture,
            isUsingD3D11Renderer: true,
            tryLoadKtx2ToD3D11Async: (_, _) => Task.FromResult(false),
            tryLoadKtx2PreviewAsync: (_, _) => Task.FromResult(true),
            CancellationToken.None);

        Assert.True(result.Loaded);
        Assert.False(result.ShouldExtractHistogram);
    }
}

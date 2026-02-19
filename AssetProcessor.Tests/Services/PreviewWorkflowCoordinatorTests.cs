using AssetProcessor.Resources;
using AssetProcessor.Services;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetProcessor.TextureViewer;
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


    [Fact]
    public async Task ExtractOrmHistogramAsync_InvokesCallback_WhenMipmapsAvailable() {
        var sut = new PreviewWorkflowCoordinator();
        var callbackCalled = false;

        var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);

        await sut.ExtractOrmHistogramAsync(
            ormPath: "orm.ktx2",
            ormName: "orm",
            loadKtx2MipmapsAsync: (_, _) => Task.FromResult(new List<KtxMipLevel> {
                new() { Level = 0, Width = 2, Height = 2, Bitmap = bitmap }
            }),
            onHistogramBitmapReady: bmp => callbackCalled = bmp == bitmap,
            cancellationToken: CancellationToken.None,
            startupDelay: TimeSpan.Zero,
            extractionTimeout: TimeSpan.FromSeconds(1));

        Assert.True(callbackCalled);
    }

    [Fact]
    public async Task ExtractOrmHistogramAsync_SkipsCallback_WhenPathEmpty() {
        var sut = new PreviewWorkflowCoordinator();
        var callbackCalled = false;

        await sut.ExtractOrmHistogramAsync(
            ormPath: null,
            ormName: "orm",
            loadKtx2MipmapsAsync: (_, _) => Task.FromResult(new List<KtxMipLevel>()),
            onHistogramBitmapReady: _ => callbackCalled = true,
            cancellationToken: CancellationToken.None,
            startupDelay: TimeSpan.Zero,
            extractionTimeout: TimeSpan.FromSeconds(1));

        Assert.False(callbackCalled);
    }

}
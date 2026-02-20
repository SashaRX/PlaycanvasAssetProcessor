using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
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
    public async Task LoadTexturePreviewAsync_D3D11Mode_LoadsSourceAndKeepsKtxFlag() {
        var sut = new PreviewWorkflowCoordinator();
        var sourceCalls = 0;

        var result = await sut.LoadTexturePreviewAsync(
            AssetProcessor.Services.Models.TexturePreviewSourceMode.Source,
            isUsingD3D11Renderer: true,
            tryLoadKtx2ToD3D11Async: _ => Task.FromResult(true),
            tryLoadKtx2PreviewAsync: _ => Task.FromResult(false),
            loadSourcePreviewAsync: (loadToViewer, _) => {
                sourceCalls++;
                Assert.True(loadToViewer);
                return Task.CompletedTask;
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.KtxLoaded);
        Assert.True(result.ShouldLoadSourcePreview);
        Assert.True(result.LoadSourceToViewer);
        Assert.Equal(1, sourceCalls);
    }

    [Fact]
    public async Task LoadTexturePreviewAsync_WpfMode_StartsKtxAndLoadsSource() {
        var sut = new PreviewWorkflowCoordinator();
        var ktxCallCount = 0;
        var sourceCalls = 0;

        var result = await sut.LoadTexturePreviewAsync(
            AssetProcessor.Services.Models.TexturePreviewSourceMode.Ktx2,
            isUsingD3D11Renderer: false,
            tryLoadKtx2ToD3D11Async: _ => Task.FromResult(false),
            tryLoadKtx2PreviewAsync: _ => {
                ktxCallCount++;
                return Task.FromResult(true);
            },
            loadSourcePreviewAsync: (loadToViewer, _) => {
                sourceCalls++;
                Assert.True(loadToViewer);
                return Task.CompletedTask;
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.KtxLoaded);
        Assert.True(result.ShouldLoadSourcePreview);
        Assert.True(result.LoadSourceToViewer);
        Assert.Equal(1, ktxCallCount);
        Assert.Equal(1, sourceCalls);
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
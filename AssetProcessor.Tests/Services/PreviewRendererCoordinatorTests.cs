using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureViewer;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class PreviewRendererCoordinatorTests {
    [Fact]
    public async Task SwitchRendererAsync_DelegatesToTexturePreviewService() {
        await StaTestRunner.Run(async () => {
            RecordingTexturePreviewService previewService = new();
            RecordingLogService logService = new();
            PreviewRendererCoordinator coordinator = new(previewService, logService);
            TexturePreviewContext context = CreateContext();

            await coordinator.SwitchRendererAsync(context, useD3D11: true);

            Assert.Same(context, previewService.LastContext);
            Assert.True(previewService.LastUseD3D11);
            Assert.Contains(logService.InfoMessages, message =>
                message.Contains("Switching preview renderer", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static TexturePreviewContext CreateContext() {
        return new TexturePreviewContext {
            D3D11TextureViewer = new DummyD3D11TextureViewerControl(),
            WpfTexturePreviewImage = new Image(),
            IsKtx2Loading = _ => false,
            LoadKtx2ToD3D11ViewerAsync = _ => Task.CompletedTask,
            IsSRGBTexture = _ => false,
            LoadTextureToD3D11Viewer = (_, _) => { },
            PrepareForWpfDisplay = bitmap => bitmap,
            ShowMipmapControls = () => { },
            LogInfo = _ => { },
            LogError = (_, _) => { },
            LogWarn = _ => { },
            ApplyWpfTiling = _ => { }
        };
    }

    private sealed class RecordingTexturePreviewService : ITexturePreviewService {
        public TexturePreviewContext? LastContext { get; private set; }
        public bool? LastUseD3D11 { get; private set; }
        public bool IsKtxPreviewActive { get; set; }
        public int CurrentMipLevel { get; set; }
        public bool IsUpdatingMipLevel { get; set; }
        public TexturePreviewSourceMode CurrentPreviewSourceMode { get; set; }
        public bool IsSourcePreviewAvailable { get; set; }
        public bool IsKtxPreviewAvailable { get; set; }
        public bool IsUserPreviewSelection { get; set; }
        public bool IsUpdatingPreviewSourceControls { get; set; }
        public string? CurrentLoadedTexturePath { get; set; }
        public string? CurrentLoadedKtx2Path { get; set; }
        public TextureResource? CurrentSelectedTexture { get; set; }
        public string? CurrentActiveChannelMask { get; set; }
        public BitmapSource? OriginalFileBitmapSource { get; set; }
        public BitmapSource? OriginalBitmapSource { get; set; }
        public double PreviewReferenceWidth { get; set; }
        public double PreviewReferenceHeight { get; set; }
        public bool IsD3D11RenderLoopEnabled { get; set; }
        public bool IsUsingD3D11Renderer { get; set; }
        public bool IsTilingEnabled { get; set; }
        public IList<KtxMipLevel> CurrentKtxMipmaps { get; } = new List<KtxMipLevel>();

        public void ResetPreviewState() {
        }

        public Task SwitchRendererAsync(TexturePreviewContext context, bool useD3D11) {
            LastContext = context;
            LastUseD3D11 = useD3D11;
            return Task.CompletedTask;
        }

        public BitmapImage? GetCachedImage(string texturePath) => null;
        public void CacheImage(string texturePath, BitmapImage bitmapImage) {
        }

        public BitmapImage? LoadOptimizedImage(string path, int maxSize) => null;

        public string? GetExistingKtx2Path(string? sourcePath, string? projectFolderPath) => null;

        public Task<List<KtxMipLevel>> LoadKtx2MipmapsAsync(string ktxPath, CancellationToken cancellationToken) =>
            Task.FromResult(new List<KtxMipLevel>());
    }

    private sealed class RecordingLogService : ILogService {
        public List<string> InfoMessages { get; } = new();

        public void LogDebug(string message) {
        }

        public void LogError(string? message) {
        }

        public void LogInfo(string message) => InfoMessages.Add(message);

        public void LogWarn(string message) {
        }
    }

    private sealed class DummyD3D11TextureViewerControl : D3D11TextureViewerControl {
    }

    private static class StaTestRunner {
        public static Task Run(Func<Task> action) {
            TaskCompletionSource<object?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Thread thread = new(() => {
                try {
                    action().GetAwaiter().GetResult();
                    tcs.SetResult(null);
                } catch (Exception ex) {
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }
    }
}

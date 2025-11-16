using AssetProcessor.Resources;
using AssetProcessor.TextureViewer;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services.Models;

public sealed class TexturePreviewContext {
    public required D3D11TextureViewerControl D3D11TextureViewer { get; init; }
    public required Image WpfTexturePreviewImage { get; init; }
    public required Func<string, bool> IsKtx2Loading { get; init; }
    public required Func<string, Task> LoadKtx2ToD3D11ViewerAsync { get; init; }
    public required Func<TextureResource?, bool> IsSRGBTexture { get; init; }
    public required Action<BitmapSource, bool> LoadTextureToD3D11Viewer { get; init; }
    public required Func<BitmapSource, BitmapSource> PrepareForWpfDisplay { get; init; }
    public required Action ShowMipmapControls { get; init; }
    public required Action<string> LogInfo { get; init; }
    public required Action<Exception, string> LogError { get; init; }
    public required Action<string> LogWarn { get; init; }
}

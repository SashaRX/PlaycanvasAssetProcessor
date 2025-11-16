using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureViewer;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public interface ITexturePreviewService {
    bool IsKtxPreviewActive { get; set; }
    int CurrentMipLevel { get; set; }
    bool IsUpdatingMipLevel { get; set; }
    TexturePreviewSourceMode CurrentPreviewSourceMode { get; set; }
    bool IsSourcePreviewAvailable { get; set; }
    bool IsKtxPreviewAvailable { get; set; }
    bool IsUserPreviewSelection { get; set; }
    bool IsUpdatingPreviewSourceControls { get; set; }
    string? CurrentLoadedTexturePath { get; set; }
    string? CurrentLoadedKtx2Path { get; set; }
    TextureResource? CurrentSelectedTexture { get; set; }
    string? CurrentActiveChannelMask { get; set; }
    BitmapSource? OriginalFileBitmapSource { get; set; }
    BitmapSource? OriginalBitmapSource { get; set; }
    double PreviewReferenceWidth { get; set; }
    double PreviewReferenceHeight { get; set; }
    bool IsD3D11RenderLoopEnabled { get; set; }
    bool IsUsingD3D11Renderer { get; set; }
    IList<KtxMipLevel> CurrentKtxMipmaps { get; }

    void ResetPreviewState();
    Task SwitchRendererAsync(TexturePreviewContext context, bool useD3D11);
}

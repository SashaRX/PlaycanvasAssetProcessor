using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureViewer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public class TexturePreviewService : ITexturePreviewService {
    private readonly List<KtxMipLevel> currentKtxMipmaps = new();
    public bool IsKtxPreviewActive { get; set; }
    public int CurrentMipLevel { get; set; }
    public bool IsUpdatingMipLevel { get; set; }
    public TexturePreviewSourceMode CurrentPreviewSourceMode { get; set; } = TexturePreviewSourceMode.Source;
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
    public bool IsD3D11RenderLoopEnabled { get; set; } = true;
    public bool IsUsingD3D11Renderer { get; set; } = true;
    public IList<KtxMipLevel> CurrentKtxMipmaps => currentKtxMipmaps;

    public void ResetPreviewState() {
        IsKtxPreviewActive = false;
        CurrentMipLevel = 0;
        currentKtxMipmaps.Clear();
        OriginalBitmapSource = null;
        OriginalFileBitmapSource = null;
        CurrentPreviewSourceMode = TexturePreviewSourceMode.Source;
        IsSourcePreviewAvailable = false;
        IsKtxPreviewAvailable = false;
        IsUserPreviewSelection = false;
        PreviewReferenceWidth = 0;
        PreviewReferenceHeight = 0;
    }

    public async Task SwitchRendererAsync(TexturePreviewContext context, bool useD3D11) {
        ArgumentNullException.ThrowIfNull(context);

        if (useD3D11) {
            IsUsingD3D11Renderer = true;
            context.D3D11TextureViewer.Visibility = Visibility.Visible;
            context.WpfTexturePreviewImage.Visibility = Visibility.Collapsed;
            context.LogInfo("Switched to D3D11 preview renderer");

            if (IsKtxPreviewAvailable && CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                context.ShowMipmapControls();
            }

            if (CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2 && !string.IsNullOrEmpty(CurrentLoadedKtx2Path)) {
                if (context.IsKtx2Loading(CurrentLoadedKtx2Path)) {
                    context.LogInfo($"KTX2 file already loading, skipping reload in SwitchPreviewRenderer: {CurrentLoadedKtx2Path}");
                } else {
                    try {
                        await context.LoadKtx2ToD3D11ViewerAsync(CurrentLoadedKtx2Path);
                        context.LogInfo($"Reloaded KTX2 to D3D11 viewer: {CurrentLoadedKtx2Path}");
                    } catch (Exception ex) {
                        context.LogError(ex, "Failed to reload KTX2 to D3D11 viewer");
                    }
                }
            } else if (CurrentPreviewSourceMode == TexturePreviewSourceMode.Source && OriginalFileBitmapSource != null) {
                try {
                    bool isSRGB = context.IsSRGBTexture(CurrentSelectedTexture);
                    context.LoadTextureToD3D11Viewer(OriginalFileBitmapSource, isSRGB);
                    context.LogInfo($"Reloaded Source PNG to D3D11 viewer, sRGB={isSRGB}");
                } catch (Exception ex) {
                    context.LogError(ex, "Failed to reload Source PNG to D3D11 viewer");
                }
            }
        } else {
            IsUsingD3D11Renderer = false;
            context.D3D11TextureViewer.Visibility = Visibility.Collapsed;
            context.WpfTexturePreviewImage.Visibility = Visibility.Visible;
            context.LogInfo("Switched to WPF preview renderer");

            if (IsKtxPreviewAvailable) {
                context.ShowMipmapControls();
            }

            if (!string.IsNullOrEmpty(CurrentLoadedTexturePath) && File.Exists(CurrentLoadedTexturePath)) {
                try {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(CurrentLoadedTexturePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    context.WpfTexturePreviewImage.Source = context.PrepareForWpfDisplay(bitmap);
                    context.LogInfo($"Loaded source texture to WPF Image: {CurrentLoadedTexturePath}");
                } catch (Exception ex) {
                    context.LogError(ex, "Failed to load texture to WPF Image");
                    context.WpfTexturePreviewImage.Source = null;
                }
            } else {
                context.LogWarn("No source texture path available for WPF preview");
                context.WpfTexturePreviewImage.Source = null;
            }
        }
    }
}

namespace AssetProcessor.Services.Models;

public sealed class PreviewTextureLoadResult {
    public bool KtxLoaded { get; init; }
    public bool ShouldLoadSourcePreview { get; init; }
    public bool LoadSourceToViewer { get; init; }
}

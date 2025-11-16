using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace AssetProcessor.TextureViewer;

internal sealed class KtxPreviewCacheEntry {
    public required DateTime LastWriteTimeUtc { get; init; }

    public required List<KtxMipLevel> Mipmaps { get; init; }
}

public sealed class KtxMipLevel {
    public required int Level { get; init; }

    public required BitmapSource Bitmap { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}

using AssetProcessor.Resources;

namespace AssetProcessor.Services;

/// <summary>
/// Service for reading KTX2 file metadata (compression format, mipmap count, file size).
/// </summary>
public interface IKtx2InfoService {
    /// <summary>
    /// Scans a texture's KTX2 file and returns metadata if found.
    /// </summary>
    /// <param name="texturePath">Path to the source texture file</param>
    /// <returns>KTX2 info if file exists, null otherwise</returns>
    Ktx2Info? GetKtx2Info(string? texturePath);

    /// <summary>
    /// Scans multiple textures for KTX2 info in parallel.
    /// Returns only textures that have KTX2 files.
    /// </summary>
    IReadOnlyList<Ktx2ScanResult> ScanTextures(IEnumerable<TextureResource> textures);
}

/// <summary>
/// KTX2 file metadata.
/// </summary>
public sealed record Ktx2Info(
    long FileSize,
    int MipmapCount,
    string? CompressionFormat
);

/// <summary>
/// Result of scanning a texture for KTX2 info.
/// </summary>
public sealed record Ktx2ScanResult(
    TextureResource Texture,
    Ktx2Info Info
);

using System;
using System.Collections.Generic;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// Represents decoded texture data ready for GPU upload.
/// All data is in RGBA8 format (4 bytes per pixel).
/// </summary>
public sealed class TextureData : IDisposable {
    /// <summary>
    /// Width of the base mip level (mip 0) in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of the base mip level (mip 0) in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Number of mipmap levels (including base level).
    /// Minimum is 1 (just the base level).
    /// </summary>
    public int MipCount => MipLevels.Count;

    /// <summary>
    /// List of mipmap levels. MipLevels[0] is the base level (full resolution).
    /// Each subsequent level is half the size (width/2, height/2).
    /// </summary>
    public required List<MipLevel> MipLevels { get; init; }

    /// <summary>
    /// Whether the texture data is in sRGB color space.
    /// If true, GPU should use sRGB texture format or shader should apply gamma correction.
    /// </summary>
    public bool IsSRGB { get; init; }

    /// <summary>
    /// Whether the texture has an alpha channel with meaningful data.
    /// </summary>
    public bool HasAlpha { get; init; }

    /// <summary>
    /// Source format name (e.g., "PNG", "KTX2/ETC1S", "KTX2/UASTC").
    /// </summary>
    public required string SourceFormat { get; init; }

    /// <summary>
    /// Whether this is an HDR texture (requires special handling for exposure/tonemapping).
    /// </summary>
    public bool IsHDR { get; init; }

    public void Dispose() {
        foreach (var mip in MipLevels) {
            mip.Dispose();
        }
        MipLevels.Clear();
    }
}

/// <summary>
/// Represents a single mipmap level.
/// </summary>
public sealed class MipLevel : IDisposable {
    /// <summary>
    /// Mip level index (0 = base level, 1 = first mip, etc.).
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Width of this mip level in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of this mip level in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Raw RGBA8 pixel data. Length = Width * Height * 4.
    /// Format: R, G, B, A (one byte per channel).
    /// Row-major order (top to bottom, left to right).
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// Row pitch in bytes (Width * 4 for RGBA8).
    /// </summary>
    public int RowPitch => Width * 4;

    public void Dispose() {
        // Data will be GC'd, but we can explicitly clear if needed
        Array.Clear(Data, 0, Data.Length);
    }
}

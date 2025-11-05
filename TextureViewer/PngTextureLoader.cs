using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// Loads PNG/JPEG/BMP textures via ImageSharp and converts to RGBA8 format.
/// </summary>
public static class PngTextureLoader {
    /// <summary>
    /// Load a texture from a file path.
    /// </summary>
    public static TextureData LoadFromFile(string filePath) {
        using var fileStream = File.OpenRead(filePath);
        return LoadFromStream(fileStream, Path.GetExtension(filePath));
    }

    /// <summary>
    /// Load a texture from a stream.
    /// </summary>
    public static TextureData LoadFromStream(Stream stream, string extension) {
        // Load image via ImageSharp
        using var image = Image.Load<Rgba32>(stream);

        int width = image.Width;
        int height = image.Height;

        // Extract pixel data
        var mipLevel = new MipLevel {
            Level = 0,
            Width = width,
            Height = height,
            Data = new byte[width * height * 4],
            RowPitch = width * 4
        };

        // Copy pixels from ImageSharp to our buffer (row by row)
        image.ProcessPixelRows(accessor => {
            for (int y = 0; y < height; y++) {
                var row = accessor.GetRowSpan(y);
                int rowOffset = y * width * 4;

                for (int x = 0; x < width; x++) {
                    var pixel = row[x];
                    int pixelOffset = rowOffset + x * 4;

                    mipLevel.Data[pixelOffset + 0] = pixel.R;
                    mipLevel.Data[pixelOffset + 1] = pixel.G;
                    mipLevel.Data[pixelOffset + 2] = pixel.B;
                    mipLevel.Data[pixelOffset + 3] = pixel.A;
                }
            }
        });

        // Check if image has meaningful alpha
        bool hasAlpha = HasMeaningfulAlpha(mipLevel.Data);

        // Determine if sRGB based on extension (PNG/JPEG are typically sRGB)
        bool isSRGB = extension.ToLowerInvariant() switch {
            ".png" => true,
            ".jpg" => true,
            ".jpeg" => true,
            ".bmp" => true,
            _ => true // Default to sRGB for most formats
        };

        return new TextureData {
            Width = width,
            Height = height,
            MipLevels = new List<MipLevel> { mipLevel },
            IsSRGB = isSRGB,
            HasAlpha = hasAlpha,
            SourceFormat = $"ImageSharp/{extension.TrimStart('.')}",
            IsHDR = false
        };
    }

    /// <summary>
    /// Check if texture has meaningful alpha channel (not all 255).
    /// </summary>
    private static bool HasMeaningfulAlpha(byte[] rgbaData) {
        // Sample every 16th pixel for performance
        for (int i = 3; i < rgbaData.Length; i += 64) { // Every 16th pixel (16 * 4 = 64)
            if (rgbaData[i] != 255) {
                return true;
            }
        }
        return false;
    }
}

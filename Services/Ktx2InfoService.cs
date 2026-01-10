using System.IO;
using AssetProcessor.Resources;
using NLog;

namespace AssetProcessor.Services;

/// <summary>
/// Service for reading KTX2 file metadata.
/// </summary>
public sealed class Ktx2InfoService : IKtx2InfoService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    /// <inheritdoc />
    public Ktx2Info? GetKtx2Info(string? texturePath) {
        if (string.IsNullOrEmpty(texturePath)) {
            return null;
        }

        var sourceDir = Path.GetDirectoryName(texturePath);
        var sourceFileName = Path.GetFileNameWithoutExtension(texturePath);

        if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(sourceFileName)) {
            return null;
        }

        var ktx2Path = Path.Combine(sourceDir, sourceFileName + ".ktx2");
        if (!File.Exists(ktx2Path)) {
            return null;
        }

        try {
            var fileInfo = new FileInfo(ktx2Path);
            int mipLevels = 0;
            string? compressionFormat = null;

            try {
                using var stream = File.OpenRead(ktx2Path);
                using var reader = new BinaryReader(stream);

                // KTX2 header structure:
                // Bytes 12-15: vkFormat (uint32) - 0 means Basis Universal
                // Bytes 40-43: levelCount (uint32)
                // Bytes 44-47: supercompressionScheme (uint32)
                reader.BaseStream.Seek(12, SeekOrigin.Begin);
                uint vkFormat = reader.ReadUInt32();

                reader.BaseStream.Seek(40, SeekOrigin.Begin);
                mipLevels = (int)reader.ReadUInt32();
                uint supercompression = reader.ReadUInt32();

                // Only set compression format for Basis Universal textures (vkFormat = 0)
                if (vkFormat == 0) {
                    // supercompressionScheme: 1=BasisLZ(ETC1S), 0/2=UASTC(None/Zstd)
                    compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
                }
                // vkFormat != 0 means raw texture format, no Basis compression
            } catch (Exception ex) {
                // Ignore header read errors, still return file size
                logger.Debug(ex, $"Failed to read KTX2 header: {ktx2Path}");
            }

            return new Ktx2Info(fileInfo.Length, mipLevels, compressionFormat);
        } catch (Exception ex) {
            logger.Debug(ex, $"Failed to get KTX2 info: {ktx2Path}");
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Ktx2ScanResult> ScanTextures(IEnumerable<TextureResource> textures) {
        var results = new List<Ktx2ScanResult>();

        var texturesToScan = textures
            .Where(t => !string.IsNullOrEmpty(t.Path) &&
                        t.CompressedSize == 0 &&
                        t is not ORMTextureResource)
            .ToList();

        if (texturesToScan.Count == 0) {
            return results;
        }

        logger.Info($"Ktx2InfoService: Scanning {texturesToScan.Count} textures for KTX2 info");

        foreach (var texture in texturesToScan) {
            var info = GetKtx2Info(texture.Path);
            if (info != null) {
                results.Add(new Ktx2ScanResult(texture, info));
            }
        }

        if (results.Count > 0) {
            logger.Info($"Ktx2InfoService: Found KTX2 info for {results.Count} textures");
        }

        return results;
    }
}

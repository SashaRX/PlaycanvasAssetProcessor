using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NLog;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// Reader for KTX2 Key-Value Data with TLV metadata parsing for histogram preprocessing.
/// </summary>
public static class Ktx2MetadataReader {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    // KTX2 file identifier (12 bytes)
    private static readonly byte[] KTX2_IDENTIFIER = new byte[] {
        0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A
    };

    /// <summary>
    /// Reads all metadata (histogram + normal layout) directly from KTX2 file.
    /// Returns a tuple (histogram, normalLayout) where either can be null.
    /// </summary>
    public static (HistogramMetadata? histogram, NormalLayoutMetadata? normalLayout) ReadAllMetadata(string filePath) {
        try {
            logger.Info($"[Ktx2MetadataReader] Reading metadata from file: {filePath}");

            if (!File.Exists(filePath)) {
                logger.Warn($"File not found: {filePath}");
                return (null, null);
            }

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fileStream);

            // Read and verify KTX2 identifier
            byte[] identifier = reader.ReadBytes(12);
            if (!identifier.SequenceEqual(KTX2_IDENTIFIER)) {
                logger.Warn("Not a valid KTX2 file (identifier mismatch)");
                return (null, null);
            }

            logger.Info("[Ktx2MetadataReader] KTX2 identifier verified");

            // Skip to KVD section
            fileStream.Seek(56, SeekOrigin.Begin);
            uint kvdByteOffset = reader.ReadUInt32();
            uint kvdByteLength = reader.ReadUInt32();

            logger.Info($"[Ktx2MetadataReader] KVD: offset={kvdByteOffset}, length={kvdByteLength}");

            if (kvdByteLength == 0) {
                logger.Info("[Ktx2MetadataReader] No Key-Value Data in this KTX2 file");
                return (null, null);
            }

            // Read KVD section
            fileStream.Seek(kvdByteOffset, SeekOrigin.Begin);
            byte[] kvdData = reader.ReadBytes((int)kvdByteLength);
            logger.Info($"[Ktx2MetadataReader] Read {kvdData.Length} bytes of KVD data");

            // Parse both metadata types
            var (histogram, normalLayout) = ParseKeyValueDataComplete(kvdData);

            if (histogram != null && histogram.Scale[0] > 1.0f) {
                logger.Error($"INVALID histogram format detected: scale={histogram.Scale[0]:F4} > 1.0");
                logger.Error("This file uses old/broken format. Please re-convert the texture with current version.");
                histogram = null;
            }

            return (histogram, normalLayout);
        } catch (Exception ex) {
            logger.Error(ex, "Failed to read metadata from KTX2 file");
            return (null, null);
        }
    }

    /// <summary>
    /// Reads all metadata from in-memory KTX2 data.
    /// Returns a tuple (histogram, normalLayout) where either can be null.
    /// </summary>
    public static (HistogramMetadata? histogram, NormalLayoutMetadata? normalLayout) ReadAllMetadataFromMemory(byte[] fileData) {
        try {
            if (fileData == null || fileData.Length < 80) {
                logger.Warn("Invalid or too small KTX2 data");
                return (null, null);
            }

            using var memStream = new MemoryStream(fileData);
            using var reader = new BinaryReader(memStream);

            // Read and verify KTX2 identifier
            byte[] identifier = reader.ReadBytes(12);
            if (!identifier.SequenceEqual(KTX2_IDENTIFIER)) {
                logger.Warn("Not a valid KTX2 data (identifier mismatch)");
                return (null, null);
            }

            // Skip to KVD section (offset 56)
            memStream.Seek(56, SeekOrigin.Begin);
            uint kvdByteOffset = reader.ReadUInt32();
            uint kvdByteLength = reader.ReadUInt32();

            if (kvdByteLength == 0) {
                return (null, null);
            }

            // Read KVD section
            memStream.Seek(kvdByteOffset, SeekOrigin.Begin);
            byte[] kvdData = reader.ReadBytes((int)kvdByteLength);

            // Parse both metadata types
            var (histogram, normalLayout) = ParseKeyValueDataComplete(kvdData);

            if (histogram != null && histogram.Scale[0] > 1.0f) {
                histogram = null;
            }

            return (histogram, normalLayout);
        } catch (Exception ex) {
            logger.Error(ex, "Failed to read metadata from KTX2 memory");
            return (null, null);
        }
    }

    /// <summary>
    /// Reads histogram metadata directly from KTX2 file.
    /// This is the SAFE approach that doesn't rely on broken structure marshaling.
    /// Returns null if no histogram metadata found.
    /// </summary>
    public static HistogramMetadata? ReadHistogramMetadata(string filePath) {
        try {
            logger.Info($"[Ktx2MetadataReader] Reading histogram metadata from file: {filePath}");

            if (!File.Exists(filePath)) {
                logger.Warn($"File not found: {filePath}");
                return null;
            }

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fileStream);

            // Read and verify KTX2 identifier (12 bytes)
            byte[] identifier = reader.ReadBytes(12);
            if (!identifier.SequenceEqual(KTX2_IDENTIFIER)) {
                logger.Warn("Not a valid KTX2 file (identifier mismatch)");
                return null;
            }

            logger.Info("[Ktx2MetadataReader] KTX2 identifier verified");

            // Read KTX2 header fields (according to KTX2 spec)
            uint vkFormat = reader.ReadUInt32();           // +12: VkFormat
            uint typeSize = reader.ReadUInt32();           // +16: typeSize
            uint pixelWidth = reader.ReadUInt32();         // +20: pixelWidth
            uint pixelHeight = reader.ReadUInt32();        // +24: pixelHeight
            uint pixelDepth = reader.ReadUInt32();         // +28: pixelDepth
            uint layerCount = reader.ReadUInt32();         // +32: layerCount
            uint faceCount = reader.ReadUInt32();          // +36: faceCount
            uint levelCount = reader.ReadUInt32();         // +40: levelCount
            uint supercompressionScheme = reader.ReadUInt32(); // +44: supercompressionScheme

            // DFD (Data Format Descriptor) offset and length
            uint dfdByteOffset = reader.ReadUInt32();      // +48: dfdByteOffset
            uint dfdByteLength = reader.ReadUInt32();      // +52: dfdByteLength

            // KVD (Key-Value Data) offset and length - THIS IS WHAT WE NEED!
            uint kvdByteOffset = reader.ReadUInt32();      // +56: kvdByteOffset
            uint kvdByteLength = reader.ReadUInt32();      // +60: kvdByteLength

            logger.Info($"[Ktx2MetadataReader] KVD: offset={kvdByteOffset}, length={kvdByteLength}");

            // Check if KVD exists
            if (kvdByteLength == 0) {
                logger.Info("[Ktx2MetadataReader] No Key-Value Data in this KTX2 file");
                return null;
            }

            // Seek to KVD section
            fileStream.Seek(kvdByteOffset, SeekOrigin.Begin);

            // Read entire KVD section
            byte[] kvdData = reader.ReadBytes((int)kvdByteLength);
            logger.Info($"[Ktx2MetadataReader] Read {kvdData.Length} bytes of KVD data");

            // Parse KVD to find "pc.meta" key
            var metadata = ParseKeyValueData(kvdData);
            if (metadata != null) {
                logger.Info($"[Ktx2MetadataReader] Found histogram metadata: {metadata.Scale.Length} channel(s)");

                // Verify format correctness
                if (metadata.Scale[0] > 1.0f) {
                    logger.Error($"INVALID histogram format detected: scale={metadata.Scale[0]:F4} > 1.0");
                    logger.Error("This file uses old/broken format. Please re-convert the texture with current version.");
                    logger.Error("Expected: scale < 1.0 (GPU recovery values)");
                    return null;
                }
            }

            return metadata;
        } catch (Exception ex) {
            logger.Error(ex, "Failed to read histogram metadata from KTX2 file");
            return null;
        }
    }

    /// <summary>
    /// Parses KTX2 Key-Value Data to find "pc.meta" key with TLV histogram data.
    /// </summary>
    private static HistogramMetadata? ParseKeyValueData(byte[] kvdData) {
        int offset = 0;

        while (offset < kvdData.Length) {
            // Read keyAndValueByteSize (4 bytes)
            if (offset + 4 > kvdData.Length) break;

            uint keyAndValueByteSize = BitConverter.ToUInt32(kvdData, offset);
            offset += 4;

            if (keyAndValueByteSize == 0 || offset + keyAndValueByteSize > kvdData.Length) {
                logger.Warn($"Invalid keyAndValueByteSize: {keyAndValueByteSize}");
                break;
            }

            // Read key (null-terminated string)
            int keyStart = offset;
            int keyEnd = keyStart;
            while (keyEnd < offset + keyAndValueByteSize && kvdData[keyEnd] != 0) {
                keyEnd++;
            }

            if (keyEnd >= offset + keyAndValueByteSize) {
                logger.Warn("No null terminator found for key");
                break;
            }

            string key = Encoding.UTF8.GetString(kvdData, keyStart, keyEnd - keyStart);
            int keyLength = keyEnd - keyStart + 1; // Include null terminator

            // Value starts right after key (including null terminator)
            int valueStart = offset + keyLength;
            int valueLength = (int)keyAndValueByteSize - keyLength;

            logger.Info($"[Ktx2MetadataReader] KVD entry: key=\"{key}\", valueLength={valueLength}");

            // Check if this is "pc.meta"
            if (key == "pc.meta") {
                logger.Info($"[Ktx2MetadataReader] Found pc.meta key with {valueLength} bytes of TLV data");
                byte[] tlvData = new byte[valueLength];
                Array.Copy(kvdData, valueStart, tlvData, 0, valueLength);
                return ParseTLVHistogramData(tlvData);
            }

            // Move to next entry (padding to 4-byte alignment)
            offset += (int)keyAndValueByteSize;
            int padding = (4 - (int)(keyAndValueByteSize & 3)) & 3;
            offset += padding;
        }

        logger.Info("[Ktx2MetadataReader] No pc.meta key found in KTX2 Key-Value Data");
        return null;
    }

    /// <summary>
    /// Parses KTX2 Key-Value Data to find "pc.meta" key with ALL metadata types.
    /// Returns tuple (histogram, normalLayout).
    /// </summary>
    private static (HistogramMetadata?, NormalLayoutMetadata?) ParseKeyValueDataComplete(byte[] kvdData) {
        int offset = 0;

        while (offset < kvdData.Length) {
            // Read keyAndValueByteSize (4 bytes)
            if (offset + 4 > kvdData.Length) break;

            uint keyAndValueByteSize = BitConverter.ToUInt32(kvdData, offset);
            offset += 4;

            if (keyAndValueByteSize == 0 || offset + keyAndValueByteSize > kvdData.Length) {
                logger.Warn($"Invalid keyAndValueByteSize: {keyAndValueByteSize}");
                break;
            }

            // Read key
            int keyStart = offset;
            int keyEnd = keyStart;
            while (keyEnd < offset + keyAndValueByteSize && kvdData[keyEnd] != 0) {
                keyEnd++;
            }

            if (keyEnd >= offset + keyAndValueByteSize) {
                logger.Warn("No null terminator found for key");
                break;
            }

            string key = Encoding.UTF8.GetString(kvdData, keyStart, keyEnd - keyStart);
            int keyLength = keyEnd - keyStart + 1;

            // Value starts right after key
            int valueStart = offset + keyLength;
            int valueLength = (int)keyAndValueByteSize - keyLength;

            // Check if this is "pc.meta"
            if (key == "pc.meta") {
                byte[] tlvData = new byte[valueLength];
                Array.Copy(kvdData, valueStart, tlvData, 0, valueLength);
                return ParseTLVAllData(tlvData);
            }

            // Move to next entry
            offset += (int)keyAndValueByteSize;
            int padding = (4 - (int)(keyAndValueByteSize & 3)) & 3;
            offset += padding;
        }

        return (null, null);
    }

    /// <summary>
    /// Parses TLV (Type-Length-Value) data to extract ALL metadata types.
    /// Returns tuple (histogram, normalLayout).
    /// </summary>
    private static (HistogramMetadata?, NormalLayoutMetadata?) ParseTLVAllData(byte[] tlvData) {
        HistogramMetadata? histogram = null;
        NormalLayoutMetadata? normalLayout = null;
        int offset = 0;

        while (offset + 4 <= tlvData.Length) {
            // Read TLV header
            byte type = tlvData[offset];
            byte flags = tlvData[offset + 1];
            ushort length = BitConverter.ToUInt16(tlvData, offset + 2);
            offset += 4;

            if (offset + length > tlvData.Length) {
                logger.Warn($"TLV block length {length} exceeds remaining data");
                break;
            }

            byte[] payload = new byte[length];
            Array.Copy(tlvData, offset, payload, 0, length);

            logger.Info($"[Ktx2MetadataReader] TLV block: type=0x{type:X2}, flags=0x{flags:X2}, length={length}");

            // Parse histogram block
            if (type >= 0x01 && type <= 0x04) {
                histogram = ParseHistogramTLV(type, flags, payload);
            }
            // Parse normal layout block
            else if (type == 0x20) {
                normalLayout = ParseNormalLayoutTLV(flags, payload);
            }

            // Move to next block
            offset += length;
            int padding = (4 - (length & 3)) & 3;
            offset += padding;
        }

        return (histogram, normalLayout);
    }

    /// <summary>
    /// Parses TLV (Type-Length-Value) data to extract histogram scale/offset.
    /// </summary>
    private static HistogramMetadata? ParseTLVHistogramData(byte[] tlvData) {
        int offset = 0;

        while (offset + 4 <= tlvData.Length) {
            // Read TLV header: Type (1 byte), Flags (1 byte), Length (2 bytes)
            byte type = tlvData[offset];
            byte flags = tlvData[offset + 1];
            ushort length = BitConverter.ToUInt16(tlvData, offset + 2);
            offset += 4;

            if (offset + length > tlvData.Length) {
                logger.Warn($"TLV block length {length} exceeds remaining data");
                break;
            }

            byte[] payload = new byte[length];
            Array.Copy(tlvData, offset, payload, 0, length);

            logger.Info($"[Ktx2MetadataReader] TLV block: type=0x{type:X2}, flags=0x{flags:X2}, length={length}");

            // Check if this is a histogram block
            var histogramMeta = ParseHistogramTLV(type, flags, payload);
            if (histogramMeta != null) {
                return histogramMeta;
            }

            // Move to next block (padding to 4-byte alignment)
            offset += length;
            int padding = (4 - (length & 3)) & 3;
            offset += padding;
        }

        logger.Debug("No histogram TLV block found");
        return null;
    }

    /// <summary>
    /// Parses a single TLV block if it's a histogram type.
    /// CRITICAL: Только 2 типа - HIST_SCALAR (0x01) и HIST_PER_CHANNEL_3 (0x03)
    /// RGBA (4 канала) НЕ поддерживается!
    /// </summary>
    private static HistogramMetadata? ParseHistogramTLV(byte type, byte flags, byte[] payload) {
        // Extract quantization from flags [3:2]
        byte quantization = (byte)((flags >> 2) & 0x03);

        switch (type) {
            case 0x01: // HIST_SCALAR (AverageLuminance)
                logger.Info("[Ktx2MetadataReader] Found HIST_SCALAR metadata");
                return ParseScalarHistogram(payload, quantization);

            case 0x03: // HIST_PER_CHANNEL_3 (RGB PerChannel)
                logger.Info("[Ktx2MetadataReader] Found HIST_PER_CHANNEL_3 metadata");
                return ParsePerChannelHistogram(payload, 3, quantization);

            default:
                logger.Info($"[Ktx2MetadataReader] Unknown TLV type: 0x{type:X2} (not a histogram block)");
                return null; // Not a histogram block
        }
    }

    /// <summary>
    /// Parses scalar histogram (single scale/offset for all channels).
    /// </summary>
    private static HistogramMetadata ParseScalarHistogram(byte[] payload, byte quantization) {
        float scale, offset;

        if (quantization == 0) {
            // Half16: 2 bytes scale, 2 bytes offset
            if (payload.Length < 4) {
                logger.Error($"HIST_SCALAR payload too short: {payload.Length} bytes");
                return HistogramMetadata.Identity();
            }

            ushort scaleHalf = BitConverter.ToUInt16(payload, 0);
            ushort offsetHalf = BitConverter.ToUInt16(payload, 2);

            scale = HalfToFloat(scaleHalf);
            offset = HalfToFloat(offsetHalf);
        } else if (quantization == 1) {
            // PackedUInt32: 2 bytes scale (uint16), 2 bytes offset (uint16)
            if (payload.Length < 4) {
                logger.Error($"HIST_SCALAR payload too short: {payload.Length} bytes");
                return HistogramMetadata.Identity();
            }

            uint packed = BitConverter.ToUInt32(payload, 0);
            ushort scaleU16 = (ushort)(packed & 0xFFFF);
            ushort offsetU16 = (ushort)((packed >> 16) & 0xFFFF);

            scale = scaleU16 / 65535.0f;
            offset = offsetU16 / 65535.0f;
        } else {
            // Float32: 4 bytes scale, 4 bytes offset
            if (payload.Length < 8) {
                logger.Error($"HIST_SCALAR payload too short: {payload.Length} bytes");
                return HistogramMetadata.Identity();
            }

            scale = BitConverter.ToSingle(payload, 0);
            offset = BitConverter.ToSingle(payload, 4);
        }

        return new HistogramMetadata {
            Scale = new[] { scale },
            Offset = new[] { offset }
        };
    }

    /// <summary>
    /// Parses per-channel histogram (separate scale/offset for RGB or RGBA).
    /// </summary>
    private static HistogramMetadata ParsePerChannelHistogram(byte[] payload, int channelCount, byte quantization) {
        float[] scale = new float[channelCount];
        float[] offset = new float[channelCount];

        if (quantization == 0) {
            // Half16: channelCount * 2 bytes for scale, channelCount * 2 bytes for offset
            int expectedSize = channelCount * 4;
            if (payload.Length < expectedSize) {
                logger.Error($"HIST_PER_CHANNEL payload too short: {payload.Length} < {expectedSize}");
                return HistogramMetadata.Identity();
            }

            for (int i = 0; i < channelCount; i++) {
                ushort scaleHalf = BitConverter.ToUInt16(payload, i * 2);
                ushort offsetHalf = BitConverter.ToUInt16(payload, channelCount * 2 + i * 2);

                scale[i] = HalfToFloat(scaleHalf);
                offset[i] = HalfToFloat(offsetHalf);
            }
        } else if (quantization == 1) {
            // PackedUInt32: channelCount * 4 bytes (each uint32 contains scale and offset)
            int expectedSize = channelCount * 4;
            if (payload.Length < expectedSize) {
                logger.Error($"HIST_PER_CHANNEL payload too short: {payload.Length} < {expectedSize}");
                return HistogramMetadata.Identity();
            }

            for (int i = 0; i < channelCount; i++) {
                uint packed = BitConverter.ToUInt32(payload, i * 4);
                ushort scaleU16 = (ushort)(packed & 0xFFFF);
                ushort offsetU16 = (ushort)((packed >> 16) & 0xFFFF);

                scale[i] = scaleU16 / 65535.0f;
                offset[i] = offsetU16 / 65535.0f;
            }
        } else {
            // Float32: channelCount * 4 bytes for scale, channelCount * 4 bytes for offset
            int expectedSize = channelCount * 8;
            if (payload.Length < expectedSize) {
                logger.Error($"HIST_PER_CHANNEL payload too short: {payload.Length} < {expectedSize}");
                return HistogramMetadata.Identity();
            }

            for (int i = 0; i < channelCount; i++) {
                scale[i] = BitConverter.ToSingle(payload, i * 4);
                offset[i] = BitConverter.ToSingle(payload, channelCount * 4 + i * 4);
            }
        }

        return new HistogramMetadata {
            Scale = scale,
            Offset = offset
        };
    }

    /// <summary>
    /// Converts IEEE 754 half float (16-bit) to float (32-bit).
    /// </summary>
    private static float HalfToFloat(ushort half) {
        // Use System.Half from .NET 5+
        byte[] bytes = BitConverter.GetBytes(half);
        Half h = BitConverter.ToHalf(bytes);
        return (float)h;
    }

    /// <summary>
    /// Parses NORMAL_LAYOUT TLV block.
    /// Layout is encoded in flags byte (bits [2:0]).
    /// Payload is typically empty for NORMAL_LAYOUT.
    /// </summary>
    private static NormalLayoutMetadata? ParseNormalLayoutTLV(byte flags, byte[] payload) {
        // Extract layout from flags [2:0]
        NormalLayout layout = (NormalLayout)(flags & 0x07);

        logger.Info($"[Ktx2MetadataReader] Found NORMAL_LAYOUT: {layout}");

        if (layout == NormalLayout.NONE) {
            return null; // Not a normal map
        }

        return new NormalLayoutMetadata {
            Layout = layout
        };
    }
}

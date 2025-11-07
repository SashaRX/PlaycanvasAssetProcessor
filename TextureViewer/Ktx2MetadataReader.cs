using System;
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

    /// <summary>
    /// Reads histogram metadata from KTX2 texture handle using ktxHashList API.
    /// Returns null if no histogram metadata found.
    /// </summary>
    public static HistogramMetadata? ReadHistogramMetadata(IntPtr textureHandle) {
        try {
            logger.Info("[Ktx2MetadataReader] Starting histogram metadata read...");

            // Get kvDataHead structure (contains hash list)
            var tex = Marshal.PtrToStructure<LibKtxNative.KtxTexture2>(textureHandle);

            // Debug: Log key structure fields to verify structure is read correctly
            logger.Info($"[DEBUG] Structure fields: baseWidth={tex.baseWidth}, baseHeight={tex.baseHeight}, numLevels={tex.numLevels}, vkFormat={tex.vkFormat}");
            logger.Info($"[DEBUG] isArray={tex.isArray}, isCubemap={tex.isCubemap}, isCompressed={tex.isCompressed}");
            logger.Info($"[DEBUG] dataSize={tex.dataSize}, pData=0x{tex.pData:X}");

            var kvDataHead = tex.kvDataHead;
            logger.Info($"kvDataHead.numEntries={kvDataHead.numEntries}, kvDataHead.pHead=0x{kvDataHead.pHead:X}");

            if (kvDataHead.pHead == IntPtr.Zero) {
                logger.Debug("No Key-Value Data in KTX2 texture (kvDataHead.pHead is null)");
                return null;
            }

            // Calculate offset of kvDataHead field within KtxTexture2 structure using Marshal.OffsetOf
            // This is safer than hardcoding offset values
            int kvDataHeadOffset = (int)Marshal.OffsetOf<LibKtxNative.KtxTexture2>("kvDataHead");
            IntPtr pKvDataHead = IntPtr.Add(textureHandle, kvDataHeadOffset);

            logger.Info($"kvDataHead offset in structure: {kvDataHeadOffset} bytes");
            logger.Info($"Calling ktxHashList_FindValue with pKvDataHead=0x{pKvDataHead:X}");

            // Use ktxHashList_FindValue to find "pc.meta" key
            // Pass pointer to kvDataHead field inside the native texture structure
            var result = LibKtxNative.ktxHashList_FindValue(
                pKvDataHead,
                "pc.meta",
                out uint valueLen,
                out IntPtr pValue);

            logger.Info($"ktxHashList_FindValue returned: {result}, valueLen={valueLen}, pValue=0x{pValue:X}");

            if (result == LibKtxNative.KtxErrorCode.KTX_NOT_FOUND) {
                logger.Info("Key 'pc.meta' not found in KTX2 Key-Value Data");
                return null;
            }

            if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                logger.Warn($"ktxHashList_FindValue failed: {LibKtxNative.GetErrorString(result)}");
                return null;
            }

            if (pValue == IntPtr.Zero || valueLen == 0) {
                logger.Warn($"Found 'pc.meta' but value is empty (len={valueLen}, ptr={pValue})");
                return null;
            }

            logger.Info($"Found 'pc.meta' key with {valueLen} bytes of TLV data");

            // Copy TLV data from native memory
            byte[] tlvData = new byte[valueLen];
            Marshal.Copy(pValue, tlvData, 0, (int)valueLen);

            logger.Debug($"TLV raw bytes (first 32): {BitConverter.ToString(tlvData.Take(Math.Min(32, tlvData.Length)).ToArray())}");

            // Parse TLV data
            return ParseTLVHistogramData(tlvData);
        } catch (Exception ex) {
            logger.Error(ex, "Failed to read histogram metadata from KTX2");
            return null;
        }
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

            logger.Debug($"TLV block: type=0x{type:X2}, flags=0x{flags:X2}, length={length}");

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
    /// </summary>
    private static HistogramMetadata? ParseHistogramTLV(byte type, byte flags, byte[] payload) {
        // Extract quantization from flags [3:2]
        byte quantization = (byte)((flags >> 2) & 0x03);

        switch (type) {
            case 0x01: // HIST_SCALAR
                logger.Info("Found HIST_SCALAR metadata");
                return ParseScalarHistogram(payload, quantization);

            case 0x02: // HIST_RGB
                logger.Info("Found HIST_RGB metadata");
                return ParseScalarHistogram(payload, quantization); // Same format as scalar

            case 0x03: // HIST_PER_CHANNEL_3 (RGB)
                logger.Info("Found HIST_PER_CHANNEL_3 metadata");
                return ParsePerChannelHistogram(payload, 3, quantization);

            case 0x04: // HIST_PER_CHANNEL_4 (RGBA)
                logger.Info("Found HIST_PER_CHANNEL_4 metadata");
                return ParsePerChannelHistogram(payload, 4, quantization);

            default:
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

        logger.Info($"Histogram metadata: scale={scale:F4}, offset={offset:F4} (quantization={quantization})");

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

        logger.Info($"Per-channel histogram metadata: channels={channelCount}, quantization={quantization}");
        for (int i = 0; i < channelCount; i++) {
            logger.Info($"  Channel {i}: scale={scale[i]:F4}, offset={offset[i]:F4}");
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
}

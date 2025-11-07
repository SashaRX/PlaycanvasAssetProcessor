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
            logger.Info("[Ktx2MetadataReader] Starting histogram metadata read (experimental offset scan)...");

            // EXPERIMENTAL: Scan structure memory to find kvData and kvDataLen fields
            // We'll look for pairs of (IntPtr, uint) where IntPtr is non-zero and uint is reasonable (10-200 bytes)

            logger.Info("Scanning structure memory for KVD fields...");

            for (int offset = 0; offset < 512; offset += 4) {
                try {
                    // Try reading IntPtr at this offset
                    IntPtr ptrValue = Marshal.ReadIntPtr(textureHandle, offset);

                    // Skip NULL pointers
                    if (ptrValue == IntPtr.Zero) continue;

                    // Try reading uint at offset+8 (assuming pointer is 8 bytes on x64)
                    uint lenValue = (uint)Marshal.ReadInt32(textureHandle, offset + 8);

                    // Check if this looks like kvData/kvDataLen pair
                    // kvDataLen should be reasonable (10-200 bytes for our metadata)
                    if (lenValue > 0 && lenValue < 500) {
                        logger.Info($"[SCAN] Offset {offset}: ptr=0x{ptrValue:X}, len={lenValue}");

                        // Try to read and parse as KVD
                        try {
                            byte[] testData = new byte[lenValue];
                            Marshal.Copy(ptrValue, testData, 0, (int)lenValue);

                            // Check if first 4 bytes look like keyAndValueByteSize
                            if (testData.Length >= 4) {
                                uint keyAndValueByteSize = BitConverter.ToUInt32(testData, 0);
                                logger.Info($"[SCAN] Offset {offset}: First 4 bytes = {keyAndValueByteSize}, raw: {BitConverter.ToString(testData.Take(Math.Min(32, testData.Length)).ToArray())}");

                                // Try parsing
                                var result = ParseKeyValueData(testData);
                                if (result != null) {
                                    logger.Info($"[SUCCESS] Found KVD at offset {offset}!");
                                    return result;
                                }
                            }
                        } catch {
                            // Not valid memory, continue scanning
                        }
                    }
                } catch {
                    // Invalid offset, continue
                }
            }

            logger.Warn("Failed to find KVD fields in structure memory scan");
            return null;
        } catch (Exception ex) {
            logger.Error(ex, "Failed to read histogram metadata from KTX2");
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

            logger.Debug($"KVD entry: key=\"{key}\", valueLength={valueLength}");

            // Check if this is "pc.meta"
            if (key == "pc.meta") {
                logger.Info($"Found pc.meta key with {valueLength} bytes of TLV data");
                byte[] tlvData = new byte[valueLength];
                Array.Copy(kvdData, valueStart, tlvData, 0, valueLength);
                return ParseTLVHistogramData(tlvData);
            }

            // Move to next entry (padding to 4-byte alignment)
            offset += (int)keyAndValueByteSize;
            int padding = (4 - (int)(keyAndValueByteSize & 3)) & 3;
            offset += padding;
        }

        logger.Debug("No pc.meta key found in KTX2 Key-Value Data");
        return null;
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

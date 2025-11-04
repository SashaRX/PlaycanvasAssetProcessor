using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NLog;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// Loads KTX2 textures via libktx and transcodes Basis Universal to RGBA8.
/// </summary>
public static class Ktx2TextureLoader {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Load a KTX2 texture from a file path.
    /// </summary>
    public static TextureData LoadFromFile(string filePath) {
        logger.Info($"Loading KTX2 texture from: {filePath}");

        // Create texture from file
        var result = LibKtxNative.ktxTexture2_CreateFromNamedFile(
            filePath,
            (uint)LibKtxNative.KtxTextureCreateFlagBits.KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT,
            out IntPtr textureHandle);

        if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
            throw new Exception($"Failed to load KTX2 file: {LibKtxNative.GetErrorString(result)}");
        }

        try {
            return LoadFromHandle(textureHandle);
        } finally {
            LibKtxNative.ktxTexture2_Destroy(textureHandle);
        }
    }

    /// <summary>
    /// Load a KTX2 texture from a byte array.
    /// </summary>
    public static TextureData LoadFromMemory(byte[] data) {
        logger.Info($"Loading KTX2 texture from memory ({data.Length} bytes)");

        IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
        try {
            Marshal.Copy(data, 0, dataPtr, data.Length);

            var result = LibKtxNative.ktxTexture2_CreateFromMemory(
                dataPtr,
                new UIntPtr((uint)data.Length),
                (uint)LibKtxNative.KtxTextureCreateFlagBits.KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT,
                out IntPtr textureHandle);

            if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                throw new Exception($"Failed to load KTX2 from memory: {LibKtxNative.GetErrorString(result)}");
            }

            try {
                return LoadFromHandle(textureHandle);
            } finally {
                LibKtxNative.ktxTexture2_Destroy(textureHandle);
            }
        } finally {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    /// <summary>
    /// Load texture data from a ktxTexture2 handle.
    /// </summary>
    private static TextureData LoadFromHandle(IntPtr textureHandle) {
        // Read texture info
        var tex = Marshal.PtrToStructure<LibKtxNative.KtxTexture2>(textureHandle);

        logger.Info($"KTX2 info: {tex.baseWidth}x{tex.baseHeight}, {tex.numLevels} mips, vkFormat={tex.vkFormat}, supercompression={tex.supercompressionScheme}");

        // Check if needs transcoding (Basis Universal compressed)
        bool needsTranscode = LibKtxNative.ktxTexture2_NeedsTranscoding(textureHandle);

        if (needsTranscode) {
            logger.Info("KTX2 texture is Basis Universal compressed, transcoding to RGBA32...");

            var transcodeResult = LibKtxNative.ktxTexture2_TranscodeBasis(
                textureHandle,
                LibKtxNative.KtxTranscodeFormat.KTX_TTF_RGBA32,
                0);

            if (transcodeResult != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                throw new Exception($"Failed to transcode KTX2: {LibKtxNative.GetErrorString(transcodeResult)}");
            }

            logger.Info("Transcode successful");

            // Re-read texture structure after transcoding
            tex = Marshal.PtrToStructure<LibKtxNative.KtxTexture2>(textureHandle);
        }

        // Get data pointer
        IntPtr dataPtr = LibKtxNative.ktxTexture_GetData(textureHandle);
        if (dataPtr == IntPtr.Zero) {
            throw new Exception("Failed to get KTX2 data pointer");
        }

        int baseWidth = (int)tex.baseWidth;
        int baseHeight = (int)tex.baseHeight;
        int mipCount = (int)tex.numLevels;

        // Extract all mip levels
        var mipLevels = new List<MipLevel>(mipCount);

        for (int level = 0; level < mipCount; level++) {
            int mipWidth = Math.Max(1, baseWidth >> level);
            int mipHeight = Math.Max(1, baseHeight >> level);

            // Get offset to this mip level
            var offsetResult = LibKtxNative.ktxTexture_GetImageOffset(
                textureHandle,
                (uint)level,
                0, // layer
                0, // faceSlice
                out UIntPtr offset);

            if (offsetResult != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                throw new Exception($"Failed to get offset for mip {level}: {LibKtxNative.GetErrorString(offsetResult)}");
            }

            // Get size of this mip level
            UIntPtr mipSize = LibKtxNative.ktxTexture_GetImageSize(textureHandle, (uint)level);
            int expectedSize = mipWidth * mipHeight * 4; // RGBA8

            if ((int)mipSize != expectedSize) {
                logger.Warn($"Mip {level} size mismatch: expected {expectedSize}, got {mipSize}");
            }

            // Copy mip data
            byte[] mipData = new byte[expectedSize];
            IntPtr mipPtr = IntPtr.Add(dataPtr, (int)offset);
            Marshal.Copy(mipPtr, mipData, 0, Math.Min(expectedSize, (int)mipSize));

            var mipLevel = new MipLevel {
                Level = level,
                Width = mipWidth,
                Height = mipHeight,
                Data = mipData
            };

            mipLevels.Add(mipLevel);

            logger.Debug($"Extracted mip {level}: {mipWidth}x{mipHeight}, {mipData.Length} bytes");
        }

        // Determine format info
        string sourceFormat = needsTranscode
            ? $"KTX2/BasisU (scheme={tex.supercompressionScheme})"
            : $"KTX2 (vkFormat={tex.vkFormat})";

        // Check for sRGB format (common Vulkan sRGB formats)
        bool isSRGB = tex.vkFormat == 37 || // VK_FORMAT_R8G8B8A8_SRGB
                      tex.vkFormat == 43 || // VK_FORMAT_B8G8R8A8_SRGB
                      tex.vkFormat == 29;   // VK_FORMAT_R8G8B8_SRGB

        // Check for alpha (RGBA formats)
        bool hasAlpha = tex.vkFormat == 37 || // VK_FORMAT_R8G8B8A8_SRGB
                        tex.vkFormat == 43 || // VK_FORMAT_B8G8R8A8_SRGB
                        tex.vkFormat == 44;   // VK_FORMAT_B8G8R8A8_UNORM

        // After transcoding, we always have RGBA8
        if (needsTranscode) {
            hasAlpha = true;
        }

        logger.Info($"KTX2 loaded successfully: {baseWidth}x{baseHeight}, {mipCount} mips, sRGB={isSRGB}, alpha={hasAlpha}");

        return new TextureData {
            Width = baseWidth,
            Height = baseHeight,
            MipLevels = mipLevels,
            IsSRGB = isSRGB,
            HasAlpha = hasAlpha,
            SourceFormat = sourceFormat,
            IsHDR = false // KTX2 can be HDR but we'd need to check vkFormat more carefully
        };
    }
}

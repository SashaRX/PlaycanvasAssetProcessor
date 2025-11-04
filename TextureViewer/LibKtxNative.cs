using System;
using System.Runtime.InteropServices;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// P/Invoke wrapper for libktx (KTX-Software v4.x).
/// Requires ktx.dll or libktx.dll to be available.
/// </summary>
internal static class LibKtxNative {
    // Try different DLL names (ktx.dll from vcpkg, libktx.dll from official builds)
    private const string DllName = "ktx";

    #region Enums

    public enum KtxErrorCode : int {
        KTX_SUCCESS = 0,
        KTX_FILE_DATA_ERROR = 1,
        KTX_FILE_ISPIPE = 2,
        KTX_FILE_OPEN_FAILED = 3,
        KTX_FILE_OVERFLOW = 4,
        KTX_FILE_READ_ERROR = 5,
        KTX_FILE_SEEK_ERROR = 6,
        KTX_FILE_UNEXPECTED_EOF = 7,
        KTX_FILE_WRITE_ERROR = 8,
        KTX_GL_ERROR = 9,
        KTX_INVALID_OPERATION = 10,
        KTX_INVALID_VALUE = 11,
        KTX_NOT_FOUND = 12,
        KTX_OUT_OF_MEMORY = 13,
        KTX_TRANSCODE_FAILED = 14,
        KTX_UNKNOWN_FILE_FORMAT = 15,
        KTX_UNSUPPORTED_TEXTURE_TYPE = 16,
        KTX_UNSUPPORTED_FEATURE = 17,
        KTX_LIBRARY_NOT_LINKED = 18,
        KTX_DECOMPRESS_LENGTH_ERROR = 19,
        KTX_DECOMPRESS_CHECKSUM_ERROR = 20
    }

    public enum KtxTranscodeFormat : uint {
        // Uncompressed (transcoded to RGBA)
        KTX_TTF_RGBA32 = 4,
        KTX_TTF_RGB565 = 5,
        KTX_TTF_RGBA4444 = 7,

        // For reference
        KTX_TTF_ETC1_RGB = 0,
        KTX_TTF_ETC2_RGBA = 1,
        KTX_TTF_BC1_RGB = 2,
        KTX_TTF_BC3_RGBA = 3,
        KTX_TTF_BC7_RGBA = 6,
        KTX_TTF_ETC2_EAC_R11 = 20,
        KTX_TTF_ETC2_EAC_RG11 = 21
    }

    public enum KtxTextureCreateFlagBits : uint {
        KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT = 0x00000001,
        KTX_TEXTURE_CREATE_RAW_KVDATA_BIT = 0x00000002,
        KTX_TEXTURE_CREATE_SKIP_KVDATA_BIT = 0x00000004,
        KTX_TEXTURE_CREATE_CHECK_GLTF_BASISU_BIT = 0x00000008
    }

    #endregion

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct KtxTexture2 {
        public IntPtr vtbl;
        public IntPtr vvtbl;
        public IntPtr classId;
        public IntPtr pvtbl;

        public uint isArray;
        public uint isCubemap;
        public uint isCompressed;
        public uint generateMipmaps;

        public uint baseWidth;
        public uint baseHeight;
        public uint baseDepth;

        public uint numDimensions;
        public uint numLevels;
        public uint numLayers;
        public uint numFaces;

        // Orientation data
        public IntPtr orientation;

        // KTX2-specific fields
        public IntPtr kvDataHead;
        public uint kvDataLen;
        public IntPtr kvData;

        public uint dataSize;
        public IntPtr pData;

        // VkFormat
        public uint vkFormat;

        // Supercompression scheme
        public uint supercompressionScheme;

        public uint isVideo;
        public uint pixelWidth;
        public uint pixelHeight;
        public uint pixelDepth;

        // DFD
        public IntPtr pDfd;
        public uint dfdLen;
    }

    #endregion

    #region P/Invoke Declarations

    /// <summary>
    /// Create a KTX2 texture from a file.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern KtxErrorCode ktxTexture2_CreateFromNamedFile(
        string filename,
        uint createFlags,
        out IntPtr newTex);

    /// <summary>
    /// Create a KTX2 texture from memory.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture2_CreateFromMemory(
        IntPtr bytes,
        UIntPtr size,
        uint createFlags,
        out IntPtr newTex);

    /// <summary>
    /// Destroy a KTX2 texture and free memory.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ktxTexture2_Destroy(IntPtr texture);

    /// <summary>
    /// Check if texture needs transcoding (is Basis Universal compressed).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool ktxTexture2_NeedsTranscoding(IntPtr texture);

    /// <summary>
    /// Transcode a Basis Universal compressed texture to the target format.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture2_TranscodeBasis(
        IntPtr texture,
        KtxTranscodeFormat outputFormat,
        uint transcodeFlags);

    /// <summary>
    /// Get the size of a specific mip level.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr ktxTexture_GetImageSize(
        IntPtr texture,
        uint level);

    /// <summary>
    /// Get offset to image data for a specific level.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture_GetImageOffset(
        IntPtr texture,
        uint level,
        uint layer,
        uint faceSlice,
        out UIntPtr pOffset);

    /// <summary>
    /// Get data pointer for texture.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ktxTexture_GetData(IntPtr texture);

    #endregion

    #region Helper Methods

    public static string GetErrorString(KtxErrorCode error) {
        return error switch {
            KtxErrorCode.KTX_SUCCESS => "Success",
            KtxErrorCode.KTX_FILE_DATA_ERROR => "File data error",
            KtxErrorCode.KTX_FILE_OPEN_FAILED => "File open failed",
            KtxErrorCode.KTX_FILE_READ_ERROR => "File read error",
            KtxErrorCode.KTX_INVALID_VALUE => "Invalid value",
            KtxErrorCode.KTX_OUT_OF_MEMORY => "Out of memory",
            KtxErrorCode.KTX_TRANSCODE_FAILED => "Transcode failed",
            KtxErrorCode.KTX_UNKNOWN_FILE_FORMAT => "Unknown file format",
            KtxErrorCode.KTX_UNSUPPORTED_TEXTURE_TYPE => "Unsupported texture type",
            _ => $"Error code {(int)error}"
        };
    }

    #endregion
}

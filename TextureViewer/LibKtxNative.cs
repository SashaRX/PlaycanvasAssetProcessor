using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// P/Invoke wrapper for libktx (KTX-Software v4.x).
/// Requires ktx.dll or libktx.dll to be available.
/// </summary>
internal static class LibKtxNative {
    // Try different DLL names (ktx.dll from vcpkg, libktx.dll from official builds)
    private const string DllName = "ktx";

    private static bool _dllLoaded = false;
    private static IntPtr _ktxHandle = IntPtr.Zero;

    /// <summary>
    /// Загружает ktx.dll из указанной директории или рядом с exe
    /// </summary>
    public static bool LoadKtxDll(string? ktxDirectory = null) {
        if (_dllLoaded) {
            return true;
        }

        // Список мест для поиска ktx.dll (в порядке приоритета)
        var searchPaths = new List<string>();

        // 1. Рядом с AssetProcessor.exe (highest priority)
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        searchPaths.Add(Path.Combine(exeDirectory, "ktx.dll"));

        // 2. В указанной директории (если задана)
        if (!string.IsNullOrEmpty(ktxDirectory)) {
            searchPaths.Add(Path.Combine(ktxDirectory, "ktx.dll"));
        }

        // 3. Пробуем загрузить из каждого места
        foreach (var ktxDllPath in searchPaths) {
            Console.WriteLine($"[LibKtxNative] Checking: {ktxDllPath}");

            if (!File.Exists(ktxDllPath)) {
                Console.WriteLine($"[LibKtxNative]   File not found");
                continue;
            }

            Console.WriteLine($"[LibKtxNative]   File exists, attempting LoadLibrary...");
            _ktxHandle = LoadLibrary(ktxDllPath);

            if (_ktxHandle != IntPtr.Zero) {
                Console.WriteLine($"[LibKtxNative]   ✓ Loaded successfully (handle: 0x{_ktxHandle:X})");
                _dllLoaded = true;
                _loadedFrom = ktxDllPath;
                return true;
            } else {
                var error = Marshal.GetLastWin32Error();
                Console.WriteLine($"[LibKtxNative]   ✗ LoadLibrary failed (Win32 error: {error})");
            }
        }

        Console.WriteLine($"[LibKtxNative] Failed to load ktx.dll from any location");
        return false;
    }

    private static string? _loadedFrom = null;

    /// <summary>
    /// Возвращает статус загрузки ktx.dll для отладки
    /// </summary>
    public static string GetLoadStatus() {
        if (_dllLoaded) {
            return $"ktx.dll loaded from: {_loadedFrom ?? "unknown"} (handle: 0x{_ktxHandle:X})";
        }
        return "ktx.dll not loaded";
    }

    // Kernel32 LoadLibrary для динамической загрузки DLL
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

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

    /// <summary>
    /// OETF (transfer function) values from KHR Data Format Descriptor.
    /// From khr_df.h in KTX-Software.
    /// </summary>
    public enum KhrDfTransfer : uint {
        KHR_DF_TRANSFER_UNSPECIFIED = 0,
        KHR_DF_TRANSFER_LINEAR = 1,       // Linear transfer function
        KHR_DF_TRANSFER_SRGB = 2,         // sRGB transfer function
        KHR_DF_TRANSFER_ITU = 3,          // BT.709/BT.601 (effectively sRGB)
        KHR_DF_TRANSFER_NTSC = 4,
        KHR_DF_TRANSFER_SLOG = 5,
        KHR_DF_TRANSFER_SLOG2 = 6,
        KHR_DF_TRANSFER_BT1886 = 7,
        KHR_DF_TRANSFER_HLG_OETF = 8,
        KHR_DF_TRANSFER_HLG_EOTF = 9,
        KHR_DF_TRANSFER_PQ_EOTF = 10,
        KHR_DF_TRANSFER_PQ_OETF = 11,
        KHR_DF_TRANSFER_DCIP3 = 12,
        KHR_DF_TRANSFER_PAL_OETF = 13,
        KHR_DF_TRANSFER_PAL625_EOTF = 14,
        KHR_DF_TRANSFER_ST240 = 15,
        KHR_DF_TRANSFER_ACESCC = 16,
        KHR_DF_TRANSFER_ACESCCT = 17,
        KHR_DF_TRANSFER_ADOBERGB = 18
    }

    /// <summary>
    /// Storage allocation options for texture creation
    /// </summary>
    public enum KtxTextureCreateStorage : uint {
        KTX_TEXTURE_CREATE_NO_STORAGE = 0,
        KTX_TEXTURE_CREATE_ALLOC_STORAGE = 1
    }

    #endregion

    #region Structs

    /// <summary>
    /// Structure for passing texture information to ktxTexture2_Create()
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KtxTextureCreateInfo {
        public uint glInternalformat;  // Ignored for KTX2
        public uint vkFormat;          // VkFormat (e.g., VK_FORMAT_R8G8B8A8_UNORM = 37)
        public IntPtr pDfd;            // Optional DFD, can be IntPtr.Zero

        public uint baseWidth;
        public uint baseHeight;
        public uint baseDepth;

        public uint numDimensions;
        public uint numLevels;
        public uint numLayers;
        public uint numFaces;

        public byte isArray;           // KTX_TRUE or KTX_FALSE
        public byte generateMipmaps;   // KTX_TRUE or KTX_FALSE
    }

    /// <summary>
    /// Extended parameters for Basis Universal compression
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KtxBasisParams {
        // Размер структуры (ОБЯЗАТЕЛЬНО!)
        public uint structSize;

        // Общие параметры
        public byte uastc;              // 1 = UASTC, 0 = ETC1S
        public byte verbose;            // Вывод отладочной информации
        public byte noSSE;              // Запретить SSE
        public uint threadCount;        // Количество потоков (0 = auto)

        // ETC1S параметры
        public uint compressionLevel;   // 0-6, default = 2
        public uint qualityLevel;       // 1-255, default = 128
        public uint maxEndpoints;       // Максимум endpoint палитры
        public float endpointRDOThreshold;
        public uint maxSelectors;       // Максимум selector палитры
        public float selectorRDOThreshold;

        // Padding для выравнивания (struct довольно большой)
        // Полная структура содержит много других полей, но для базового использования достаточно этих
        // Остальное можно оставить нулевым
    }

    /// <summary>
    /// VkFormat значения для KTX2
    /// </summary>
    public enum VkFormat : uint {
        VK_FORMAT_UNDEFINED = 0,
        VK_FORMAT_R8G8B8A8_UNORM = 37,
        VK_FORMAT_R8G8B8A8_SRGB = 43,
        VK_FORMAT_R8G8B8_UNORM = 23,
        VK_FORMAT_R8G8B8_SRGB = 29
    }

    /// <summary>
    /// Simplified KTX texture structure - only read fields we actually need.
    /// We don't try to map the entire structure since it's complex and error-prone.
    /// Instead, we rely on iterator callbacks for actual data extraction.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KtxTexture2 {
        // Base ktxTexture fields (from ktxTexture.h)
        public IntPtr classId;
        public IntPtr vtbl;
        public IntPtr vvtbl;
        public IntPtr _protected;

        // Flags (4 bytes each in the structure, but actually booleans)
        public byte isArray;
        public byte isCubemap;
        public byte isCompressed;
        public byte generateMipmaps;

        // Dimensions
        public uint baseWidth;
        public uint baseHeight;
        public uint baseDepth;

        public uint numDimensions;
        public uint numLevels;
        public uint numLayers;
        public uint numFaces;

        // Orientation (ktx_pack_astc_encoder_mode_e or similar)
        public IntPtr orientation;

        // Key-value data
        public IntPtr kvDataHead;
        public uint kvDataLen;
        public IntPtr kvData;

        // Image data
        public uint dataSize;
        public IntPtr pData;

        // KTX2-specific (beyond base ktxTexture)
        public uint vkFormat;
        public IntPtr pDfd;

        // Note: There are more fields but we don't need them
        // and trying to map them all leads to alignment issues
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
    /// Get OETF (transfer function) from KTX2 texture's DFD.
    /// Returns khr_df_transfer_e enum value.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ktxTexture2_GetOETF(IntPtr texture);

    /// <summary>
    /// Get color primaries from KTX2 texture's DFD.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ktxTexture2_GetColorModel(IntPtr texture);

    // NOTE: ktxTexture_GetImageSize is not available in vcpkg ktx.dll v4.4.2
    // Commented out to avoid EntryPointNotFoundException

    // /// <summary>
    // /// Get the size of a specific mip level.
    // /// </summary>
    // [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    // public static extern UIntPtr ktxTexture_GetImageSize(
    //     IntPtr texture,
    //     uint level);

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

    /// <summary>
    /// Get row pitch for a specific level (alternative to GetImageOffset).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr ktxTexture_GetRowPitch(IntPtr texture, uint level);

    // NOTE: These functions are not available in older vcpkg versions of ktx.dll
    // Commented out to avoid EntryPointNotFoundException

    // /// <summary>
    // /// Get data size in bytes.
    // /// </summary>
    // [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    // public static extern UIntPtr ktxTexture_GetDataSizeUncompressed(IntPtr texture);

    // /// <summary>
    // /// Get element size (bytes per pixel).
    // /// </summary>
    // [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    // public static extern uint ktxTexture_GetElementSize(IntPtr texture);

    /// <summary>
    /// Iterate through image levels (safer than manual offset calculation).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate KtxErrorCode KtxIterCallback(int miplevel, int face, int width, int height, int depth, UIntPtr imageSize, IntPtr pixels, IntPtr userdata);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture_IterateLevelFaces(IntPtr texture, KtxIterCallback iterCb, IntPtr userdata);

    /// <summary>
    /// Create a new hash list for key-value data.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ktxHashList_Create();

    /// <summary>
    /// Destroy a hash list.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ktxHashList_Destroy(IntPtr hashList);

    /// <summary>
    /// Add a key-value pair (binary data) to the hash list.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxHashList_AddKVPair(
        IntPtr hashList,
        [MarshalAs(UnmanagedType.LPStr)] string key,
        uint valueLen,
        IntPtr value);

    /// <summary>
    /// Serialize hash list to byte array (allocates memory).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxHashList_Serialize(
        IntPtr hashList,
        out uint kvdLen,
        out IntPtr kvd);

    /// <summary>
    /// Write KTX2 texture to file.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern KtxErrorCode ktxTexture2_WriteToNamedFile(
        IntPtr texture,
        [MarshalAs(UnmanagedType.LPStr)] string dstname);

    /// <summary>
    /// Create a new empty KTX2 texture.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture2_Create(
        ref KtxTextureCreateInfo createInfo,
        KtxTextureCreateStorage storageAllocation,
        out IntPtr newTex);

    /// <summary>
    /// Set image data for a specific mip level, layer, and face from memory (KTX2 specific).
    /// Signature: KTX_error_code ktxTexture2_SetImageFromMemory(ktxTexture2 *This, ktx_uint32_t level, ktx_uint32_t layer, ktx_uint32_t faceSlice, const ktx_uint8_t *src, ktx_size_t srcSize)
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture2_SetImageFromMemory(
        IntPtr texture,
        uint level,
        uint layer,
        uint faceSlice,
        IntPtr src,
        nuint srcSize);

    /// <summary>
    /// Compress texture using Basis Universal with extended parameters.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture2_CompressBasisEx(
        IntPtr texture,
        ref KtxBasisParams params_);

    /// <summary>
    /// Compress texture using Basis Universal with simple quality parameter.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern KtxErrorCode ktxTexture2_CompressBasis(
        IntPtr texture,
        uint quality);

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

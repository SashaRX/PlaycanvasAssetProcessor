using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureViewer;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Конвертер текстур в KTX2 используя libktx API напрямую (замена ToktxWrapper)
    /// Полностью заменяет CLI утилиты (toktx.exe, ktx.exe)
    /// </summary>
    public class LibKtxConverter {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string? _ktxDllDirectory;

        public LibKtxConverter(string? ktxDllDirectory = null) {
            _ktxDllDirectory = ktxDllDirectory;
        }

        /// <summary>
        /// Упаковывает мипмапы в KTX2 формат с Basis Universal сжатием
        /// </summary>
        /// <param name="mipmapPaths">Пути к мипмапам (level 0 = base)</param>
        /// <param name="outputPath">Путь к выходному KTX2 файлу</param>
        /// <param name="settings">Настройки сжатия</param>
        /// <param name="kvdBinaryFiles">Key-Value метаданные (опционально)</param>
        public async Task<ToktxResult> PackMipmapsAsync(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings,
            Dictionary<string, string>? kvdBinaryFiles = null) {

            var result = new ToktxResult();

            try {
                // Загружаем ktx.dll
                Logger.Info("=== KTX.DLL LOADING ===");

                if (!string.IsNullOrEmpty(_ktxDllDirectory)) {
                    Logger.Info($"Trying to load ktx.dll from toktx directory: {_ktxDllDirectory}");
                } else {
                    Logger.Info("No toktx directory specified, will search in exe directory");
                }

                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                Logger.Info($"Exe directory: {exeDir}");
                Logger.Info($"Looking for ktx.dll in:");
                Logger.Info($"  1. {Path.Combine(exeDir, "ktx.dll")} (next to AssetProcessor.exe)");

                if (!string.IsNullOrEmpty(_ktxDllDirectory)) {
                    Logger.Info($"  2. {Path.Combine(_ktxDllDirectory, "ktx.dll")} (toktx directory)");
                }

                if (!LibKtxNative.LoadKtxDll(_ktxDllDirectory)) {
                    result.Error = "Failed to load ktx.dll. Please place ktx.dll next to AssetProcessor.exe or specify correct toktx directory.";
                    Logger.Error(result.Error);
                    Logger.Error($"DLL status: {LibKtxNative.GetLoadStatus()}");
                    return result;
                }

                Logger.Info($"✓ {LibKtxNative.GetLoadStatus()}");

                Logger.Info($"=== LIBKTX TEXTURE CONVERSION START ===");
                Logger.Info($"  Mipmaps: {mipmapPaths.Count}");
                Logger.Info($"  Format: {settings.CompressionFormat}");
                Logger.Info($"  Output: {outputPath}");

                // Загружаем все мипмапы в память
                var mipmaps = new List<Image<Rgba32>>();
                try {
                    foreach (var path in mipmapPaths) {
                        var img = await Image.LoadAsync<Rgba32>(path);
                        mipmaps.Add(img);
                        Logger.Info($"  Loaded mip level {mipmaps.Count - 1}: {img.Width}x{img.Height}");
                    }

                    // Создаём текстуру через libktx
                    await Task.Run(() => {
                        CreateAndCompressTexture(mipmaps, outputPath, settings, kvdBinaryFiles);
                    });

                    result.Success = true;
                    var fileInfo = new FileInfo(outputPath);
                    result.OutputFileSize = fileInfo.Length;
                    result.Output = $"Successfully created KTX2: {outputPath} ({fileInfo.Length} bytes)";

                    Logger.Info($"=== LIBKTX TEXTURE CONVERSION SUCCESS ===");
                    Logger.Info($"  Output file size: {fileInfo.Length} bytes");

                } finally {
                    // Освобождаем изображения
                    foreach (var img in mipmaps) {
                        img.Dispose();
                    }
                }

            } catch (Exception ex) {
                Logger.Error(ex, "LibKtxConverter error");
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Создаёт и сжимает текстуру используя libktx API
        /// </summary>
        private void CreateAndCompressTexture(
            List<Image<Rgba32>> mipmaps,
            string outputPath,
            CompressionSettings settings,
            Dictionary<string, string>? kvdBinaryFiles) {

            if (mipmaps.Count == 0) {
                throw new Exception("No mipmaps provided");
            }

            var baseMip = mipmaps[0];

            // 1. Создаём структуру создания текстуры
            var createInfo = new LibKtxNative.KtxTextureCreateInfo {
                vkFormat = (uint)LibKtxNative.VkFormat.VK_FORMAT_R8G8B8A8_UNORM,
                baseWidth = (uint)baseMip.Width,
                baseHeight = (uint)baseMip.Height,
                baseDepth = 1,
                numDimensions = 2,
                numLevels = (uint)mipmaps.Count,
                numLayers = 1,
                numFaces = 1,
                isArray = 0,
                generateMipmaps = 0
            };

            IntPtr texturePtr = IntPtr.Zero;

            try {
                Logger.Info("Creating KTX2 texture structure...");
                var result = LibKtxNative.ktxTexture2_Create(
                    ref createInfo,
                    LibKtxNative.KtxTextureCreateStorage.KTX_TEXTURE_CREATE_ALLOC_STORAGE,
                    out texturePtr
                );

                if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                    throw new Exception($"Failed to create KTX2 texture: {LibKtxNative.GetErrorString(result)}");
                }

                Logger.Info("✓ KTX2 texture structure created");

                // 2. Загружаем данные мипмапов
                Logger.Info("Loading mipmap data...");
                for (int level = 0; level < mipmaps.Count; level++) {
                    var mip = mipmaps[level];

                    // Получаем raw pixel data (RGBA8)
                    int pixelCount = mip.Width * mip.Height;
                    int dataSize = pixelCount * 4; // RGBA = 4 bytes per pixel
                    byte[] pixelData = new byte[dataSize];

                    // Копируем данные пикселей
                    mip.CopyPixelDataTo(pixelData);

                    // Загружаем в текстуру
                    IntPtr dataPtr = Marshal.AllocHGlobal(dataSize);
                    try {
                        Marshal.Copy(pixelData, 0, dataPtr, dataSize);

                        result = LibKtxNative.ktxTexture_SetImageFromMemory(
                            texturePtr,
                            (uint)level,
                            0, // layer
                            0, // faceSlice
                            dataPtr,
                            (UIntPtr)dataSize
                        );

                        if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                            throw new Exception($"Failed to set image data for level {level}: {LibKtxNative.GetErrorString(result)}");
                        }

                        Logger.Info($"  ✓ Loaded mip level {level}: {mip.Width}x{mip.Height} ({dataSize} bytes)");
                    } finally {
                        Marshal.FreeHGlobal(dataPtr);
                    }
                }

                // 3. Добавляем Key-Value метаданные ПЕРЕД сжатием
                if (kvdBinaryFiles != null && kvdBinaryFiles.Count > 0) {
                    Logger.Info("Adding Key-Value metadata...");
                    AddMetadata(texturePtr, kvdBinaryFiles);
                }

                // 4. Сжимаем в Basis Universal
                Logger.Info("Compressing with Basis Universal...");
                CompressTexture(texturePtr, settings);

                // 5. Сохраняем в файл
                Logger.Info($"Writing KTX2 file: {outputPath}");
                result = LibKtxNative.ktxTexture2_WriteToNamedFile(texturePtr, outputPath);

                if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                    throw new Exception($"Failed to write KTX2 file: {LibKtxNative.GetErrorString(result)}");
                }

                Logger.Info("✓ KTX2 file written successfully");

            } finally {
                // Освобождаем ресурсы
                if (texturePtr != IntPtr.Zero) {
                    LibKtxNative.ktxTexture2_Destroy(texturePtr);
                }
            }
        }

        /// <summary>
        /// Сжимает текстуру в Basis Universal
        /// </summary>
        private void CompressTexture(IntPtr texturePtr, CompressionSettings settings) {
            bool isUASTC = settings.CompressionFormat == CompressionFormat.UASTC;

            Logger.Info($"  Format: {(isUASTC ? "UASTC" : "ETC1S")}");
            Logger.Info($"  Quality Level: {settings.QualityLevel}");
            Logger.Info($"  Compression Level: {settings.CompressionLevel}");

            // Создаём параметры сжатия
            var basisParams = new LibKtxNative.KtxBasisParams {
                structSize = (uint)Marshal.SizeOf<LibKtxNative.KtxBasisParams>(),
                uastc = (byte)(isUASTC ? 1 : 0),
                verbose = 0,
                noSSE = 0,
                threadCount = 0, // 0 = auto

                // ETC1S параметры
                compressionLevel = (uint)settings.CompressionLevel,
                qualityLevel = (uint)settings.QualityLevel,
                maxEndpoints = 0,
                endpointRDOThreshold = 0,
                maxSelectors = 0,
                selectorRDOThreshold = 0
            };

            Logger.Info($"  Basis params struct size: {basisParams.structSize}");

            var result = LibKtxNative.ktxTexture2_CompressBasisEx(texturePtr, ref basisParams);

            if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                throw new Exception($"Basis compression failed: {LibKtxNative.GetErrorString(result)}");
            }

            Logger.Info("✓ Basis Universal compression completed");
        }

        /// <summary>
        /// Добавляет Key-Value метаданные в текстуру
        /// </summary>
        private void AddMetadata(IntPtr texturePtr, Dictionary<string, string> kvdBinaryFiles) {
            foreach (var kvPair in kvdBinaryFiles) {
                Logger.Info($"  Adding metadata: key='{kvPair.Key}', file='{kvPair.Value}'");

                if (!File.Exists(kvPair.Value)) {
                    Logger.Warn($"  Metadata file not found: {kvPair.Value}");
                    continue;
                }

                byte[] metadataBytes = File.ReadAllBytes(kvPair.Value);
                Logger.Info($"  Metadata size: {metadataBytes.Length} bytes");

                // Получаем offset до kvDataHead
                int kvDataHeadOffset = Marshal.OffsetOf<LibKtxNative.KtxTexture2>("kvDataHead").ToInt32();
                IntPtr kvDataHeadPtr = IntPtr.Add(texturePtr, kvDataHeadOffset);

                // Копируем метаданные в unmanaged память
                IntPtr metadataPtr = Marshal.AllocHGlobal(metadataBytes.Length);
                try {
                    Marshal.Copy(metadataBytes, 0, metadataPtr, metadataBytes.Length);

                    var result = LibKtxNative.ktxHashList_AddKVPair(
                        kvDataHeadPtr,
                        kvPair.Key,
                        (uint)metadataBytes.Length,
                        metadataPtr
                    );

                    if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                        Logger.Warn($"  Failed to add metadata '{kvPair.Key}': {LibKtxNative.GetErrorString(result)}");
                    } else {
                        Logger.Info($"  ✓ Metadata '{kvPair.Key}' added successfully");
                    }
                } finally {
                    Marshal.FreeHGlobal(metadataPtr);
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using AssetProcessor.TextureViewer;
using NLog;

namespace AssetProcessor.TextureConversion.KVD {
    /// <summary>
    /// Инжектирует TLV metadata в существующий KTX2 файл используя libktx API
    /// </summary>
    public class Ktx2MetadataInjector {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Инжектирует binary metadata (TLV файл) в KTX2 файл
        /// </summary>
        /// <param name="ktx2FilePath">Путь к существующему KTX2 файлу</param>
        /// <param name="tlvFilePath">Путь к TLV binary файлу с метаданными</param>
        /// <param name="key">Ключ для метаданных (по умолчанию "pc.meta")</param>
        public static bool InjectMetadata(string ktx2FilePath, string tlvFilePath, string key = "pc.meta") {
            if (!File.Exists(ktx2FilePath)) {
                Logger.Error($"KTX2 file not found: {ktx2FilePath}");
                return false;
            }

            if (!File.Exists(tlvFilePath)) {
                Logger.Error($"TLV metadata file not found: {tlvFilePath}");
                return false;
            }

            Logger.Info($"=== KTX2 METADATA INJECTION START ===");
            Logger.Info($"  KTX2 file: {ktx2FilePath}");
            Logger.Info($"  TLV file: {tlvFilePath}");
            Logger.Info($"  Key: {key}");

            // Читаем TLV файл
            byte[] tlvData;
            try {
                tlvData = File.ReadAllBytes(tlvFilePath);
                Logger.Info($"  TLV size: {tlvData.Length} bytes");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to read TLV file: {tlvFilePath}");
                return false;
            }

            // Загружаем KTX2 файл
            IntPtr texturePtr = IntPtr.Zero;

            try {
                Logger.Info("Loading KTX2 file with libktx...");
                var result = LibKtxNative.ktxTexture2_CreateFromNamedFile(
                    ktx2FilePath,
                    (uint)LibKtxNative.KtxTextureCreateFlagBits.KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT,
                    out texturePtr
                );

                if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                    Logger.Error($"Failed to load KTX2 file: {LibKtxNative.GetErrorString(result)}");
                    return false;
                }

                Logger.Info("KTX2 file loaded successfully");

                // Получаем структуру текстуры для доступа к kvDataHead
                var texture = Marshal.PtrToStructure<LibKtxNative.KtxTexture2>(texturePtr);

                // Вычисляем offset до поля kvDataHead (IntPtr находится на определённом смещении)
                // kvDataHead - это 26-е поле в структуре (после orientation)
                // Размер полей до kvDataHead: нужно точно вычислить
                int kvDataHeadOffset = Marshal.OffsetOf<LibKtxNative.KtxTexture2>("kvDataHead").ToInt32();
                IntPtr kvDataHeadPtr = IntPtr.Add(texturePtr, kvDataHeadOffset);

                Logger.Info($"kvDataHead offset: {kvDataHeadOffset}");

                // Копируем TLV data в unmanaged память
                IntPtr tlvPtr = Marshal.AllocHGlobal(tlvData.Length);
                try {
                    Marshal.Copy(tlvData, 0, tlvPtr, tlvData.Length);

                    // Добавляем KV пару напрямую в texture->kvDataHead
                    Logger.Info($"Adding KV pair to texture->kvDataHead: key='{key}', valueLen={tlvData.Length}");
                    result = LibKtxNative.ktxHashList_AddKVPair(
                        kvDataHeadPtr,
                        key,
                        (uint)tlvData.Length,
                        tlvPtr
                    );

                    if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                        Logger.Error($"Failed to add KV pair: {LibKtxNative.GetErrorString(result)}");
                        return false;
                    }

                    Logger.Info("KV pair added successfully to texture kvDataHead");
                } finally {
                    Marshal.FreeHGlobal(tlvPtr);
                }

                // Сохраняем KTX2 файл с metadata
                Logger.Info($"Writing KTX2 file with metadata...");
                result = LibKtxNative.ktxTexture2_WriteToNamedFile(texturePtr, ktx2FilePath);
                if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                    Logger.Error($"Failed to write KTX2 file: {LibKtxNative.GetErrorString(result)}");
                    return false;
                }

                var fileInfo = new FileInfo(ktx2FilePath);
                Logger.Info($"=== KTX2 METADATA INJECTION SUCCESS ===");
                Logger.Info($"  Output file: {ktx2FilePath}");
                Logger.Info($"  File size: {fileInfo.Length} bytes");
                Logger.Info($"  Metadata key: {key}");
                Logger.Info($"  Metadata size: {tlvData.Length} bytes");

                return true;

            } catch (Exception ex) {
                Logger.Error(ex, "Exception during metadata injection");
                return false;
            } finally {
                // Освобождаем ресурсы
                if (texturePtr != IntPtr.Zero) {
                    LibKtxNative.ktxTexture2_Destroy(texturePtr);
                }
            }
        }
    }
}

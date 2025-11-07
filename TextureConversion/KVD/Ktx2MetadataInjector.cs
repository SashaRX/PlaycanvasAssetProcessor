using System;
using System.IO;
using NLog;

namespace AssetProcessor.TextureConversion.KVD {
    /// <summary>
    /// Инжектирует TLV metadata в существующий KTX2 файл через прямое редактирование бинарного формата
    /// Использует Ktx2BinaryPatcher вместо libktx P/Invoke для надёжности
    /// </summary>
    public class Ktx2MetadataInjector {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Инжектирует binary metadata (TLV файл) в KTX2 файл
        /// </summary>
        /// <param name="ktx2FilePath">Путь к существующему KTX2 файлу</param>
        /// <param name="tlvFilePath">Путь к TLV binary файлу с метаданными</param>
        /// <param name="key">Ключ для метаданных (по умолчанию "pc.meta")</param>
        /// <param name="ktxDllDirectory">НЕ ИСПОЛЬЗУЕТСЯ (оставлен для совместимости API)</param>
        public static bool InjectMetadata(string ktx2FilePath, string tlvFilePath, string key = "pc.meta", string? ktxDllDirectory = null) {
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

            // Используем binary patching вместо libktx P/Invoke для надёжности
            bool success = Ktx2BinaryPatcher.AddKeyValueData(ktx2FilePath, key, tlvData);

            if (success) {
                var fileInfo = new FileInfo(ktx2FilePath);
                Logger.Info($"=== KTX2 METADATA INJECTION SUCCESS ===");
                Logger.Info($"  Output file: {ktx2FilePath}");
                Logger.Info($"  File size: {fileInfo.Length} bytes");
                Logger.Info($"  Metadata key: {key}");
                Logger.Info($"  Metadata size: {tlvData.Length} bytes");
            }

            return success;
        }
    }
}

using System;
using System.IO;
using System.Text;
using NLog;

namespace AssetProcessor.TextureConversion.KVD {
    /// <summary>
    /// Добавляет Key-Value Data в KTX2 файл через прямую модификацию бинарного формата
    /// Безопаснее чем libktx P/Invoke, так как не зависит от выравнивания структур
    /// </summary>
    public class Ktx2BinaryPatcher {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // KTX2 identifier
        private static readonly byte[] KTX2_IDENTIFIER = new byte[] {
            0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A
        };

        /// <summary>
        /// Добавляет Key-Value пару в KTX2 файл
        /// </summary>
        public static bool AddKeyValueData(string ktx2FilePath, string key, byte[] value) {
            try {
                Logger.Info($"=== KTX2 BINARY PATCHING START ===");
                Logger.Info($"  File: {ktx2FilePath}");
                Logger.Info($"  Key: {key}");
                Logger.Info($"  Value size: {value.Length} bytes");

                // Читаем весь файл
                byte[] fileData = File.ReadAllBytes(ktx2FilePath);
                Logger.Info($"  File size: {fileData.Length} bytes");

                // Проверяем identifier
                for (int i = 0; i < 12; i++) {
                    if (fileData[i] != KTX2_IDENTIFIER[i]) {
                        Logger.Error("Invalid KTX2 file identifier");
                        return false;
                    }
                }

                // Читаем header
                uint vkFormat = BitConverter.ToUInt32(fileData, 12);
                uint typeSize = BitConverter.ToUInt32(fileData, 16);
                uint pixelWidth = BitConverter.ToUInt32(fileData, 20);
                uint pixelHeight = BitConverter.ToUInt32(fileData, 24);
                uint pixelDepth = BitConverter.ToUInt32(fileData, 28);
                uint layerCount = BitConverter.ToUInt32(fileData, 32);
                uint faceCount = BitConverter.ToUInt32(fileData, 36);
                uint levelCount = BitConverter.ToUInt32(fileData, 40);
                uint supercompressionScheme = BitConverter.ToUInt32(fileData, 44);

                // Index section (48-60)
                uint dfdByteOffset = BitConverter.ToUInt32(fileData, 48);
                uint dfdByteLength = BitConverter.ToUInt32(fileData, 52);
                uint kvdByteOffset = BitConverter.ToUInt32(fileData, 56);
                uint kvdByteLength = BitConverter.ToUInt32(fileData, 60);
                uint sgdByteOffset = BitConverter.ToUInt32(fileData, 64);
                uint sgdByteLength = BitConverter.ToUInt32(fileData, 68);

                Logger.Info($"  Texture: {pixelWidth}x{pixelHeight}, {levelCount} levels");
                Logger.Info($"  Current kvdByteOffset: {kvdByteOffset}");
                Logger.Info($"  Current kvdByteLength: {kvdByteLength}");

                // Формируем новую KV пару
                // Формат: keyAndValueByteLength (4) + key (null-terminated) + value + padding
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                int kvPairDataLength = keyBytes.Length + 1 + value.Length; // +1 для null terminator
                int paddedLength = (kvPairDataLength + 3) & ~3; // Выравнивание до 4 байт
                int paddingBytes = paddedLength - kvPairDataLength;

                Logger.Info($"  KV pair data length: {kvPairDataLength}");
                Logger.Info($"  Padded length: {paddedLength}");
                Logger.Info($"  Padding bytes: {paddingBytes}");

                // Собираем KV пару
                byte[] newKvPair = new byte[4 + paddedLength];
                BitConverter.GetBytes((uint)kvPairDataLength).CopyTo(newKvPair, 0);
                keyBytes.CopyTo(newKvPair, 4);
                newKvPair[4 + keyBytes.Length] = 0; // null terminator
                value.CopyTo(newKvPair, 4 + keyBytes.Length + 1);
                // padding остаётся нулями

                // Вставляем KV данные в файл
                byte[] newFileData;
                uint insertOffset;

                if (kvdByteLength == 0) {
                    // Нет существующих KVD - создаём новую секцию
                    Logger.Info("  No existing KVD, creating new section");

                    // kvdByteOffset обычно указывает после Level Index Array
                    // Если kvdByteOffset == 0, используем позицию после header + level index
                    insertOffset = kvdByteOffset;
                    if (insertOffset == 0) {
                        insertOffset = 80 + (levelCount * 24); // header (80) + level index array
                    }

                    newFileData = new byte[fileData.Length + newKvPair.Length];

                    // Копируем данные до insertOffset
                    Array.Copy(fileData, 0, newFileData, 0, (int)insertOffset);

                    // Вставляем новую KV пару
                    Array.Copy(newKvPair, 0, newFileData, (int)insertOffset, newKvPair.Length);

                    // Копируем остальные данные
                    Array.Copy(fileData, (int)insertOffset, newFileData, (int)insertOffset + newKvPair.Length,
                        fileData.Length - (int)insertOffset);

                    // Обновляем header
                    if (kvdByteOffset == 0) {
                        BitConverter.GetBytes(insertOffset).CopyTo(newFileData, 56);
                    }
                    BitConverter.GetBytes((uint)newKvPair.Length).CopyTo(newFileData, 60);

                    // Корректируем остальные offset'ы в header
                    if (dfdByteOffset >= insertOffset) {
                        BitConverter.GetBytes(dfdByteOffset + (uint)newKvPair.Length).CopyTo(newFileData, 48);
                    }
                    if (sgdByteOffset >= insertOffset && sgdByteOffset > 0) {
                        BitConverter.GetBytes(sgdByteOffset + (uint)newKvPair.Length).CopyTo(newFileData, 64);
                    }

                } else {
                    // Добавляем к существующим KVD
                    Logger.Info("  Appending to existing KVD");

                    insertOffset = kvdByteOffset + kvdByteLength;
                    newFileData = new byte[fileData.Length + newKvPair.Length];

                    Array.Copy(fileData, 0, newFileData, 0, (int)insertOffset);
                    Array.Copy(newKvPair, 0, newFileData, (int)insertOffset, newKvPair.Length);
                    Array.Copy(fileData, (int)insertOffset, newFileData, (int)insertOffset + newKvPair.Length,
                        fileData.Length - (int)insertOffset);

                    // Обновляем kvdByteLength
                    BitConverter.GetBytes(kvdByteLength + (uint)newKvPair.Length).CopyTo(newFileData, 60);

                    // Корректируем offset'ы после KVD в header
                    if (dfdByteOffset > insertOffset) {
                        BitConverter.GetBytes(dfdByteOffset + (uint)newKvPair.Length).CopyTo(newFileData, 48);
                    }
                    if (sgdByteOffset > insertOffset && sgdByteOffset > 0) {
                        BitConverter.GetBytes(sgdByteOffset + (uint)newKvPair.Length).CopyTo(newFileData, 64);
                    }
                }

                // КРИТИЧНО: Обновляем Level Index Array!
                // Каждый level имеет структуру: byteOffset (8), byteLength (8), uncompressedByteLength (8)
                Logger.Info("  Updating Level Index Array offsets...");
                int levelIndexOffset = 80; // Level Index Array начинается после header
                for (uint i = 0; i < levelCount; i++) {
                    int currentLevelOffset = levelIndexOffset + (int)(i * 24);
                    ulong levelByteOffset = BitConverter.ToUInt64(newFileData, currentLevelOffset);

                    // Если offset уровня больше точки вставки - корректируем его
                    if (levelByteOffset >= insertOffset) {
                        ulong newLevelByteOffset = levelByteOffset + (uint)newKvPair.Length;
                        BitConverter.GetBytes(newLevelByteOffset).CopyTo(newFileData, currentLevelOffset);
                        Logger.Info($"    Level {i}: offset {levelByteOffset} -> {newLevelByteOffset}");
                    }
                }

                // Записываем файл обратно
                File.WriteAllBytes(ktx2FilePath, newFileData);

                Logger.Info($"=== KTX2 BINARY PATCHING SUCCESS ===");
                Logger.Info($"  New file size: {newFileData.Length} bytes");
                Logger.Info($"  KV pair added: {key} ({value.Length} bytes)");

                return true;

            } catch (Exception ex) {
                Logger.Error(ex, "KTX2 binary patching failed");
                return false;
            }
        }
    }
}

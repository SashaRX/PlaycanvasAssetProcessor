using System.IO;
using System.Linq;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.KVD {
    /// <summary>
    /// Writer для TLV (Type-Length-Value) формата KTX2 Key-Value Data
    /// </summary>
    public class TLVWriter {
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;

        public TLVWriter() {
            _stream = new MemoryStream();
            _writer = new BinaryWriter(_stream);
        }

        /// <summary>
        /// Записывает TLV блок
        /// </summary>
        /// <param name="type">Тип блока</param>
        /// <param name="flags">Флаги/модификаторы</param>
        /// <param name="payload">Данные блока</param>
        public void WriteTLV(TLVType type, byte flags, byte[] payload) {
            // Type (1 byte)
            _writer.Write((byte)type);

            // Flags (1 byte)
            _writer.Write(flags);

            // Length (2 bytes, little-endian)
            ushort length = (ushort)payload.Length;
            _writer.Write(length);

            // Payload
            _writer.Write(payload);

            // Padding to 4-byte alignment
            int padding = (4 - (payload.Length & 3)) & 3;
            for (int i = 0; i < padding; i++) {
                _writer.Write((byte)0);
            }
        }

        /// <summary>
        /// Записывает результат анализа гистограммы
        /// </summary>
        public void WriteHistogramResult(HistogramResult result, HistogramQuantization quantization = HistogramQuantization.Half16) {
            if (result.Mode == HistogramMode.Off || !result.Success) {
                // Не записываем ничего если анализ отключён или не успешен
                return;
            }

            // Определяем тип TLV блока в зависимости от количества каналов
            TLVType tlvType;
            byte[] payload;

            if (result.ChannelMode == HistogramChannelMode.AverageLuminance) {
                // HIST_SCALAR: один scale/offset для всех каналов
                tlvType = TLVType.HIST_SCALAR;
                payload = QuantizeScaleOffset(result.Scale[0], result.Offset[0], quantization);
            } else if (result.ChannelMode == HistogramChannelMode.RGBOnly) {
                // HIST_RGB: общий scale/offset для RGB
                tlvType = TLVType.HIST_RGB;
                payload = QuantizeScaleOffset(result.Scale[0], result.Offset[0], quantization);
            } else if (result.ChannelMode == HistogramChannelMode.PerChannel) {
                // HIST_PER_CHANNEL_3: поканально для RGB
                tlvType = TLVType.HIST_PER_CHANNEL_3;
                payload = QuantizeScaleOffsetArray(result.Scale, result.Offset, 3, quantization);
            } else if (result.ChannelMode == HistogramChannelMode.PerChannelRGBA) {
                // HIST_PER_CHANNEL_4: поканально для RGBA
                tlvType = TLVType.HIST_PER_CHANNEL_4;
                payload = QuantizeScaleOffsetArray(result.Scale, result.Offset, 4, quantization);
            } else {
                return; // Unknown channel mode
            }

            // Flags: версия в битах [7:4], квантование в [3:2], резерв в [1:0]
            byte flags = (byte)(0x10 | ((byte)quantization << 2)); // версия 1 + квантование

            WriteTLV(tlvType, flags, payload);
        }

        /// <summary>
        /// Квантует scale и offset в выбранный формат
        /// </summary>
        private byte[] QuantizeScaleOffset(float scale, float offset, HistogramQuantization quantization) {
            return quantization switch {
                HistogramQuantization.Half16 => HalfHelper.FloatsToHalfBytes(scale, offset),
                HistogramQuantization.PackedUInt32 => HalfHelper.PackScaleOffsetToUInt32(scale, offset),
                HistogramQuantization.Float32 => HalfHelper.FloatsToFloat32Bytes(scale, offset),
                _ => HalfHelper.FloatsToHalfBytes(scale, offset)
            };
        }

        /// <summary>
        /// Квантует массивы scale/offset в выбранный формат
        /// </summary>
        private byte[] QuantizeScaleOffsetArray(float[] scale, float[] offset, int count, HistogramQuantization quantization) {
            if (quantization == HistogramQuantization.PackedUInt32) {
                // Для packed uint32 упаковываем каждую пару отдельно
                var result = new byte[count * 4];
                for (int i = 0; i < count; i++) {
                    var packed = HalfHelper.PackScaleOffsetToUInt32(scale[i], offset[i]);
                    Array.Copy(packed, 0, result, i * 4, 4);
                }
                return result;
            } else if (quantization == HistogramQuantization.Float32) {
                // Float32: сначала все scale, потом все offset
                var scaleBytes = HalfHelper.FloatsToFloat32Bytes(scale.Take(count).ToArray());
                var offsetBytes = HalfHelper.FloatsToFloat32Bytes(offset.Take(count).ToArray());
                return scaleBytes.Concat(offsetBytes).ToArray();
            } else {
                // Half16: сначала все scale, потом все offset
                var scaleBytes = HalfHelper.FloatsToHalfBytes(scale.Take(count).ToArray());
                var offsetBytes = HalfHelper.FloatsToHalfBytes(offset.Take(count).ToArray());
                return scaleBytes.Concat(offsetBytes).ToArray();
            }
        }

        /// <summary>
        /// Записывает параметры анализа гистограммы (опционально)
        /// </summary>
        public void WriteHistogramParams(HistogramSettings settings) {
            if (settings.Mode == HistogramMode.Off) {
                return;
            }

            // Payload: pLow, pHigh, knee (3 half floats)
            byte[] payload = HalfHelper.FloatsToHalfBytes(
                settings.PercentileLow,
                settings.PercentileHigh,
                settings.KneeWidth
            );

            // Flags: режим в битах [3:0]
            byte flags = (byte)settings.Mode;

            WriteTLV(TLVType.HIST_PARAMS, flags, payload);
        }

        /// <summary>
        /// Записывает схему хранения нормалей
        /// </summary>
        public void WriteNormalLayout(NormalLayout layout) {
            if (layout == NormalLayout.NONE) {
                return; // Не записываем если нет нормалей
            }

            byte flags = (byte)layout;
            byte[] emptyPayload = Array.Empty<byte>();

            WriteTLV(TLVType.NORMAL_LAYOUT, flags, emptyPayload);
        }

        /// <summary>
        /// Возвращает финальные данные TLV
        /// </summary>
        public byte[] ToArray() {
            _writer.Flush();
            return _stream.ToArray();
        }

        /// <summary>
        /// Сохраняет TLV данные в файл
        /// </summary>
        public void SaveToFile(string filePath) {
            File.WriteAllBytes(filePath, ToArray());
        }

        public void Dispose() {
            _writer?.Dispose();
            _stream?.Dispose();
        }
    }

    /// <summary>
    /// Reader для TLV формата (для отладки и верификации)
    /// </summary>
    public class TLVReader {
        private readonly BinaryReader _reader;

        public TLVReader(byte[] data) {
            _reader = new BinaryReader(new MemoryStream(data));
        }

        /// <summary>
        /// Читает следующий TLV блок
        /// </summary>
        public (TLVType type, byte flags, byte[] payload)? ReadNext() {
            if (_reader.BaseStream.Position >= _reader.BaseStream.Length) {
                return null;
            }

            try {
                byte type = _reader.ReadByte();
                byte flags = _reader.ReadByte();
                ushort length = _reader.ReadUInt16();

                byte[] payload = _reader.ReadBytes(length);

                // Skip padding
                int padding = (4 - (length & 3)) & 3;
                _reader.BaseStream.Seek(padding, SeekOrigin.Current);

                return ((TLVType)type, flags, payload);
            } catch {
                return null;
            }
        }

        /// <summary>
        /// Читает все TLV блоки
        /// </summary>
        public List<(TLVType type, byte flags, byte[] payload)> ReadAll() {
            var blocks = new List<(TLVType, byte, byte[])>();

            while (true) {
                var block = ReadNext();
                if (block == null) break;
                blocks.Add(block.Value);
            }

            return blocks;
        }

        public void Dispose() {
            _reader?.Dispose();
        }
    }
}

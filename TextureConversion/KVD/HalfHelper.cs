using System.Runtime.InteropServices;

namespace AssetProcessor.TextureConversion.KVD {
    /// <summary>
    /// Вспомогательный класс для работы с Half float (16-bit)
    /// </summary>
    public static class HalfHelper {
        /// <summary>
        /// Конвертирует float в half (16-bit)
        /// </summary>
        public static ushort FloatToHalf(float value) {
            // Используем BitConverter для конвертации
            // Альтернатива: можно использовать System.Half из .NET 5+

            // Для .NET 5+ можно использовать:
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
                Half half = (Half)value;
                return BitConverter.ToUInt16(BitConverter.GetBytes(half));
            }

            // Fallback: ручная конвертация
            return FloatToHalfManual(value);
        }

        /// <summary>
        /// Конвертирует half (16-bit) в float
        /// </summary>
        public static float HalfToFloat(ushort half) {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
                byte[] bytes = BitConverter.GetBytes(half);
                Half h = BitConverter.ToHalf(bytes);
                return (float)h;
            }

            return HalfToFloatManual(half);
        }

        /// <summary>
        /// Ручная конвертация float -> half (fallback)
        /// </summary>
        private static ushort FloatToHalfManual(float value) {
            int bits = BitConverter.SingleToInt32Bits(value);

            int sign = (bits >> 16) & 0x8000;
            int exponent = ((bits >> 23) & 0xFF) - 127 + 15;
            int mantissa = bits & 0x7FFFFF;

            if (exponent <= 0) {
                // Denormalized or zero
                if (exponent < -10) {
                    return (ushort)sign; // Too small, return signed zero
                }
                mantissa = (mantissa | 0x800000) >> (1 - exponent);
                return (ushort)(sign | (mantissa >> 13));
            }

            if (exponent >= 0x1F) {
                // Infinity or NaN
                return (ushort)(sign | 0x7C00);
            }

            return (ushort)(sign | (exponent << 10) | (mantissa >> 13));
        }

        /// <summary>
        /// Ручная конвертация half -> float (fallback)
        /// </summary>
        private static float HalfToFloatManual(ushort half) {
            int sign = (half >> 15) & 0x1;
            int exponent = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            if (exponent == 0) {
                if (mantissa == 0) {
                    // Signed zero
                    return BitConverter.Int32BitsToSingle(sign << 31);
                }
                // Denormalized
                exponent = 1;
            } else if (exponent == 0x1F) {
                // Infinity or NaN
                return BitConverter.Int32BitsToSingle((sign << 31) | 0x7F800000 | (mantissa << 13));
            }

            exponent = exponent + (127 - 15);
            mantissa = mantissa << 13;

            int bits = (sign << 31) | (exponent << 23) | mantissa;
            return BitConverter.Int32BitsToSingle(bits);
        }

        /// <summary>
        /// Конвертирует массив float в массив half (bytes)
        /// </summary>
        public static byte[] FloatsToHalfBytes(params float[] values) {
            var result = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++) {
                ushort half = FloatToHalf(values[i]);
                result[i * 2] = (byte)(half & 0xFF);
                result[i * 2 + 1] = (byte)((half >> 8) & 0xFF);
            }
            return result;
        }
    }
}

using System;
using AssetProcessor.TextureConversion.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Модификатор Toksvig для коррекции roughness/gloss на основе карты нормалей
    ///
    /// Алгоритм Toksvig вычисляет модифицированный roughness учитывая
    /// дисперсию нормалей в пикселе. Это предотвращает чрезмерно яркие
    /// блики на мипмапах с усредненными нормалями.
    ///
    /// Формула опирается на вычисление дисперсии нормалей:
    /// variance = (1 - |avgNormal|) / |avgNormal|
    /// После этого применяется Toksvig-коррекция для GGX-roughness
    /// (адаптирована из реализации Unreal Engine).
    ///
    /// Ссылки:
    /// - Toksvig, M. (2005). "Mipmapping Normal Maps"
    /// - https://blog.selfshadow.com/publications/blending-in-detail/
    /// </summary>
    public class ToksvigModifier : IMipModifier {
        public string Name => "Toksvig Gloss/Roughness Modifier";

        private readonly Image<Rgba32>? _normalMap;
        private readonly bool _isGloss; // true для gloss, false для roughness

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="normalMap">Карта нормалей (опционально, если null - не применяется)</param>
        /// <param name="isGloss">Модифицировать gloss (true) или roughness (false)</param>
        public ToksvigModifier(Image<Rgba32>? normalMap, bool isGloss = false) {
            _normalMap = normalMap;
            _isGloss = isGloss;
        }

        public Image<Rgba32> Apply(Image<Rgba32> mipImage, int mipLevel, Image<Rgba32>? originalImage = null) {
            if (_normalMap == null || mipImage.Width == 0 || mipImage.Height == 0) {
                return mipImage.Clone();
            }

            // Базовый уровень обычно не корректируем, чтобы сохранить детали
            if (mipLevel == 0) {
                return mipImage.Clone();
            }

            var corrected = mipImage.Clone();

            float scaleX = (float)_normalMap.Width / mipImage.Width;
            float scaleY = (float)_normalMap.Height / mipImage.Height;

            for (int y = 0; y < mipImage.Height; y++) {
                var sourceRow = mipImage.GetPixelRowSpan(y);
                var destRow = corrected.GetPixelRowSpan(y);

                for (int x = 0; x < mipImage.Width; x++) {
                    var pixel = sourceRow[x];
                    float value = pixel.R / 255.0f;

                    float roughness = _isGloss ? GlossToRoughness(value) : value;
                    float avgNormalLength = ComputeAverageNormalLength(x, y, scaleX, scaleY);
                    float correctedRoughness = CalculateToksvigRoughness(roughness, avgNormalLength);

                    float outputValue = _isGloss ? RoughnessToGloss(correctedRoughness) : correctedRoughness;
                    byte outputByte = (byte)Math.Clamp(outputValue * 255.0f, 0.0f, 255.0f);

                    destRow[x] = new Rgba32(outputByte, outputByte, outputByte, pixel.A);
                }
            }

            return corrected;
        }

        public bool IsApplicable(TextureType textureType) {
            // Применимо только к roughness и gloss картам
            return textureType == TextureType.Roughness ||
                   textureType == TextureType.Gloss;
        }

        /// <summary>
        /// Вычисляет модифицированный roughness по формуле Toksvig
        /// </summary>
        private float CalculateToksvigRoughness(float originalRoughness, float normalLengthAverage) {
            const float Epsilon = 1e-5f;

            float clampedLength = Math.Clamp(normalLengthAverage, Epsilon, 1.0f);
            float variance = (1.0f - clampedLength) / clampedLength;
            variance = Math.Max(0.0f, variance - 0.00004f);

            float roughness = Math.Clamp(originalRoughness, 0.0f, 1.0f);
            float a = roughness * roughness;
            float a2 = a * a;

            // Упрощённая формула Toksvig (адаптирована из Unreal Engine)
            float B = 2.0f * variance * (a2 - 1.0f);

            if (Math.Abs(B - 1.0f) < Epsilon) {
                return roughness;
            }

            float a2Corrected = (B - a2) / (B - 1.0f);
            a2Corrected = Math.Clamp(a2Corrected, Epsilon * Epsilon, 1.0f);

            return MathF.Pow(a2Corrected, 0.25f);
        }

        /// <summary>
        /// Конвертирует gloss в roughness
        /// </summary>
        private float GlossToRoughness(float gloss) {
            return 1.0f - gloss;
        }

        /// <summary>
        /// Конвертирует roughness в gloss
        /// </summary>
        private float RoughnessToGloss(float roughness) {
            return 1.0f - roughness;
        }

        private float ComputeAverageNormalLength(int mipX, int mipY, float scaleX, float scaleY) {
            if (_normalMap == null) {
                return 1.0f;
            }

            int srcX0 = (int)MathF.Floor(mipX * scaleX);
            int srcY0 = (int)MathF.Floor(mipY * scaleY);
            int srcX1 = (int)MathF.Ceiling((mipX + 1) * scaleX);
            int srcY1 = (int)MathF.Ceiling((mipY + 1) * scaleY);

            srcX0 = Math.Clamp(srcX0, 0, _normalMap.Width - 1);
            srcY0 = Math.Clamp(srcY0, 0, _normalMap.Height - 1);
            srcX1 = Math.Clamp(Math.Max(srcX1, srcX0 + 1), 1, _normalMap.Width);
            srcY1 = Math.Clamp(Math.Max(srcY1, srcY0 + 1), 1, _normalMap.Height);

            float sumX = 0.0f;
            float sumY = 0.0f;
            float sumZ = 0.0f;
            int sampleCount = 0;

            for (int sy = srcY0; sy < srcY1; sy++) {
                var normalRow = _normalMap.GetPixelRowSpan(sy);
                for (int sx = srcX0; sx < srcX1; sx++) {
                    var normalPixel = normalRow[sx];
                    float nx = normalPixel.R / 255.0f * 2.0f - 1.0f;
                    float ny = normalPixel.G / 255.0f * 2.0f - 1.0f;
                    float nz = normalPixel.B / 255.0f * 2.0f - 1.0f;

                    sumX += nx;
                    sumY += ny;
                    sumZ += nz;
                    sampleCount++;
                }
            }

            if (sampleCount == 0) {
                return 1.0f;
            }

            float invCount = 1.0f / sampleCount;
            float avgX = sumX * invCount;
            float avgY = sumY * invCount;
            float avgZ = sumZ * invCount;

            float length = MathF.Sqrt(avgX * avgX + avgY * avgY + avgZ * avgZ);
            return Math.Clamp(length, 0.0f, 1.0f);
        }
    }
}

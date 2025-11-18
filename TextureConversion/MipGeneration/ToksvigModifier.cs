using System.Numerics;
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
    /// Дисперсия оценивается как variance = (1 - |N̄|) / |N̄|, где |N̄|
    /// — длина усреднённой нормали в окрестности пикселя. Далее применяется
    /// Toksvig-формула из Unreal Engine для пересчёта roughness (коррекция
    /// параметра alpha в GGX BRDF).
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
            if (_normalMap == null || mipLevel == 0) {
                // На уровне 0 или без normal map просто возвращаем оригинал
                return mipImage.Clone();
            }

            float scaleX = (float)_normalMap.Width / mipImage.Width;
            float scaleY = (float)_normalMap.Height / mipImage.Height;

            var corrected = new Image<Rgba32>(mipImage.Width, mipImage.Height);

            for (int y = 0; y < mipImage.Height; y++) {
                for (int x = 0; x < mipImage.Width; x++) {
                    var pixel = mipImage[x, y];
                    float inputValue = pixel.R / 255.0f;

                    float averageNormalLength = CalculateAverageNormalLength(x, y, scaleX, scaleY);
                    float roughness = _isGloss ? GlossToRoughness(inputValue) : inputValue;
                    float correctedRoughness = CalculateToksvigRoughness(roughness, averageNormalLength);
                    float outputValue = _isGloss ? RoughnessToGloss(correctedRoughness) : correctedRoughness;

                    byte outputByte = (byte)Math.Clamp(outputValue * 255.0f, 0, 255);
                    corrected[x, y] = new Rgba32(outputByte, outputByte, outputByte, pixel.A);
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
            const float epsilon = 1e-5f;

            // Чем короче средняя нормаль, тем больше дисперсия
            float safeLength = Math.Clamp(normalLengthAverage, epsilon, 0.9999f);
            float variance = (1.0f - safeLength) / safeLength;

            if (variance <= epsilon) {
                return originalRoughness;
            }

            float a = originalRoughness * originalRoughness;
            float a2 = a * a;

            float B = 2.0f * variance * (a2 - 1.0f);
            if (Math.Abs(B - 1.0f) < epsilon) {
                return originalRoughness;
            }

            float a2Corrected = (B - a2) / (B - 1.0f);
            a2Corrected = Math.Clamp(a2Corrected, epsilon * epsilon, 1.0f);

            float correctedRoughness = MathF.Pow(a2Corrected, 0.25f);
            return Math.Clamp(correctedRoughness, 0.0f, 1.0f);
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

        private float CalculateAverageNormalLength(int mipX, int mipY, float scaleX, float scaleY) {
            if (_normalMap == null) {
                return 1.0f;
            }

            int startX = Math.Clamp((int)MathF.Floor(mipX * scaleX), 0, _normalMap.Width - 1);
            int endX = Math.Clamp((int)MathF.Ceiling((mipX + 1) * scaleX), startX + 1, _normalMap.Width);

            int startY = Math.Clamp((int)MathF.Floor(mipY * scaleY), 0, _normalMap.Height - 1);
            int endY = Math.Clamp((int)MathF.Ceiling((mipY + 1) * scaleY), startY + 1, _normalMap.Height);

            Vector3 accumulated = Vector3.Zero;
            int sampleCount = 0;

            for (int y = startY; y < endY; y++) {
                for (int x = startX; x < endX; x++) {
                    var normalPixel = _normalMap[x, y];
                    var normal = DecodeNormal(normalPixel);

                    if (normal.LengthSquared() > 1e-6f) {
                        normal = Vector3.Normalize(normal);
                        accumulated += normal;
                        sampleCount++;
                    }
                }
            }

            if (sampleCount == 0) {
                return 1.0f;
            }

            var average = accumulated / sampleCount;
            return Math.Clamp(average.Length(), 0.0f, 1.0f);
        }

        private static Vector3 DecodeNormal(Rgba32 pixel) {
            float nx = pixel.R / 255.0f * 2.0f - 1.0f;
            float ny = pixel.G / 255.0f * 2.0f - 1.0f;
            float nz = pixel.B / 255.0f * 2.0f - 1.0f;
            return new Vector3(nx, ny, nz);
        }
    }
}

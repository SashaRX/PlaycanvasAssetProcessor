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
    /// Формула: roughness' = sqrt(roughness^2 + (1/k - 1))
    /// где k = 1 / (1 - avg_normal_length^2)
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
                return mipImage.Clone(ctx => { });
            }

            // TODO: Реализовать алгоритм Toksvig
            //
            // Шаги реализации:
            // 1. Для каждого пикселя в мипе:
            //    - Найти соответствующую область в оригинальной карте нормалей
            //    - Вычислить средний вектор нормали в этой области
            //    - Вычислить длину среднего вектора (показатель дисперсии)
            //    - Применить формулу Toksvig для коррекции roughness/gloss
            // 2. Обновить значения пикселей

            // Пока возвращаем клон без изменений
            return mipImage.Clone(ctx => { });
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
            if (normalLengthAverage >= 0.9999f) {
                return originalRoughness;
            }

            // k = 1 / (1 - |avgNormal|^2)
            float k = 1.0f / (1.0f - normalLengthAverage * normalLengthAverage);

            // roughness' = sqrt(roughness^2 + (1/k - 1))
            float modifiedRoughness = MathF.Sqrt(
                originalRoughness * originalRoughness + (1.0f / k - 1.0f)
            );

            return Math.Clamp(modifiedRoughness, 0.0f, 1.0f);
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
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using AssetProcessor.TextureConversion.Core;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Процессор для применения Toksvig mipmap generation
    /// Уменьшает specular aliasing путём коррекции gloss/roughness на основе дисперсии normal map
    /// </summary>
    public class ToksvigProcessor {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly MipGenerator _mipGenerator;
        private const float Epsilon = 1e-4f;

        public ToksvigProcessor() {
            _mipGenerator = new MipGenerator();
        }

        /// <summary>
        /// Применяет Toksvig коррекцию к gloss/roughness текстуре
        /// </summary>
        /// <param name="glossRoughnessMipmaps">Мипмапы gloss или roughness текстуры</param>
        /// <param name="normalMapImage">Normal map изображение</param>
        /// <param name="settings">Настройки Toksvig</param>
        /// <param name="isGloss">true если входные данные - gloss, false если roughness</param>
        /// <returns>Скорректированные мипмапы</returns>
        public List<Image<Rgba32>> ApplyToksvigCorrection(
            List<Image<Rgba32>> glossRoughnessMipmaps,
            Image<Rgba32> normalMapImage,
            ToksvigSettings settings,
            bool isGloss) {

            if (!settings.Enabled) {
                Logger.Info("Toksvig не включён, возвращаем оригинальные мипмапы");
                return glossRoughnessMipmaps;
            }

            if (!settings.Validate(out var error)) {
                Logger.Warn($"Некорректные настройки Toksvig: {error}. Пропускаем коррекцию.");
                return glossRoughnessMipmaps;
            }

            // Проверяем совпадение размеров
            if (glossRoughnessMipmaps[0].Width != normalMapImage.Width ||
                glossRoughnessMipmaps[0].Height != normalMapImage.Height) {
                Logger.Warn($"Размеры gloss/roughness ({glossRoughnessMipmaps[0].Width}x{glossRoughnessMipmaps[0].Height}) " +
                           $"и normal map ({normalMapImage.Width}x{normalMapImage.Height}) не совпадают. " +
                           $"Пропускаем Toksvig коррекцию.");
                return glossRoughnessMipmaps;
            }

            Logger.Info($"Применяем Toksvig коррекцию: k={settings.CompositePower}, " +
                       $"minLevel={settings.MinToksvigMipLevel}, smoothVariance={settings.SmoothVariance}");

            // Генерируем мипмапы для normal map
            var normalProfile = MipGenerationProfile.CreateDefault(TextureType.Normal);
            var normalMipmaps = _mipGenerator.GenerateMipmaps(normalMapImage, normalProfile);

            Logger.Info($"Сгенерировано {normalMipmaps.Count} уровней мипмапов для normal map");

            // Создаём корректированные мипмапы
            var correctedMipmaps = new List<Image<Rgba32>>();

            for (int level = 0; level < glossRoughnessMipmaps.Count; level++) {
                if (level < settings.MinToksvigMipLevel || level >= normalMipmaps.Count) {
                    // Для уровней ниже минимального или если не хватает normal mipmaps - копируем без изменений
                    correctedMipmaps.Add(glossRoughnessMipmaps[level].Clone());
                    Logger.Info($"  Mip{level} ({glossRoughnessMipmaps[level].Width}x{glossRoughnessMipmaps[level].Height}): " +
                               $"SKIPPED (minLevel={settings.MinToksvigMipLevel})");
                } else {
                    // Применяем Toksvig коррекцию
                    var correctedMip = ApplyToksvigToLevel(
                        glossRoughnessMipmaps[level],
                        normalMipmaps[level],
                        settings,
                        isGloss,
                        level);

                    correctedMipmaps.Add(correctedMip);
                    Logger.Debug($"Уровень {level}: применена Toksvig коррекция");
                }
            }

            // Освобождаем память normal mipmaps
            foreach (var mip in normalMipmaps) {
                mip.Dispose();
            }

            return correctedMipmaps;
        }

        /// <summary>
        /// Применяет Toksvig коррекцию к одному уровню мипмапа
        /// </summary>
        private Image<Rgba32> ApplyToksvigToLevel(
            Image<Rgba32> glossRoughnessMip,
            Image<Rgba32> normalMip,
            ToksvigSettings settings,
            bool isGloss,
            int level) {

            // Вычисляем дисперсию normal map
            var varianceMap = CalculateNormalVariance(normalMip);

            // Применяем сглаживание дисперсии если включено
            if (settings.SmoothVariance) {
                varianceMap = SmoothVariance(varianceMap);
            }

            // Статистика изменений
            int pixelsChanged = 0;
            float totalDifference = 0f;
            float maxDifference = 0f;

            // Создаём корректированный мипмап
            var correctedMip = glossRoughnessMip.Clone();

            correctedMip.Mutate(ctx => {
                ctx.ProcessPixelRowsAsVector4((row, point) => {
                    for (int x = 0; x < row.Length; x++) {
                        var pixel = row[x];
                        // Получаем значение дисперсии из R канала varianceMap
                        float variance = varianceMap[x, point.Y].ToVector4().X;

                        // Берём только R канал (предполагаем что gloss/roughness в R)
                        float inputValue = pixel.X;

                        // Конвертируем в roughness если на входе gloss
                        float roughness = isGloss ? (1.0f - inputValue) : inputValue;

                        // Применяем Toksvig коррекцию
                        float correctedRoughness = ApplyToksvigFormula(roughness, variance, settings.CompositePower);

                        // Конвертируем обратно в gloss если нужно
                        float outputValue = isGloss ? (1.0f - correctedRoughness) : correctedRoughness;

                        // Статистика изменений
                        float diff = Math.Abs(outputValue - inputValue);
                        if (diff > 0.001f) {
                            pixelsChanged++;
                            totalDifference += diff;
                            maxDifference = Math.Max(maxDifference, diff);
                        }

                        // Записываем во все каналы RGB (обычно gloss/roughness одноканальные, но храним в RGB)
                        pixel.X = outputValue;
                        pixel.Y = outputValue;
                        pixel.Z = outputValue;
                        // Alpha не трогаем

                        row[x] = pixel;
                    }
                });
            });

            // Логируем статистику изменений
            int totalPixels = glossRoughnessMip.Width * glossRoughnessMip.Height;
            float avgDifference = pixelsChanged > 0 ? totalDifference / pixelsChanged : 0f;
            Logger.Info($"  Mip{level} ({glossRoughnessMip.Width}x{glossRoughnessMip.Height}): " +
                       $"{pixelsChanged}/{totalPixels} pixels changed " +
                       $"(avg diff: {avgDifference:F4}, max diff: {maxDifference:F4})");

            return correctedMip;
        }

        /// <summary>
        /// Вычисляет дисперсию нормалей для каждого пикселя
        /// Использует локальное окно 3x3 для вычисления дисперсии
        /// </summary>
        private Image<Rgba32> CalculateNormalVariance(Image<Rgba32> normalMip) {
            var varianceMap = new Image<Rgba32>(normalMip.Width, normalMip.Height);

            for (int y = 0; y < normalMip.Height; y++) {
                for (int x = 0; x < normalMip.Width; x++) {
                    // Вычисляем дисперсию в окне 3x3
                    float variance = CalculateLocalVariance(normalMip, x, y);

                    // Сохраняем дисперсию в R канал (используем grayscale)
                    varianceMap[x, y] = new Rgba32(variance, variance, variance, 1.0f);
                }
            }

            return varianceMap;
        }

        /// <summary>
        /// Вычисляет локальную дисперсию нормалей в окне 3x3
        /// </summary>
        private float CalculateLocalVariance(Image<Rgba32> normalMip, int centerX, int centerY) {
            // Собираем нормали в окне 3x3
            var normals = new List<Vector3>();

            for (int dy = -1; dy <= 1; dy++) {
                for (int dx = -1; dx <= 1; dx++) {
                    int x = Math.Clamp(centerX + dx, 0, normalMip.Width - 1);
                    int y = Math.Clamp(centerY + dy, 0, normalMip.Height - 1);

                    var pixel = normalMip[x, y].ToVector4();

                    // Конвертируем из [0,1] в [-1,1]
                    var normal = new Vector3(
                        pixel.X * 2.0f - 1.0f,
                        pixel.Y * 2.0f - 1.0f,
                        pixel.Z * 2.0f - 1.0f
                    );

                    // Нормализуем
                    float length = normal.Length();
                    if (length > Epsilon) {
                        normal /= length;
                    }

                    normals.Add(normal);
                }
            }

            // Вычисляем среднюю нормаль
            var avgNormal = Vector3.Zero;
            foreach (var n in normals) {
                avgNormal += n;
            }
            avgNormal /= normals.Count;

            // Нормализуем среднюю нормаль
            float avgLength = avgNormal.Length();
            if (avgLength > Epsilon) {
                avgNormal /= avgLength;
            }

            // Дисперсия = 1 - длина средней нормали
            // Чем больше разброс нормалей, тем короче средний вектор
            float variance = Math.Max(0.0f, 1.0f - avgLength);

            return variance;
        }

        /// <summary>
        /// Сглаживает карту дисперсии с помощью 3x3 блюра
        /// </summary>
        private Image<Rgba32> SmoothVariance(Image<Rgba32> varianceMap) {
            // Для маленьких изображений (меньше 4x4) пропускаем blur
            if (varianceMap.Width < 4 || varianceMap.Height < 4) {
                Logger.Debug($"Изображение слишком маленькое ({varianceMap.Width}x{varianceMap.Height}) для blur, пропускаем сглаживание");
                return varianceMap.Clone();
            }

            var smoothed = varianceMap.Clone();

            // Применяем лёгкий Gaussian blur 3x3
            smoothed.Mutate(ctx => ctx.GaussianBlur(0.5f));

            return smoothed;
        }

        /// <summary>
        /// Применяет формулу Toksvig для коррекции roughness
        /// </summary>
        /// <param name="roughness">Входное значение roughness [0,1]</param>
        /// <param name="variance">Дисперсия нормалей [0,1]</param>
        /// <param name="k">Composite Power (вес влияния)</param>
        /// <returns>Скорректированное значение roughness</returns>
        private float ApplyToksvigFormula(float roughness, float variance, float k) {
            // Конвертируем roughness в GGX alpha
            float alpha = roughness * roughness;

            // Применяем Toksvig: alpha' = clamp(alpha + k * variance, epsilon, 1)
            float correctedAlpha = Math.Clamp(alpha + k * variance, Epsilon, 1.0f);

            // Конвертируем обратно в roughness
            float correctedRoughness = MathF.Sqrt(correctedAlpha);

            return correctedRoughness;
        }

        /// <summary>
        /// Создаёт карту дисперсии для визуализации (для отладки)
        /// </summary>
        public Image<Rgba32> CreateVarianceVisualization(Image<Rgba32> normalMapImage, ToksvigSettings settings) {
            // Генерируем мипмапы для normal map
            var normalProfile = MipGenerationProfile.CreateDefault(TextureType.Normal);
            var normalMipmaps = _mipGenerator.GenerateMipmaps(normalMapImage, normalProfile);

            if (normalMipmaps.Count <= settings.MinToksvigMipLevel) {
                Logger.Warn("Недостаточно уровней мипмапов для визуализации");
                return new Image<Rgba32>(1, 1);
            }

            // Берём указанный уровень
            var normalMip = normalMipmaps[settings.MinToksvigMipLevel];

            // Вычисляем дисперсию
            var varianceMap = CalculateNormalVariance(normalMip);

            if (settings.SmoothVariance) {
                var smoothedVariance = SmoothVariance(varianceMap);
                varianceMap.Dispose();
                varianceMap = smoothedVariance;
            }

            // Освобождаем memory
            foreach (var mip in normalMipmaps) {
                mip.Dispose();
            }

            return varianceMap;
        }
    }
}

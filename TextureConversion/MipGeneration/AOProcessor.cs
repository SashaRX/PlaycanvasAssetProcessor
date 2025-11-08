using System;
using System.Collections.Generic;
using System.Linq;
using AssetProcessor.TextureConversion.Core;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Процессор для специальной обработки AO (Ambient Occlusion) мипмапов
    /// Применяет lerp(mean, min, bias) или percentile-based подход для сохранения деталей затенения
    /// </summary>
    public class AOProcessor {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Применяет специальную обработку к AO мипмапам
        /// </summary>
        /// <param name="mipmaps">Список мипмапов для обработки</param>
        /// <param name="mode">Режим обработки</param>
        /// <param name="bias">Bias для BiasedDarkening (0.0-1.0)</param>
        /// <param name="percentile">Percentile для Percentile режима (0.0-100.0)</param>
        /// <param name="startLevel">С какого уровня начинать обработку (обычно 1, т.к. mip0 не трогаем)</param>
        /// <returns>Обработанные мипмапы</returns>
        public List<Image<Rgba32>> ProcessAOMipmaps(
            List<Image<Rgba32>> mipmaps,
            AOProcessingMode mode,
            float bias = 0.5f,
            float percentile = 10.0f,
            int startLevel = 1) {

            if (mode == AOProcessingMode.None) {
                Logger.Info("AO processing mode is None, returning original mipmaps");
                return mipmaps;
            }

            Logger.Info($"=== AO MIPMAP PROCESSING ===");
            Logger.Info($"  Mode: {mode}");
            Logger.Info($"  Bias: {bias:F2} (for BiasedDarkening)");
            Logger.Info($"  Percentile: {percentile:F1}% (for Percentile)");
            Logger.Info($"  Start level: {startLevel}");
            Logger.Info($"  Total mipmaps: {mipmaps.Count}");

            var processedMipmaps = new List<Image<Rgba32>>();

            for (int level = 0; level < mipmaps.Count; level++) {
                if (level < startLevel) {
                    // Не обрабатываем начальные уровни - создаем независимую копию
                    var copy = CloneImage(mipmaps[level]);
                    processedMipmaps.Add(copy);
                    Logger.Info($"  Mip{level} ({mipmaps[level].Width}x{mipmaps[level].Height}): SKIPPED (< startLevel)");
                    continue;
                }

                Image<Rgba32> processedMip;

                if (mode == AOProcessingMode.BiasedDarkening) {
                    processedMip = ApplyBiasedDarkening(mipmaps[level], bias, level);
                } else {
                    processedMip = ApplyPercentileDarkening(mipmaps[level], percentile, level);
                }

                processedMipmaps.Add(processedMip);
            }

            Logger.Info($"✓ AO processing complete: {processedMipmaps.Count} mipmaps processed");
            return processedMipmaps;
        }

        /// <summary>
        /// Применяет biased darkening: lerp(mean, min, bias)
        /// </summary>
        private Image<Rgba32> ApplyBiasedDarkening(Image<Rgba32> mip, float bias, int level) {
            var stats = CalculateStatistics(mip);

            // Вычисляем целевое значение: lerp(mean, min, bias)
            // bias = 0.0 -> mean (светлее)
            // bias = 1.0 -> min (темнее)
            float targetValue = Lerp(stats.Mean, stats.Min, bias);

            Logger.Info($"  Mip{level} ({mip.Width}x{mip.Height}): " +
                       $"min={stats.Min:F3}, mean={stats.Mean:F3}, max={stats.Max:F3} -> " +
                       $"target={targetValue:F3} (bias={bias:F2})");

            // Создаем новое изображение
            var processed = new Image<Rgba32>(mip.Width, mip.Height);

            // Применяем трансформацию к каждому пикселю
            for (int y = 0; y < mip.Height; y++) {
                for (int x = 0; x < mip.Width; x++) {
                    var pixel = mip[x, y];
                    float value = pixel.R / 255.0f;

                    // Lerp между текущим значением и целевым (сохраняем вариацию)
                    float newValue = Lerp(value, targetValue, bias * 0.5f);
                    newValue = Math.Clamp(newValue, 0.0f, 1.0f);

                    byte byteValue = (byte)(newValue * 255);
                    processed[x, y] = new Rgba32(byteValue, byteValue, byteValue, pixel.A);
                }
            }

            return processed;
        }

        /// <summary>
        /// Применяет percentile-based darkening
        /// </summary>
        private Image<Rgba32> ApplyPercentileDarkening(Image<Rgba32> mip, float percentile, int level) {
            var stats = CalculateStatistics(mip);

            // Вычисляем значение на заданном перцентиле
            var histogram = BuildHistogram(mip);
            float percentileValue = CalculatePercentile(histogram, percentile);

            Logger.Info($"  Mip{level} ({mip.Width}x{mip.Height}): " +
                       $"min={stats.Min:F3}, mean={stats.Mean:F3}, max={stats.Max:F3}, " +
                       $"p{percentile:F0}={percentileValue:F3}");

            // Создаем новое изображение
            var processed = new Image<Rgba32>(mip.Width, mip.Height);

            // Применяем soft bias к значениям ниже перцентиля
            for (int y = 0; y < mip.Height; y++) {
                for (int x = 0; x < mip.Width; x++) {
                    var pixel = mip[x, y];
                    float value = pixel.R / 255.0f;

                    // Если значение ниже перцентиля - немного затемняем
                    float newValue = value;
                    if (value < percentileValue) {
                        // Soft blend к перцентилю (сохраняем 70% оригинального значения)
                        newValue = Lerp(value, percentileValue, 0.3f);
                    }

                    newValue = Math.Clamp(newValue, 0.0f, 1.0f);
                    byte byteValue = (byte)(newValue * 255);
                    processed[x, y] = new Rgba32(byteValue, byteValue, byteValue, pixel.A);
                }
            }

            return processed;
        }

        /// <summary>
        /// Вычисляет статистику для изображения
        /// </summary>
        private (float Min, float Max, float Mean) CalculateStatistics(Image<Rgba32> image) {
            float min = 1.0f;
            float max = 0.0f;
            double sum = 0.0;
            int count = 0;

            for (int y = 0; y < image.Height; y++) {
                for (int x = 0; x < image.Width; x++) {
                    float value = image[x, y].R / 255.0f;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                    sum += value;
                    count++;
                }
            }

            return (min, max, (float)(sum / count));
        }

        /// <summary>
        /// Строит гистограмму значений
        /// </summary>
        private int[] BuildHistogram(Image<Rgba32> image) {
            var histogram = new int[256];

            for (int y = 0; y < image.Height; y++) {
                for (int x = 0; x < image.Width; x++) {
                    byte value = image[x, y].R;
                    histogram[value]++;
                }
            }

            return histogram;
        }

        /// <summary>
        /// Вычисляет значение на заданном перцентиле
        /// </summary>
        private float CalculatePercentile(int[] histogram, float percentile) {
            int totalPixels = histogram.Sum();
            int targetCount = (int)(totalPixels * percentile / 100.0f);

            int accumulatedCount = 0;
            for (int i = 0; i < histogram.Length; i++) {
                accumulatedCount += histogram[i];
                if (accumulatedCount >= targetCount) {
                    return i / 255.0f;
                }
            }

            return 1.0f;
        }

        /// <summary>
        /// Линейная интерполяция
        /// </summary>
        private float Lerp(float a, float b, float t) {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Создает независимую копию изображения
        /// </summary>
        private Image<Rgba32> CloneImage(Image<Rgba32> source) {
            var copy = new Image<Rgba32>(source.Width, source.Height);

            source.ProcessPixelRows(copy, (sourceAccessor, targetAccessor) => {
                for (int y = 0; y < sourceAccessor.Height; y++) {
                    var sourceRow = sourceAccessor.GetRowSpan(y);
                    var targetRow = targetAccessor.GetRowSpan(y);
                    sourceRow.CopyTo(targetRow);
                }
            });

            return copy;
        }
    }
}

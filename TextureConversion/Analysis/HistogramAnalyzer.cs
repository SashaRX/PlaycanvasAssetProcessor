using AssetProcessor.TextureConversion.Core;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.Analysis {
    /// <summary>
    /// Анализатор гистограммы с устойчивым (robust) анализом диапазона
    /// Вычисляет scale/offset для оптимизации сжатия текстур
    /// </summary>
    public class HistogramAnalyzer {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Анализирует изображение и вычисляет параметры нормализации
        /// </summary>
        /// <param name="image">Исходное изображение</param>
        /// <param name="settings">Настройки анализа</param>
        /// <returns>Результат анализа с scale/offset</returns>
        public HistogramResult Analyze(Image<Rgba32> image, HistogramSettings settings) {
            if (settings.Mode == HistogramMode.Off) {
                return HistogramResult.CreateIdentity();
            }

            try {
                Logger.Info($"=== HISTOGRAM ANALYSIS START ===");
                Logger.Info($"  Mode: {settings.Mode}");
                Logger.Info($"  Channel Mode: {settings.ChannelMode}");
                Logger.Info($"  Percentiles: {settings.PercentileLow}% - {settings.PercentileHigh}%");
                Logger.Info($"  Image size: {image.Width}x{image.Height}");

                var result = new HistogramResult {
                    Mode = settings.Mode,
                    ChannelMode = settings.ChannelMode,
                    TotalPixels = image.Width * image.Height
                };

                // Выбираем режим анализа в зависимости от настроек
                switch (settings.ChannelMode) {
                    case HistogramChannelMode.AverageLuminance:
                        AnalyzeLuminance(image, settings, result);
                        break;

                    case HistogramChannelMode.PerChannel:
                    case HistogramChannelMode.RGBOnly:
                        AnalyzePerChannel(image, settings, result, includeAlpha: false);
                        break;

                    case HistogramChannelMode.PerChannelRGBA:
                        AnalyzePerChannel(image, settings, result, includeAlpha: true);
                        break;

                    default:
                        result.Success = false;
                        result.Error = $"Unknown channel mode: {settings.ChannelMode}";
                        return result;
                }

                result.Success = true;

                Logger.Info($"=== HISTOGRAM ANALYSIS COMPLETE ===");
                Logger.Info($"  Scale: [{string.Join(", ", result.Scale.Select(s => s.ToString("F4")))}]");
                Logger.Info($"  Offset: [{string.Join(", ", result.Offset.Select(o => o.ToString("F4")))}]");
                Logger.Info($"  Range: [{result.RangeLow:F4} - {result.RangeHigh:F4}]");
                Logger.Info($"  Tail fraction: {result.TailFraction:P2}");
                Logger.Info($"  Knee applied: {result.KneeApplied}");

                if (result.Warnings.Count > 0) {
                    Logger.Warn($"  Warnings:");
                    foreach (var warning in result.Warnings) {
                        Logger.Warn($"    - {warning}");
                    }
                }

                return result;

            } catch (Exception ex) {
                Logger.Error(ex, "Histogram analysis failed");
                return new HistogramResult {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Анализирует усреднённую яркость (luminance)
        /// </summary>
        private void AnalyzeLuminance(Image<Rgba32> image, HistogramSettings settings, HistogramResult result) {
            // Строим гистограмму яркости (256 бинов)
            var histogram = new long[256];

            // Thread-local histograms для параллельной обработки
            var lockObj = new object();

            Parallel.For(0, image.Height, () => new long[256], (y, loopState, localHist) => {
                var pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    var pixel = pixelRow[x];
                    // Усреднённая яркость
                    int luminance = (pixel.R + pixel.G + pixel.B) / 3;
                    localHist[luminance]++;
                }
                return localHist;
            }, localHist => {
                lock (lockObj) {
                    for (int i = 0; i < 256; i++) {
                        histogram[i] += localHist[i];
                    }
                }
            });

            // Вычисляем перцентили и применяем режим
            float lo, hi;
            CalculatePercentilesAndRange(histogram, settings, out lo, out hi, out float tailFraction);

            result.RangeLow = lo;
            result.RangeHigh = hi;
            result.TailFraction = tailFraction;

            // Проверяем минимальный диапазон
            float range = hi - lo;
            if (range < settings.MinRangeThreshold) {
                Logger.Warn($"Range too small ({range:F6}), skipping normalization");
                result.Scale = new[] { 1.0f };
                result.Offset = new[] { 0.0f };
                result.Warnings.Add($"Range too small ({range:F6}), normalization skipped");
                return;
            }

            // Вычисляем scale и offset
            float scale = 1.0f / range;
            float offset = -lo * scale;

            result.Scale = new[] { scale };
            result.Offset = new[] { offset };

            // Проверяем долю хвостов
            if (tailFraction > settings.TailThreshold) {
                result.Warnings.Add($"High tail fraction ({tailFraction:P2}), potential outliers or noise detected");
            }
        }

        /// <summary>
        /// Анализирует каждый канал отдельно
        /// </summary>
        private void AnalyzePerChannel(Image<Rgba32> image, HistogramSettings settings, HistogramResult result, bool includeAlpha) {
            int channelCount = includeAlpha ? 4 : 3;

            // Строим гистограммы для каждого канала
            var histograms = new long[channelCount][];
            for (int c = 0; c < channelCount; c++) {
                histograms[c] = new long[256];
            }

            // Thread-local histograms
            var lockObj = new object();

            Parallel.For(0, image.Height, () => {
                var localHists = new long[channelCount][];
                for (int c = 0; c < channelCount; c++) {
                    localHists[c] = new long[256];
                }
                return localHists;
            }, (y, loopState, localHists) => {
                var pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    var pixel = pixelRow[x];
                    localHists[0][pixel.R]++;
                    localHists[1][pixel.G]++;
                    localHists[2][pixel.B]++;
                    if (includeAlpha) {
                        localHists[3][pixel.A]++;
                    }
                }
                return localHists;
            }, localHists => {
                lock (lockObj) {
                    for (int c = 0; c < channelCount; c++) {
                        for (int i = 0; i < 256; i++) {
                            histograms[c][i] += localHists[c][i];
                        }
                    }
                }
            });

            // Анализируем каждый канал
            var scales = new float[channelCount];
            var offsets = new float[channelCount];
            float totalTailFraction = 0;

            for (int c = 0; c < channelCount; c++) {
                float lo, hi;
                CalculatePercentilesAndRange(histograms[c], settings, out lo, out hi, out float tailFraction);

                totalTailFraction += tailFraction;

                // Вычисляем scale и offset для канала
                float range = hi - lo;
                if (range < settings.MinRangeThreshold) {
                    scales[c] = 1.0f;
                    offsets[c] = 0.0f;
                    Logger.Warn($"Channel {c}: Range too small ({range:F6}), skipping normalization");
                } else {
                    scales[c] = 1.0f / range;
                    offsets[c] = -lo * scales[c];
                }

                Logger.Info($"  Channel {c}: lo={lo:F4}, hi={hi:F4}, scale={scales[c]:F4}, offset={offsets[c]:F4}");
            }

            result.Scale = scales;
            result.Offset = offsets;
            result.TailFraction = totalTailFraction / channelCount;

            // Для упрощения используем средний диапазон
            result.RangeLow = offsets.Average();
            result.RangeHigh = (scales.Sum() / channelCount);

            if (result.TailFraction > settings.TailThreshold) {
                result.Warnings.Add($"High tail fraction ({result.TailFraction:P2}), potential outliers or noise detected");
            }
        }

        /// <summary>
        /// Вычисляет перцентили и диапазон с учётом режима (с коленом или без)
        /// </summary>
        private void CalculatePercentilesAndRange(
            long[] histogram,
            HistogramSettings settings,
            out float lo,
            out float hi,
            out float tailFraction) {

            long totalPixels = histogram.Sum();

            if (totalPixels == 0) {
                lo = 0;
                hi = 1;
                tailFraction = 0;
                return;
            }

            // Вычисляем перцентили
            long lowThreshold = (long)(totalPixels * settings.PercentileLow / 100.0);
            long highThreshold = (long)(totalPixels * settings.PercentileHigh / 100.0);

            long accumulated = 0;
            int loIndex = 0;
            int hiIndex = 255;

            // Находим нижний перцентиль
            for (int i = 0; i < 256; i++) {
                accumulated += histogram[i];
                if (accumulated >= lowThreshold) {
                    loIndex = i;
                    break;
                }
            }

            // Находим верхний перцентиль
            accumulated = 0;
            for (int i = 0; i < 256; i++) {
                accumulated += histogram[i];
                if (accumulated >= highThreshold) {
                    hiIndex = i;
                    break;
                }
            }

            // Нормализуем к [0, 1]
            lo = loIndex / 255.0f;
            hi = hiIndex / 255.0f;

            // Вычисляем долю хвостов
            long tailPixels = 0;
            for (int i = 0; i < loIndex; i++) {
                tailPixels += histogram[i];
            }
            for (int i = hiIndex + 1; i < 256; i++) {
                tailPixels += histogram[i];
            }
            tailFraction = (float)tailPixels / totalPixels;

            // Применяем soft-knee если включен
            if (settings.Mode == HistogramMode.PercentileWithKnee) {
                // Вычисляем ширину колена
                float kneeWidth = settings.KneeWidth * (hi - lo);

                // Для soft-knee нам нужно расширить диапазон на ширину колена
                // чтобы учесть зону сглаживания
                lo = Math.Max(0, lo - kneeWidth);
                hi = Math.Min(1, hi + kneeWidth);

                Logger.Info($"  Soft-knee applied: knee width = {kneeWidth:F4}");
            }

            Logger.Info($"  Percentiles: lo={lo:F4} ({loIndex}/255), hi={hi:F4} ({hiIndex}/255)");
            Logger.Info($"  Tail fraction: {tailFraction:P2}");
        }

        /// <summary>
        /// Применяет soft-knee smoothstep функцию
        /// </summary>
        private float SmoothStep(float t) {
            // Классическая smoothstep функция: S(t) = 3t² - 2t³
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// Применяет винсоризацию (winsorization) к изображению
        /// ВАЖНО: Это модифицирующая операция, применяется опционально
        /// </summary>
        public Image<Rgba32> ApplyWinsorization(Image<Rgba32> image, float lo, float hi) {
            var result = image.Clone();

            float range = hi - lo;

            // Safety check: avoid division by zero or invalid range
            if (range < 0.0001f || float.IsNaN(range) || float.IsInfinity(range)) {
                return result; // Return unmodified if range is too small or invalid
            }

            Parallel.For(0, result.Height, y => {
                var pixelRow = result.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    var pixel = pixelRow[x];

                    // CRITICAL FIX: Apply LINEAR normalization WITHOUT pre-clamping
                    // Pre-clamping causes channels below lo to map to 0 (black), making texture red!
                    // Instead: normalize directly, let PNG format clamp the byte values
                    float r_norm = pixel.R / 255.0f;
                    float g_norm = pixel.G / 255.0f;
                    float b_norm = pixel.B / 255.0f;

                    // Apply linear normalization WITHOUT pre-clamping
                    // If v < lo: normalized < 0 → byte clamped to 0
                    // If v > hi: normalized > 1 → byte clamped to 255
                    // GPU recovers: v_original = v_normalized * range + lo
                    float r_normalized = (r_norm - lo) / range;
                    float g_normalized = (g_norm - lo) / range;
                    float b_normalized = (b_norm - lo) / range;

                    // Clamp to [0, 255] byte range (this is the ONLY clamping!)
                    byte r = (byte)Math.Clamp((int)(r_normalized * 255), 0, 255);
                    byte g = (byte)Math.Clamp((int)(g_normalized * 255), 0, 255);
                    byte b = (byte)Math.Clamp((int)(b_normalized * 255), 0, 255);

                    pixelRow[x] = new Rgba32(r, g, b, pixel.A);
                }
            });

            return result;
        }

        /// <summary>
        /// Применяет soft-knee к изображению (опционально)
        /// ВАЖНО: Это модифицирующая операция для предобработки
        /// </summary>
        public Image<Rgba32> ApplySoftKnee(Image<Rgba32> image, float lo, float hi, float kneeWidth) {
            var result = image.Clone();

            float range = hi - lo;

            // Safety check: avoid division by zero or invalid range
            if (range < 0.0001f || float.IsNaN(range) || float.IsInfinity(range)) {
                return result; // Return unmodified if range is too small or invalid
            }

            Parallel.For(0, result.Height, y => {
                var pixelRow = result.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    var pixel = pixelRow[x];

                    // CRITICAL FIX: Apply soft-knee to EACH CHANNEL with lo/hi from luminance histogram
                    // This allows GPU to recover using simple linear formula: v_original = v_norm * scale + offset
                    float r_norm = pixel.R / 255.0f;
                    float g_norm = pixel.G / 255.0f;
                    float b_norm = pixel.B / 255.0f;

                    // Apply soft-knee to each channel separately
                    float r_normalized = ApplySoftKneeToValue(r_norm, lo, hi, kneeWidth);
                    float g_normalized = ApplySoftKneeToValue(g_norm, lo, hi, kneeWidth);
                    float b_normalized = ApplySoftKneeToValue(b_norm, lo, hi, kneeWidth);

                    byte rByte = (byte)Math.Clamp((int)(r_normalized * 255), 0, 255);
                    byte gByte = (byte)Math.Clamp((int)(g_normalized * 255), 0, 255);
                    byte bByte = (byte)Math.Clamp((int)(b_normalized * 255), 0, 255);

                    pixelRow[x] = new Rgba32(rByte, gByte, bByte, pixel.A);
                }
            });

            return result;
        }

        /// <summary>
        /// Применяет soft-knee к одному значению
        /// </summary>
        private float ApplySoftKneeToValue(float v, float lo, float hi, float knee) {
            // КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: нормализуем основной диапазон [lo, hi] → [0, 1]
            // Это растягивает динамический диапазон, делая текстуру ярче

            // Если значение в основном диапазоне - нормализуем (растягиваем)
            if (v >= lo && v <= hi) {
                // Линейное масштабирование: [lo, hi] → [0, 1]
                return (v - lo) / (hi - lo);
            }

            // Если ниже lo - применяем сглаживание и затем нормализуем
            if (v < lo) {
                float t = (lo - v) / knee;
                if (t >= 1.0f) {
                    // За пределами колена - клампим к 0
                    return 0.0f;
                }
                // Сглаженный переход к 0
                float smoothedLow = lo - knee * SmoothStep(t);
                return (smoothedLow - lo) / (hi - lo);
            }

            // Если выше hi - применяем сглаживание и затем нормализуем
            float tHigh = (v - hi) / knee;
            if (tHigh >= 1.0f) {
                // За пределами колена - клампим к 1
                return 1.0f;
            }
            // Сглаженный переход к 1
            float smoothedHigh = hi + knee * SmoothStep(tHigh);
            return (smoothedHigh - lo) / (hi - lo);
        }
    }
}

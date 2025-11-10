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
        /// Применяет Toksvig коррекцию к gloss/roughness текстуре и возвращает карту дисперсии
        /// </summary>
        /// <param name="glossRoughnessMipmaps">Мипмапы gloss или roughness текстуры</param>
        /// <param name="normalMapImage">Normal map изображение</param>
        /// <param name="settings">Настройки Toksvig</param>
        /// <param name="isGloss">true если входные данные - gloss, false если roughness</param>
        /// <returns>Tuple: (скорректированные мипмапы, карты дисперсии для debug)</returns>
        public (List<Image<Rgba32>> correctedMipmaps, List<Image<Rgba32>>? varianceMipmaps) ApplyToksvigCorrectionWithVariance(
            List<Image<Rgba32>> glossRoughnessMipmaps,
            Image<Rgba32> normalMapImage,
            ToksvigSettings settings,
            bool isGloss) {

            var result = ApplyToksvigCorrectionInternal(glossRoughnessMipmaps, normalMapImage, settings, isGloss, captureVariance: true);
            return (result.correctedMipmaps, result.varianceMipmaps);
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

            var result = ApplyToksvigCorrectionInternal(glossRoughnessMipmaps, normalMapImage, settings, isGloss, captureVariance: false);
            return result.correctedMipmaps;
        }

        /// <summary>
        /// Внутренний метод применения Toksvig коррекции
        /// </summary>
        private (List<Image<Rgba32>> correctedMipmaps, List<Image<Rgba32>>? varianceMipmaps) ApplyToksvigCorrectionInternal(
            List<Image<Rgba32>> glossRoughnessMipmaps,
            Image<Rgba32> normalMapImage,
            ToksvigSettings settings,
            bool isGloss,
            bool captureVariance) {

            if (!settings.Enabled) {
                Logger.Info("Toksvig не включён, возвращаем оригинальные мипмапы");
                return (glossRoughnessMipmaps, null);
            }

            if (!settings.Validate(out var error)) {
                Logger.Warn($"Некорректные настройки Toksvig: {error}. Пропускаем коррекцию.");
                return (glossRoughnessMipmaps, null);
            }

            // Проверяем совпадение размеров
            if (glossRoughnessMipmaps[0].Width != normalMapImage.Width ||
                glossRoughnessMipmaps[0].Height != normalMapImage.Height) {
                Logger.Warn($"Размеры gloss/roughness ({glossRoughnessMipmaps[0].Width}x{glossRoughnessMipmaps[0].Height}) " +
                           $"и normal map ({normalMapImage.Width}x{normalMapImage.Height}) не совпадают. " +
                           $"Пропускаем Toksvig коррекцию.");
                return (glossRoughnessMipmaps, null);
            }

            string modeInfo = settings.CalculationMode == ToksvigCalculationMode.Simplified
                ? $"Simplified (linear k, box 2x2, threshold={settings.VarianceThreshold:F4})"
                : $"Classic (k^1.5={MathF.Pow(settings.CompositePower, 1.5f):F1}, 3x3, smooth={settings.SmoothVariance})";
            string energyInfo = settings.UseEnergyPreserving ? " + EnergyPreserving" : "";
            Logger.Info($"🔧 Toksvig: k={settings.CompositePower:F1}, mode={modeInfo}, minLevel={settings.MinToksvigMipLevel}{energyInfo}");

            // Генерируем мипмапы для normal map
            // КРИТИЧНО: НЕ нормализуем нормали после фильтрации!
            // Нормализация должна происходить ВНУТРИ расчёта дисперсии (Energy preserving ДО Toksvig)
            var normalProfile = MipGenerationProfile.CreateDefault(TextureType.Normal);
            normalProfile.NormalizeNormals = false; // Отключаем глобальную нормализацию
            var normalMipmaps = _mipGenerator.GenerateMipmaps(normalMapImage, normalProfile);

            Logger.Info($"Сгенерировано {normalMipmaps.Count} уровней мипмапов для normal map");

            // Создаём корректированные мипмапы
            var correctedMipmaps = new List<Image<Rgba32>>();
            var varianceMipmaps = captureVariance ? new List<Image<Rgba32>>() : null;

            if (settings.UseEnergyPreserving) {
                // === Режим 1: Toksvig + Energy-Preserving ===
                // 1. Применяем Toksvig к базовому уровню
                // 2. Генерируем мипмапы с Energy-Preserving от Toksvig-corrected базового уровня

                // Применяем Toksvig только к базовому уровню
                var (toksvigCorrectedBase, varianceMapBase) = ApplyToksvigToLevel(
                    glossRoughnessMipmaps[0],
                    normalMipmaps[0],
                    settings,
                    isGloss,
                    0,
                    captureVariance);

                correctedMipmaps.Add(toksvigCorrectedBase);
                if (captureVariance && varianceMapBase != null) {
                    varianceMipmaps!.Add(varianceMapBase);
                }

                // Генерируем мипмапы с Energy-Preserving от Toksvig-corrected базового уровня
                var textureType = isGloss ? TextureType.Gloss : TextureType.Roughness;
                var mipProfile = MipGenerationProfile.CreateDefault(textureType);

                // Включаем energy-preserving фильтрацию
                mipProfile.UseEnergyPreserving = true;
                mipProfile.IsGloss = isGloss;

                // Генерируем мипмапы от Toksvig-corrected базового уровня
                var energyPreservingMips = _mipGenerator.GenerateMipmaps(toksvigCorrectedBase, mipProfile);

                // Добавляем сгенерированные мипы (пропускаем первый, т.к. он уже добавлен)
                for (int i = 1; i < energyPreservingMips.Count; i++) {
                    correctedMipmaps.Add(energyPreservingMips[i]);

                    // Для variance создаём пустые карты для остальных уровней
                    if (captureVariance) {
                        varianceMipmaps!.Add(new Image<Rgba32>(energyPreservingMips[i].Width, energyPreservingMips[i].Height));
                    }
                }
            } else {
                // === Режим 2: Только Toksvig (старый режим) ===
                // Применяем Toksvig к каждому уровню независимо

                for (int level = 0; level < glossRoughnessMipmaps.Count; level++) {
                    if (level < settings.MinToksvigMipLevel || level >= normalMipmaps.Count) {
                        // КРИТИЧНО: НЕ используем Clone() - создаём НОВЫЙ Image с независимым буфером
                        var original = glossRoughnessMipmaps[level];
                        var independentCopy = new Image<Rgba32>(
                            Configuration.Default,
                            original.Width,
                            original.Height);

                        // Копируем пиксели через ProcessPixelRows (ГАРАНТИРОВАННО независимый буфер)
                        original.ProcessPixelRows(independentCopy, (sourceAccessor, targetAccessor) => {
                            for (int y = 0; y < sourceAccessor.Height; y++) {
                                var sourceRow = sourceAccessor.GetRowSpan(y);
                                var targetRow = targetAccessor.GetRowSpan(y);
                                sourceRow.CopyTo(targetRow);
                            }
                        });
                        correctedMipmaps.Add(independentCopy);

                        // Для variance создаём пустую карту
                        if (captureVariance) {
                            varianceMipmaps!.Add(new Image<Rgba32>(glossRoughnessMipmaps[level].Width, glossRoughnessMipmaps[level].Height));
                        }

                        Logger.Info($"  Mip{level} ({glossRoughnessMipmaps[level].Width}x{glossRoughnessMipmaps[level].Height}): " +
                                   $"SKIPPED (minLevel={settings.MinToksvigMipLevel})");
                    } else {
                        // Применяем Toksvig коррекцию
                        var (correctedMip, varianceMap) = ApplyToksvigToLevel(
                            glossRoughnessMipmaps[level],
                            normalMipmaps[level],
                            settings,
                            isGloss,
                            level,
                            captureVariance);

                        correctedMipmaps.Add(correctedMip);
                        if (captureVariance && varianceMap != null) {
                            varianceMipmaps!.Add(varianceMap);
                        }
                    }
                }
            }

            // Освобождаем память normal mipmaps
            foreach (var mip in normalMipmaps) {
                mip.Dispose();
            }

            return (correctedMipmaps, varianceMipmaps);
        }

        /// <summary>
        /// Применяет Toksvig коррекцию к одному уровню мипмапа
        /// </summary>
        private (Image<Rgba32> correctedMip, Image<Rgba32>? varianceMap) ApplyToksvigToLevel(
            Image<Rgba32> glossRoughnessMip,
            Image<Rgba32> normalMip,
            ToksvigSettings settings,
            bool isGloss,
            int level,
            bool captureVariance) {

            // Критичная валидация размеров
            if (glossRoughnessMip.Width != normalMip.Width || glossRoughnessMip.Height != normalMip.Height) {
                Logger.Error($"[ToksvigProcessor] Dimension mismatch at level {level}: " +
                           $"Gloss={glossRoughnessMip.Width}x{glossRoughnessMip.Height}, " +
                           $"Normal={normalMip.Width}x{normalMip.Height}. Returning original gloss map.");

                // Возвращаем оригинал без коррекции
                var uncorrected = new Image<Rgba32>(Configuration.Default, glossRoughnessMip.Width, glossRoughnessMip.Height);
                glossRoughnessMip.ProcessPixelRows(uncorrected, (sourceAccessor, targetAccessor) => {
                    for (int y = 0; y < sourceAccessor.Height; y++) {
                        sourceAccessor.GetRowSpan(y).CopyTo(targetAccessor.GetRowSpan(y));
                    }
                });
                return (uncorrected, null);
            }

            // Вычисляем дисперсию normal map в зависимости от режима
            Image<Rgba32> varianceMap;
            if (settings.CalculationMode == ToksvigCalculationMode.Simplified) {
                // Simplified режим: нормализация + Box 2x2
                varianceMap = CalculateNormalVarianceSimplified(normalMip);
            } else {
                // Classic режим: 3x3 окно без нормализации
                varianceMap = CalculateNormalVariance(normalMip);
            }

            // Применяем сглаживание дисперсии если включено и это Classic режим
            if (settings.SmoothVariance && settings.CalculationMode == ToksvigCalculationMode.Classic) {
                varianceMap = SmoothVariance(varianceMap);
            }

            // Статистика изменений
            int pixelsChanged = 0;
            float totalDifference = 0f;
            float maxDifference = 0f;
            float minVariance = float.MaxValue;
            float maxVariance = float.MinValue;
            float avgVariance = 0f;
            float minInput = float.MaxValue;
            float maxInput = float.MinValue;
            float minOutput = float.MaxValue;
            float maxOutput = float.MinValue;

            // КРИТИЧНО: НЕ ИСПОЛЬЗУЕМ Clone() - он создаёт shallow copy с SHARED pixel buffer!
            // Создаём ПОЛНОСТЬЮ НОВЫЙ Image и копируем пиксели ВРУЧНУЮ
            var correctedMip = new Image<Rgba32>(
                Configuration.Default,
                glossRoughnessMip.Width,
                glossRoughnessMip.Height);

            // Копируем ВСЕ пиксели из оригинала в НОВЫЙ независимый буфер
            glossRoughnessMip.ProcessPixelRows(correctedMip, (sourceAccessor, targetAccessor) => {
                for (int y = 0; y < sourceAccessor.Height; y++) {
                    var sourceRow = sourceAccessor.GetRowSpan(y);
                    var targetRow = targetAccessor.GetRowSpan(y);
                    sourceRow.CopyTo(targetRow);
                }
            });

            // Для первых 3 пикселей логируем детальный расчёт (только для уровней 0-1)
            int debugPixelCount = 0;
            const int maxDebugPixels = 3;

            // Обрабатываем каждый пиксель напрямую
            for (int y = 0; y < glossRoughnessMip.Height; y++) {
                for (int x = 0; x < glossRoughnessMip.Width; x++) {
                    // Читаем оригинальный пиксель
                    var inputPixel = glossRoughnessMip[x, y];

                    // Получаем значение дисперсии из R канала varianceMap
                    float variance = varianceMap[x, y].R / 255.0f;

                    // Применяем порог дисперсии (dead zone) в Simplified режиме
                    if (settings.CalculationMode == ToksvigCalculationMode.Simplified) {
                        if (variance < settings.VarianceThreshold) {
                            variance = 0.0f;
                        }
                    }

                    // Статистика variance
                    avgVariance += variance;
                    minVariance = Math.Min(minVariance, variance);
                    maxVariance = Math.Max(maxVariance, variance);

                    // Берём только R канал (предполагаем что gloss/roughness в R)
                    float inputValue = inputPixel.R / 255.0f;
                    minInput = Math.Min(minInput, inputValue);
                    maxInput = Math.Max(maxInput, inputValue);

                    // Конвертируем в roughness если на входе gloss
                    float roughness = isGloss ? (1.0f - inputValue) : inputValue;

                    // Применяем Toksvig коррекцию
                    bool useLinearPower = settings.CalculationMode == ToksvigCalculationMode.Simplified;
                    float correctedRoughness = ApplyToksvigFormula(roughness, variance, settings.CompositePower, useLinearPower);

                    // Конвертируем обратно в gloss если нужно
                    float outputValue = isGloss ? (1.0f - correctedRoughness) : correctedRoughness;
                    minOutput = Math.Min(minOutput, outputValue);
                    maxOutput = Math.Max(maxOutput, outputValue);

                    // Детальное логирование для первых пикселей (только level 0-1)
                    if (level <= 1 && debugPixelCount < maxDebugPixels && Math.Abs(outputValue - inputValue) > 0.01f) {
                        debugPixelCount++;
                        Logger.Info($"    [{level}] Pixel({x},{y}): in={inputValue:F3}, var={variance:F4}, " +
                                   $"rough={roughness:F3}→{correctedRoughness:F3}, out={outputValue:F3}, diff={Math.Abs(outputValue - inputValue):F3}");
                    }

                    // Статистика изменений
                    float diff = Math.Abs(outputValue - inputValue);
                    if (diff > 0.001f) {
                        pixelsChanged++;
                        totalDifference += diff;
                        maxDifference = Math.Max(maxDifference, diff);
                    }

                    // Конвертируем обратно в байты и записываем НАПРЯМУЮ в клонированный image
                    byte outputByte = (byte)Math.Clamp(outputValue * 255.0f, 0, 255);
                    correctedMip[x, y] = new Rgba32(outputByte, outputByte, outputByte, inputPixel.A);
                }
            }

            // Логируем только важные уровни (0, 1, 2) и если есть изменения
            int totalPixels = glossRoughnessMip.Width * glossRoughnessMip.Height;
            avgVariance /= totalPixels;
            float avgDifference = pixelsChanged > 0 ? totalDifference / pixelsChanged : 0f;
            float changePercent = (float)pixelsChanged / totalPixels * 100f;

            if (level <= 2 || pixelsChanged > 0) {
                // Показываем adjustedVariance в зависимости от режима
                float adjustedVariance;
                string varianceLabel;
                if (settings.CalculationMode == ToksvigCalculationMode.Simplified) {
                    adjustedVariance = avgVariance * settings.CompositePower;
                    varianceLabel = "var*k";
                } else {
                    adjustedVariance = avgVariance * MathF.Pow(settings.CompositePower, 1.5f);
                    varianceLabel = "var*k^1.5";
                }
                Logger.Info($"  Mip{level} ({glossRoughnessMip.Width}x{glossRoughnessMip.Height}): " +
                           $"var={avgVariance:F4}, {varianceLabel}={adjustedVariance:F4}, k={settings.CompositePower:F1}, " +
                           $"changed={changePercent:F1}%, avgDiff={avgDifference:F3}, maxDiff={maxDifference:F3}");
            }

            // Возвращаем variance map если нужно, иначе освобождаем
            Image<Rgba32>? returnedVarianceMap = null;
            if (captureVariance) {
                returnedVarianceMap = varianceMap;
            } else {
                varianceMap.Dispose();
            }

            return (correctedMip, returnedVarianceMap);
        }

        /// <summary>
        /// Вычисляет дисперсию нормалей для каждого пикселя
        /// Использует локальное окно 3x3 для вычисления дисперсии (Classic режим)
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
        /// Вычисляет дисперсию нормалей для каждого пикселя (Simplified режим)
        /// Нормализует каждую нормаль, усредняет Box 2x2, затем берёт |N̄|
        /// </summary>
        private Image<Rgba32> CalculateNormalVarianceSimplified(Image<Rgba32> normalMip) {
            var varianceMap = new Image<Rgba32>(normalMip.Width, normalMip.Height);

            for (int y = 0; y < normalMip.Height; y++) {
                for (int x = 0; x < normalMip.Width; x++) {
                    // Вычисляем дисперсию в окне 2x2 с нормализацией
                    float variance = CalculateLocalVarianceBox2x2Normalized(normalMip, x, y);

                    // Сохраняем дисперсию в R канал (используем grayscale)
                    varianceMap[x, y] = new Rgba32(variance, variance, variance, 1.0f);
                }
            }

            return varianceMap;
        }

        /// <summary>
        /// Вычисляет локальную дисперсию нормалей в окне 2x2 с нормализацией
        /// 1. Нормализует каждую нормаль
        /// 2. Усредняет их (Box 2x2)
        /// 3. Берёт длину усредненной нормали
        /// 4. Вычисляет дисперсию по формуле (1 - |N̄|) / |N̄|
        /// </summary>
        private float CalculateLocalVarianceBox2x2Normalized(Image<Rgba32> normalMip, int centerX, int centerY) {
            // Валидация входных параметров
            if (normalMip == null || normalMip.Width == 0 || normalMip.Height == 0) {
                Logger.Error($"[ToksvigProcessor] Invalid normalMip: Width={normalMip?.Width}, Height={normalMip?.Height}");
                return 0.0f;
            }

            if (centerX < 0 || centerX >= normalMip.Width || centerY < 0 || centerY >= normalMip.Height) {
                Logger.Error($"[ToksvigProcessor] Invalid center coordinates: ({centerX}, {centerY}) for image {normalMip.Width}x{normalMip.Height}");
                return 0.0f;
            }

            // Собираем нормализованные нормали в окне 2x2
            var normals = new List<Vector3>();

            // Box 2x2: берём центральный пиксель и соседей справа, снизу и по диагонали
            for (int dy = 0; dy <= 1; dy++) {
                for (int dx = 0; dx <= 1; dx++) {
                    int x = Math.Clamp(centerX + dx, 0, normalMip.Width - 1);
                    int y = Math.Clamp(centerY + dy, 0, normalMip.Height - 1);

                    var pixel = normalMip[x, y].ToVector4();

                    // Конвертируем из [0,1] в [-1,1]
                    var normal = new Vector3(
                        pixel.X * 2.0f - 1.0f,
                        pixel.Y * 2.0f - 1.0f,
                        pixel.Z * 2.0f - 1.0f
                    );

                    // НОРМАЛИЗУЕМ каждую нормаль перед усреднением
                    float length = normal.Length();
                    if (length > Epsilon) {
                        normal = Vector3.Normalize(normal);
                    }

                    normals.Add(normal);
                }
            }

            // Усредняем нормализованные нормали (Box 2x2 - один проход)
            var avgNormal = Vector3.Zero;
            foreach (var n in normals) {
                avgNormal += n;
            }
            avgNormal /= normals.Count;

            // Берём длину усредненной нормали
            float lengthN = avgNormal.Length();

            // Защита от деления на ноль
            if (lengthN < Epsilon) {
                return 0.0f;
            }

            // Формула Toksvig: Variance = (1 - |N̄|) / |N̄|
            float variance = (1.0f - lengthN) / lengthN;

            // Вычитаем небольшое смещение для уменьшения шума
            variance = Math.Max(0.0f, variance - 0.00004f);

            return variance;
        }

        /// <summary>
        /// Вычисляет локальную дисперсию нормалей в окне 3x3 (по методу Unreal Engine Toksvig)
        /// </summary>
        private float CalculateLocalVariance(Image<Rgba32> normalMip, int centerX, int centerY) {
            // Валидация входных параметров
            if (normalMip == null || normalMip.Width == 0 || normalMip.Height == 0) {
                Logger.Error($"[ToksvigProcessor] Invalid normalMip: Width={normalMip?.Width}, Height={normalMip?.Height}");
                return 0.0f;
            }

            if (centerX < 0 || centerX >= normalMip.Width || centerY < 0 || centerY >= normalMip.Height) {
                Logger.Error($"[ToksvigProcessor] Invalid center coordinates: ({centerX}, {centerY}) for image {normalMip.Width}x{normalMip.Height}");
                return 0.0f;
            }

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

                    // КРИТИЧНО: НОРМАЛИЗУЕМ каждую нормаль ПЕРЕД усреднением (Energy preserving)
                    // Это предотвращает потерю информации о дисперсии при фильтрации мипмапов
                    float length = normal.Length();
                    if (length > Epsilon) {
                        normal = Vector3.Normalize(normal);
                    }

                    normals.Add(normal);
                }
            }

            // Вычисляем среднюю (композитную) нормаль
            var compositeNormal = Vector3.Zero;
            foreach (var n in normals) {
                compositeNormal += n;
            }
            compositeNormal /= normals.Count;

            // Вычисляем длину композитной нормали
            float lengthN = compositeNormal.Length();

            // Защита от деления на ноль
            if (lengthN < Epsilon) {
                return 0.0f; // Нет дисперсии для нулевого вектора
            }

            // Формула Toksvig из Unreal:
            // Variance = (1 - LengthN) / LengthN
            // Чем короче композитная нормаль, тем больше дисперсия
            float variance = (1.0f - lengthN) / lengthN;

            // Вычитаем небольшое смещение (как в Unreal) для уменьшения шума
            variance = Math.Max(0.0f, variance - 0.00004f);

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
        /// Применяет формулу Toksvig для коррекции roughness (адаптировано из Unreal Engine)
        /// </summary>
        /// <param name="roughness">Входное значение roughness [0,1]</param>
        /// <param name="variance">Дисперсия нормалей [0,1]</param>
        /// <param name="k">Composite Power (вес влияния)</param>
        /// <param name="useLinearPower">Использовать линейный CompositePower (true для Simplified режима)</param>
        /// <returns>Скорректированное значение roughness</returns>
        private float ApplyToksvigFormula(float roughness, float variance, float k, bool useLinearPower) {
            // Применяем CompositePower к дисперсии
            float adjustedVariance;
            if (useLinearPower) {
                // Simplified режим: линейная зависимость Variance *= CompositePower
                adjustedVariance = variance * k;
            } else {
                // Classic режим: степенная зависимость k^1.5 для более заметного эффекта при высоких значениях k
                // k=1.0 → 1.0 (без изменений), k=2.0 → 2.83, k=4.0 → 8.0, k=8.0 → 22.6
                adjustedVariance = variance * MathF.Pow(k, 1.5f);
            }

            // Конвертируем roughness в alpha (GGX)
            float a = roughness * roughness;
            float a2 = a * a;

            // Формула Toksvig из Unreal Engine:
            // B = 2 * variance * (a2 - 1)
            // a2_corrected = (B - a2) / (B - 1)
            float B = 2.0f * adjustedVariance * (a2 - 1.0f);

            // Защита от деления на ноль
            if (Math.Abs(B - 1.0f) < Epsilon) {
                return roughness; // Нет коррекции
            }

            float a2_corrected = (B - a2) / (B - 1.0f);

            // Clamp для предотвращения некорректных значений
            a2_corrected = Math.Clamp(a2_corrected, Epsilon * Epsilon, 1.0f);

            // Конвертируем обратно: roughness = a2^0.25
            float correctedRoughness = MathF.Pow(a2_corrected, 0.25f);

            return correctedRoughness;
        }

        /// <summary>
        /// Создаёт карту дисперсии для визуализации (для отладки)
        /// </summary>
        public Image<Rgba32> CreateVarianceVisualization(Image<Rgba32> normalMapImage, ToksvigSettings settings) {
            // Генерируем мипмапы для normal map
            // КРИТИЧНО: НЕ нормализуем нормали после фильтрации!
            // Нормализация должна происходить ВНУТРИ расчёта дисперсии (Energy preserving ДО Toksvig)
            var normalProfile = MipGenerationProfile.CreateDefault(TextureType.Normal);
            normalProfile.NormalizeNormals = false; // Отключаем глобальную нормализацию
            var normalMipmaps = _mipGenerator.GenerateMipmaps(normalMapImage, normalProfile);

            if (normalMipmaps.Count <= settings.MinToksvigMipLevel) {
                Logger.Warn("Недостаточно уровней мипмапов для визуализации");
                return new Image<Rgba32>(1, 1);
            }

            // Берём указанный уровень
            var normalMip = normalMipmaps[settings.MinToksvigMipLevel];

            // Вычисляем дисперсию в зависимости от режима
            Image<Rgba32> varianceMap;
            if (settings.CalculationMode == ToksvigCalculationMode.Simplified) {
                varianceMap = CalculateNormalVarianceSimplified(normalMip);
            } else {
                varianceMap = CalculateNormalVariance(normalMip);
            }

            // Применяем сглаживание только в Classic режиме
            if (settings.SmoothVariance && settings.CalculationMode == ToksvigCalculationMode.Classic) {
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

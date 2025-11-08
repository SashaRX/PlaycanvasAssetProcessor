using System.IO;
using AssetProcessor.TextureConversion.Analysis;
using AssetProcessor.TextureConversion.BasisU;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.KVD;
using AssetProcessor.TextureConversion.MipGeneration;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.Pipeline {
    /// <summary>
    /// Главный пайплайн конвертации текстур
    /// Объединяет генерацию мипмапов и сжатие в Basis Universal
    /// </summary>
    public class TextureConversionPipeline {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly MipGenerator _mipGenerator;
        private readonly KtxCreateWrapper _ktxCreateWrapper;
        private readonly ToksvigProcessor _toksvigProcessor;
        private readonly NormalMapMatcher _normalMapMatcher;
        private readonly HistogramAnalyzer _histogramAnalyzer;

        public TextureConversionPipeline(string? ktxExecutablePath = null) {
            _mipGenerator = new MipGenerator();
            _ktxCreateWrapper = new KtxCreateWrapper(ktxExecutablePath ?? "ktx");
            _toksvigProcessor = new ToksvigProcessor();
            _normalMapMatcher = new NormalMapMatcher();
            _histogramAnalyzer = new HistogramAnalyzer();
        }

        /// <summary>
        /// Конвертирует текстуру в KTX2 формат с генерацией мипмапов используя toktx
        /// </summary>
        /// <param name="inputPath">Путь к входной текстуре</param>
        /// <param name="outputPath">Путь к выходному файлу (.ktx2)</param>
        /// <param name="mipProfile">Профиль генерации мипмапов</param>
        /// <param name="compressionSettings">Настройки сжатия</param>
        /// <param name="toksvigSettings">Настройки Toksvig (optional, для gloss/roughness текстур)</param>
        /// <param name="saveSeparateMipmaps">Сохранить ли мипмапы отдельно</param>
        /// <param name="mipmapOutputDir">Директория для сохранения отдельных мипмапов</param>
        public async Task<ConversionResult> ConvertTextureAsync(
            string inputPath,
            string outputPath,
            MipGenerationProfile mipProfile,
            CompressionSettings compressionSettings,
            ToksvigSettings? toksvigSettings = null,
            bool saveSeparateMipmaps = false,
            string? mipmapOutputDir = null) {
            var result = new ConversionResult {
                InputPath = inputPath,
                OutputPath = outputPath
            };

            var startTime = DateTime.Now;

            try {
                Logger.Info($"Starting conversion: {inputPath}");

                // Проверяем доступность ktx
                if (!await _ktxCreateWrapper.IsAvailableAsync()) {
                    throw new Exception("ktx executable not found. Please specify path to ktx.exe in settings (e.g., KTX-Software/build_ktx/Release/ktx.exe)");
                }

                // Загружаем изображение
                using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath);
                Logger.Info($"Loaded image: {sourceImage.Width}x{sourceImage.Height}");

                // Определяем нужно ли генерировать мипмапы вручную
                List<Image<Rgba32>> mipmaps;

                if (compressionSettings.UseCustomMipmaps) {
                    // РУЧНАЯ ГЕНЕРАЦИЯ МИПМАПОВ: используем MipGenerator
                    Logger.Info("=== MANUAL MIPMAP GENERATION (UseCustomMipmaps=true) ===");
                    Logger.Info("Generating mipmaps manually with MipGenerator...");
                    mipmaps = _mipGenerator.GenerateMipmaps(sourceImage, mipProfile);
                    Logger.Info($"Generated {mipmaps.Count} mipmap levels");

                    // Выводим информацию о каждом мипмапе
                    for (int i = 0; i < mipmaps.Count; i++) {
                        Logger.Info($"Mipmap level {i}: {mipmaps[i].Width}x{mipmaps[i].Height}");
                    }
                } else {
                    // АВТОМАТИЧЕСКАЯ ГЕНЕРАЦИЯ: toktx сам сгенерирует мипмапы с --genmipmap
                    Logger.Info("=== AUTOMATIC MIPMAP GENERATION (UseCustomMipmaps=false) ===");
                    Logger.Info("Will pass only source image to toktx, it will generate mipmaps automatically with --genmipmap");
                    Logger.Info("This allows --normal_mode and --normalize flags to work correctly");

                    // Создаем список с ОДНИМ изображением (клон оригинала)
                    mipmaps = new List<Image<Rgba32>> { sourceImage.Clone() };
                    Logger.Info($"Created single-image list for toktx: {mipmaps[0].Width}x{mipmaps[0].Height}");
                }

                // Применяем Toksvig коррекцию если включена (для gloss/roughness текстур)
                Logger.Info($"=== TOKSVIG CHECK ===");
                Logger.Info($"  toksvigSettings != null: {toksvigSettings != null}");
                if (toksvigSettings != null) {
                    Logger.Info($"  toksvigSettings.Enabled: {toksvigSettings.Enabled}");
                    Logger.Info($"  toksvigSettings.CompositePower: {toksvigSettings.CompositePower}");
                    Logger.Info($"  toksvigSettings.NormalMapPath: {toksvigSettings.NormalMapPath ?? "(null - auto)"}");
                }
                Logger.Info($"  mipProfile.TextureType: {mipProfile.TextureType}");
                Logger.Info($"  Is Gloss or Roughness: {mipProfile.TextureType == TextureType.Gloss || mipProfile.TextureType == TextureType.Roughness}");
                Logger.Info($"  UseCustomMipmaps: {compressionSettings.UseCustomMipmaps}");

                if (toksvigSettings != null && toksvigSettings.Enabled &&
                    (mipProfile.TextureType == TextureType.Gloss || mipProfile.TextureType == TextureType.Roughness)) {

                    // КРИТИЧНО: Toksvig требует ручную генерацию мипмапов!
                    if (!compressionSettings.UseCustomMipmaps) {
                        Logger.Error("=== TOKSVIG ERROR ===");
                        Logger.Error("  Toksvig correction requires Custom Mipmaps (Manual) to be enabled!");
                        Logger.Error("  Toksvig works by analyzing and modifying each mipmap level individually.");
                        Logger.Error("  Automatic mipmaps (toktx --genmipmap) generate mipmaps AFTER Toksvig cannot be applied.");
                        Logger.Error("  Skipping Toksvig correction.");
                        result.ToksvigApplied = false;
                    } else {
                        Logger.Info("=== ПРИМЕНЯЕМ TOKSVIG КОРРЕКЦИЮ ===");

                    try {
                        // Ищем normal map если путь не указан
                        string? normalMapPath = toksvigSettings.NormalMapPath;
                        if (string.IsNullOrEmpty(normalMapPath)) {
                            Logger.Info("Путь к normal map не указан, выполняем автоматический поиск...");
                            normalMapPath = _normalMapMatcher.FindNormalMapAuto(inputPath, validateDimensions: true);
                        }

                        if (string.IsNullOrEmpty(normalMapPath)) {
                            Logger.Warn("Normal map не найдена, пропускаем Toksvig коррекцию");
                        } else {
                            // Загружаем normal map
                            using var normalMapImage = await Image.LoadAsync<Rgba32>(normalMapPath);
                            Logger.Info($"Загружена normal map: {normalMapPath} ({normalMapImage.Width}x{normalMapImage.Height})");

                            // Определяем, gloss или roughness
                            bool isGloss = mipProfile.TextureType == TextureType.Gloss;
                            if (isGloss) {
                                var detectedType = _normalMapMatcher.IsGlossByName(inputPath);
                                if (detectedType.HasValue) {
                                    isGloss = detectedType.Value;
                                    Logger.Info($"Определён тип текстуры по имени: {(isGloss ? "Gloss" : "Roughness")}");
                                }
                            }

                            // Применяем Toksvig (возвращает скорректированные мипмапы И карту дисперсии)
                            var (correctedMipmaps, varianceMipmaps) = _toksvigProcessor.ApplyToksvigCorrectionWithVariance(
                                mipmaps,
                                normalMapImage,
                                toksvigSettings,
                                isGloss
                            );

                            // ЕСЛИ Keep Temporal Mipmaps включен - сохраняем ВСЕ ТРИ НАБОРА МИПМАПОВ
                            if (!compressionSettings.RemoveTemporaryMipmaps) {
                                // 1. ОРИГИНАЛЬНЫЕ мипмапы БЕЗ Toksvig (gloss/roughness до коррекции)
                                await SaveDebugMipmapsAsync(inputPath, mipmaps, "_gloss_mip");

                                // 2. Карта дисперсии Toksvig (показывает влияние normal map)
                                if (varianceMipmaps != null) {
                                    await SaveDebugMipmapsAsync(inputPath, varianceMipmaps, "_toksvig_variance_mip");
                                }

                                // 3. КОМПОЗИТНЫЕ мипмапы (gloss/roughness + toksvig correction)
                                await SaveDebugMipmapsAsync(inputPath, correctedMipmaps, "_composite_mip");

                                // ВЕРИФИКАЦИЯ: проверяем что файлы на диске РЕАЛЬНО различаются
                                await VerifyDebugMipmapsOnDisk(inputPath, "_gloss_mip", "_composite_mip");
                            }

                            // Освобождаем variance mipmaps (уже не нужны)
                            if (varianceMipmaps != null) {
                                foreach (var vmap in varianceMipmaps) {
                                    vmap.Dispose();
                                }
                            }

                            // КРИТИЧНО: НЕ освобождаем old mipmaps сразу!
                            // Сохраняем их для освобождения в конце метода (после toktx packing)
                            // Это предотвращает race condition если async save не завершился
                            var oldMipmaps = mipmaps;

                            Logger.Info($"Переключаемся на corrected mipmaps ({correctedMipmaps.Count} изображений)");
                            mipmaps = correctedMipmaps;
                            result.ToksvigApplied = true;
                            result.NormalMapUsed = normalMapPath;
                            result.OldMipmapsToDispose = oldMipmaps; // Сохраняем для освобождения позже

                            Logger.Info("Toksvig коррекция успешно применена");
                        }
                    } catch (Exception ex) {
                        Logger.Error(ex, "Ошибка при применении Toksvig коррекции");
                        result.ToksvigApplied = false;
                        // Продолжаем с оригинальными мипмапами
                    }
                    }
                }

                var fileName = Path.GetFileNameWithoutExtension(inputPath);

                // Флаг: были ли сохранены debug mipmaps для Toksvig (чтобы избежать дублирования)
                bool toksvigDebugMipmapsSaved = result.ToksvigApplied && !compressionSettings.RemoveTemporaryMipmaps;

                // ВСЕГДА сохраняем все мипмапы во временную директорию для toktx
                var tempMipmapDir = Path.Combine(Path.GetTempPath(), "TexTool_Mipmaps", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempMipmapDir);
                Logger.Info($"Создана временная директория для мипмапов: {tempMipmapDir}");

                var tempMipmapPaths = new List<string>();

                try {
                    // Сохраняем все мипмапы как временные PNG для toktx
                    Logger.Info($"Сохраняем {mipmaps.Count} мипмапов для toktx...");
                    for (int i = 0; i < mipmaps.Count; i++) {
                        var mipPath = Path.Combine(tempMipmapDir, $"{fileName}_mip{i}.png");
                        Logger.Info($"Сохраняем mipmap level {i} ({mipmaps[i].Width}x{mipmaps[i].Height}) в: {mipPath}");

                        try {
                            await mipmaps[i].SaveAsPngAsync(mipPath);
                        } catch (ObjectDisposedException disposeEx) {
                            Logger.Error($"КРИТИЧЕСКАЯ ОШИБКА: mip[{i}] УЖЕ ОСВОБОЖДЕН!");
                            Logger.Error($"  Размер изображения был: {mipmaps[i].Width}x{mipmaps[i].Height}");
                            Logger.Error($"  Exception: {disposeEx.Message}");
                            throw;
                        }

                        // Проверяем создание файла
                        if (File.Exists(mipPath)) {
                            var fileInfo = new FileInfo(mipPath);
                            Logger.Info($"✓ Mipmap {i} сохранён ({fileInfo.Length} bytes)");
                            tempMipmapPaths.Add(mipPath);
                        } else {
                            Logger.Error($"✗ Не удалось сохранить mipmap {i}!");
                            throw new Exception($"Failed to save mipmap level {i}");
                        }
                    }

                    // Опционально: копируем мипмапы в директорию пользователя
                    if (saveSeparateMipmaps && !string.IsNullOrEmpty(mipmapOutputDir)) {
                        Logger.Info($"Копируем {mipmaps.Count} мипмапов в {mipmapOutputDir}");
                        Directory.CreateDirectory(mipmapOutputDir);

                        for (int i = 0; i < tempMipmapPaths.Count; i++) {
                            var destPath = Path.Combine(mipmapOutputDir, $"{fileName}_mip{i}.png");
                            File.Copy(tempMipmapPaths[i], destPath, overwrite: true);
                            Logger.Info($"✓ Mipmap {i} скопирован в {destPath}");
                        }

                        result.MipmapsSavedPath = mipmapOutputDir;
                        Logger.Info($"Мипмапы сохранены в {mipmapOutputDir}");
                    }

                    // HISTOGRAM ANALYSIS - анализ перед упаковкой
                    HistogramResult? histogramResult = null;
                    Dictionary<string, string>? kvdBinaryFiles = null;

                    if (compressionSettings.HistogramAnalysis != null &&
                        compressionSettings.HistogramAnalysis.Mode != HistogramMode.Off) {

                        Logger.Info("=== HISTOGRAM ANALYSIS ===");

                        // Анализируем первый мипмап (mip0)
                        using var mip0 = await Image.LoadAsync<Rgba32>(tempMipmapPaths[0]);
                        histogramResult = _histogramAnalyzer.Analyze(mip0, compressionSettings.HistogramAnalysis);

                        if (histogramResult.Success) {
                            Logger.Info($"Histogram analysis successful");
                            Logger.Info($"  Scale: [{string.Join(", ", histogramResult.Scale.Select(s => s.ToString("F4")))}]");
                            Logger.Info($"  Offset: [{string.Join(", ", histogramResult.Offset.Select(o => o.ToString("F4")))}]");
                            Logger.Info($"  Quality mode: {compressionSettings.HistogramAnalysis.Quality}");

                            // ВСЕГДА применяем Preprocessing (нормализацию текстуры)
                            Logger.Info("=== HISTOGRAM PREPROCESSING ===");
                            Logger.Info("Normalizing texture before compression, scale/offset will be stored in KVD for GPU recovery");

                            // Применяем трансформацию к ВСЕМ мипмапам
                            for (int i = 0; i < mipmaps.Count; i++) {
                                Image<Rgba32> transformedMip;

                                if (compressionSettings.HistogramAnalysis.Mode == HistogramMode.PercentileWithKnee) {
                                    // Soft-knee (High Quality)
                                    float kneeWidth = compressionSettings.HistogramAnalysis.KneeWidth;
                                    Logger.Info($"Applying soft-knee (width={kneeWidth:F4}) to mip {i}");
                                    transformedMip = _histogramAnalyzer.ApplySoftKnee(
                                        mipmaps[i],
                                        histogramResult,
                                        kneeWidth
                                    );
                                } else {
                                    // Winsorization (Fast mode - жёсткое клампирование)
                                    Logger.Info($"Applying winsorization to mip {i}");
                                    transformedMip = _histogramAnalyzer.ApplyWinsorization(
                                        mipmaps[i],
                                        histogramResult
                                    );
                                }

                                // Заменяем мипмап
                                mipmaps[i].Dispose();
                                mipmaps[i] = transformedMip;
                            }

                            Logger.Info($"Preprocessing applied to {mipmaps.Count} mipmaps");

                            // Пересохраняем трансформированные мипмапы
                            for (int i = 0; i < mipmaps.Count; i++) {
                                var mipPath = tempMipmapPaths[i];
                                await mipmaps[i].SaveAsPngAsync(mipPath);
                                Logger.Info($"✓ Preprocessed mip {i} saved to {mipPath}");
                            }

                            // Создаём TLV метаданные
                            using var tlvWriter = new TLVWriter();

                            // ВСЕГДА инвертируем scale/offset для восстановления на GPU
                            // т.к. текстура нормализована: v_norm = (v - lo) / (hi - lo)
                            // GPU применит обратное преобразование: v_original = v_norm * (hi - lo) + lo
                            Logger.Info("=== INVERTING SCALE/OFFSET FOR GPU RECOVERY ===");
                            Logger.Info("Texture normalized, computing inverse transform for GPU denormalization");
                            Logger.Info($"Forward transform: scale=[{string.Join(", ", histogramResult.Scale.Select(s => s.ToString("F4")))}], offset=[{string.Join(", ", histogramResult.Offset.Select(o => o.ToString("F4")))}]");

                            // Создаём копию для TLV с инвертированными параметрами
                            var histogramForTLV = new HistogramResult {
                                Success = histogramResult.Success,
                                Mode = histogramResult.Mode,
                                ChannelMode = histogramResult.ChannelMode,
                                Scale = new float[histogramResult.Scale.Length],
                                Offset = new float[histogramResult.Offset.Length],
                                RangeLow = histogramResult.RangeLow,
                                RangeHigh = histogramResult.RangeHigh,
                                TailFraction = histogramResult.TailFraction,
                                KneeApplied = histogramResult.KneeApplied,
                                TotalPixels = histogramResult.TotalPixels,
                                Error = histogramResult.Error,
                                Warnings = new List<string>(histogramResult.Warnings)
                            };

                            // Инвертируем каждый канал: scale_inv = 1/scale, offset_inv = -offset/scale
                            for (int i = 0; i < histogramResult.Scale.Length; i++) {
                                float scale = histogramResult.Scale[i];
                                float offset = histogramResult.Offset[i];

                                histogramForTLV.Scale[i] = 1.0f / scale;
                                histogramForTLV.Offset[i] = -offset / scale;
                            }

                            Logger.Info($"Inverse transform: scale=[{string.Join(", ", histogramForTLV.Scale.Select(s => s.ToString("F4")))}], offset=[{string.Join(", ", histogramForTLV.Offset.Select(o => o.ToString("F4")))}]");
                            Logger.Info("GPU will apply: v_original = fma(v_normalized, scale, offset)");

                            // Записываем результат анализа (Half16 формат)
                            tlvWriter.WriteHistogramResult(histogramForTLV, HistogramQuantization.Half16);

                            // ВСЕГДА записываем параметры анализа для справки
                            tlvWriter.WriteHistogramParams(compressionSettings.HistogramAnalysis);

                            // Сохраняем TLV в временный файл
                            var tlvPath = Path.Combine(tempMipmapDir, "pc.meta.bin");
                            tlvWriter.SaveToFile(tlvPath);
                            Logger.Info($"TLV metadata saved to: {tlvPath}");

                            // Проверяем создание файла
                            if (File.Exists(tlvPath)) {
                                var fileInfo = new FileInfo(tlvPath);
                                Logger.Info($"TLV file size: {fileInfo.Length} bytes");

                                // Добавляем в словарь KVD файлов для toktx
                                kvdBinaryFiles = new Dictionary<string, string> {
                                    { "pc.meta", tlvPath }
                                };
                            } else {
                                Logger.Warn("TLV file not created, skipping KVD metadata");
                            }

                            result.HistogramAnalysisResult = histogramResult;
                        } else {
                            Logger.Warn($"Histogram analysis failed: {histogramResult.Error}");
                        }
                    }

                    // NORMAL MAP LAYOUT METADATA - независимо от histogram
                    if (mipProfile.TextureType == TextureType.Normal && compressionSettings.ConvertToNormalMap) {
                        Logger.Info("=== NORMAL MAP LAYOUT METADATA ===");

                        NormalLayout normalLayout;

                        // Определяем схему на основе формата компрессии
                        if (compressionSettings.CompressionFormat == CompressionFormat.ETC1S) {
                            // ETC1S: X в RGB (все каналы), Y в A
                            normalLayout = NormalLayout.RGBxAy;
                            Logger.Info("ETC1S normal map: using RGBxAy layout (X in RGB, Y in A)");
                        } else {
                            // UASTC/BC5: X в R, Y в G
                            normalLayout = NormalLayout.RG;
                            Logger.Info("UASTC/BC5 normal map: using RG layout (X in R, Y in G)");
                        }

                        // Создаём или дополняем TLV metadata
                        if (kvdBinaryFiles == null) {
                            // Histogram не был включен, создаём новый TLV writer
                            using var tlvWriter = new TLVWriter();
                            tlvWriter.WriteNormalLayout(normalLayout);

                            // Сохраняем TLV в временный файл
                            var tlvPath = Path.Combine(tempMipmapDir, "pc.meta.bin");
                            tlvWriter.SaveToFile(tlvPath);
                            Logger.Info($"TLV metadata (NormalLayout only) saved to: {tlvPath}");

                            if (File.Exists(tlvPath)) {
                                var fileInfo = new FileInfo(tlvPath);
                                Logger.Info($"TLV file size: {fileInfo.Length} bytes");

                                kvdBinaryFiles = new Dictionary<string, string> {
                                    { "pc.meta", tlvPath }
                                };
                            }
                        } else {
                            // Histogram был включен, дополняем существующий TLV файл
                            var existingTlvPath = kvdBinaryFiles["pc.meta"];

                            // TLV формат позволяет просто добавлять блоки в конец
                            // Создаём новый TLV только с NormalLayout
                            using var normalLayoutWriter = new TLVWriter();
                            normalLayoutWriter.WriteNormalLayout(normalLayout);
                            var normalLayoutBytes = normalLayoutWriter.ToArray();

                            // Добавляем к существующему файлу
                            using var fileStream = new FileStream(existingTlvPath, FileMode.Append);
                            fileStream.Write(normalLayoutBytes, 0, normalLayoutBytes.Length);

                            Logger.Info($"NormalLayout metadata appended to existing TLV: {existingTlvPath}");
                        }

                        Logger.Info($"✓ Normal map layout metadata written: {normalLayout}");
                    }

                    // Упаковываем мипмапы в KTX2 используя ktx create
                    Logger.Info("=== PACKING TO KTX2 WITH KTX CREATE ===");
                    Logger.Info($"  Mipmaps: {tempMipmapPaths.Count}");
                    Logger.Info($"  Output: {outputPath}");
                    Logger.Info($"  Compression: {compressionSettings.CompressionFormat}");
                    if (kvdBinaryFiles != null) {
                        Logger.Info($"  KVD files: {kvdBinaryFiles.Count}");
                    }

                    var ktxResult = await _ktxCreateWrapper.PackMipmapsAsync(
                        tempMipmapPaths,
                        outputPath,
                        compressionSettings,
                        kvdBinaryFiles
                    );

                    if (!ktxResult.Success) {
                        throw new Exception($"ktx create failed: {ktxResult.Error}");
                    }

                    // POST-PROCESSING: Inject TLV metadata if available
                    if (kvdBinaryFiles != null && kvdBinaryFiles.Count > 0) {
                        Logger.Info("=== POST-PROCESSING: METADATA INJECTION ===");

                        // Получаем директорию ktx для загрузки ktx.dll
                        var ktxDllDirectory = _ktxCreateWrapper.KtxDirectory;
                        if (!string.IsNullOrEmpty(ktxDllDirectory)) {
                            Logger.Info($"ktx directory: {ktxDllDirectory}");
                        } else {
                            Logger.Warn("ktx directory not found (ktx might be in PATH)");
                        }

                        foreach (var kvPair in kvdBinaryFiles) {
                            Logger.Info($"Injecting metadata: key='{kvPair.Key}', file='{kvPair.Value}'");
                            bool injected = Ktx2MetadataInjector.InjectMetadata(
                                outputPath,
                                kvPair.Value,
                                kvPair.Key,
                                ktxDllDirectory
                            );
                            if (injected) {
                                Logger.Info($"✓ Metadata '{kvPair.Key}' injected successfully");
                            } else {
                                Logger.Error($"✗ Failed to inject metadata '{kvPair.Key}'");
                                throw new Exception($"Failed to inject metadata '{kvPair.Key}'");
                            }
                        }
                    }

                    result.Success = true;
                    result.BasisOutput = ktxResult.Output;
                    result.MipLevels = mipmaps.Count;

                    Logger.Info($"=== KTX2 PACKING SUCCESS ===");
                    Logger.Info($"  Output: {outputPath}");
                    Logger.Info($"  File size: {ktxResult.OutputFileSize} bytes");
                    Logger.Info($"  Mip levels: {mipmaps.Count}");

                } finally {
                    // Обрабатываем временные мипмапы
                    try {
                        if (Directory.Exists(tempMipmapDir)) {
                            // Если Keep Temporal Mipmaps включен - копируем в debug папку
                            // НО: пропускаем если уже сохранили Toksvig debug mipmaps (чтобы избежать дублирования)
                            if (!compressionSettings.RemoveTemporaryMipmaps && !toksvigDebugMipmapsSaved) {
                                // Создаём папку "mipmaps" рядом с текстурой
                                var textureDir = Path.GetDirectoryName(inputPath);
                                if (!string.IsNullOrEmpty(textureDir)) {
                                    var debugMipmapDir = Path.Combine(textureDir, "mipmaps");
                                    Directory.CreateDirectory(debugMipmapDir);

                                    // Копируем все мипмапы из temp в debug папку
                                    var tempFiles = Directory.GetFiles(tempMipmapDir, "*.png");
                                    foreach (var tempFile in tempFiles) {
                                        var tempFileName = Path.GetFileName(tempFile);
                                        var debugPath = Path.Combine(debugMipmapDir, tempFileName);
                                        File.Copy(tempFile, debugPath, overwrite: true);
                                    }

                                    Logger.Info($"✓ Debug mipmaps сохранены в: {debugMipmapDir} ({tempFiles.Length} файлов)");
                                }
                            } else if (toksvigDebugMipmapsSaved) {
                                Logger.Info($"Пропускаем копирование temp mipmaps (уже сохранены Toksvig debug mipmaps)");
                            }

                            // Всегда удаляем временную директорию (даже если копировали)
                            Directory.Delete(tempMipmapDir, recursive: true);
                            Logger.Info($"Временная директория удалена: {tempMipmapDir}");
                        }
                    } catch (Exception ex) {
                        Logger.Warn($"Не удалось обработать временную директорию: {ex.Message}");
                    }
                }

                Logger.Info($"Conversion successful: {outputPath}");

                // Освобождаем память (текущие corrected mipmaps)
                foreach (var mip in mipmaps) {
                    mip.Dispose();
                }

                // Освобождаем старые оригинальные мипмapы (если были сохранены в Toksvig)
                if (result.OldMipmapsToDispose != null) {
                    Logger.Info($"Освобождаем {result.OldMipmapsToDispose.Count} оригинальных мипмапов (отложенное освобождение)...");
                    foreach (var oldMip in result.OldMipmapsToDispose) {
                        try {
                            oldMip.Dispose();
                        } catch (Exception disposeEx) {
                            Logger.Warn($"Не удалось освободить старый мипмап: {disposeEx.Message}");
                        }
                    }
                    result.OldMipmapsToDispose = null;
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Conversion failed: {inputPath}");
                result.Success = false;
                result.Error = ex.Message;

                // Освобождаем старые мипмапы даже при ошибке
                if (result.OldMipmapsToDispose != null) {
                    foreach (var oldMip in result.OldMipmapsToDispose) {
                        try {
                            oldMip.Dispose();
                        } catch {
                            // Игнорируем ошибки при cleanup
                        }
                    }
                    result.OldMipmapsToDispose = null;
                }
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Сохраняет мипмапы в debug папку для инспекции
        /// </summary>
        /// <param name="inputPath">Путь к исходной текстуре</param>
        /// <param name="mipmaps">Список мипмапов для сохранения</param>
        /// <param name="suffix">Суффикс для имени файла (например "_gloss" или "_toksvig_variance")</param>
        private async Task SaveDebugMipmapsAsync(string inputPath, List<Image<Rgba32>> mipmaps, string suffix) {
            try {
                var textureDir = Path.GetDirectoryName(inputPath);
                if (string.IsNullOrEmpty(textureDir)) return;

                // КРИТИЧНО: Удаляем известные суффиксы текстур из fileName чтобы избежать дублирования!
                // Например: "dirtyLada_gloss.png" → "dirtyLada" (без "_gloss")
                var fileName = Path.GetFileNameWithoutExtension(inputPath);

                // Список суффиксов текстур для удаления
                string[] textureSuffixes = { "_gloss", "_glossiness", "_smoothness", "_albedo", "_diffuse", "_normal", "_roughness", "_metallic", "_ao", "_emissive" };
                foreach (var texSuffix in textureSuffixes) {
                    if (fileName.EndsWith(texSuffix, StringComparison.OrdinalIgnoreCase)) {
                        fileName = fileName.Substring(0, fileName.Length - texSuffix.Length);
                        break; // Удаляем только один суффикс
                    }
                }

                var debugMipmapDir = Path.Combine(textureDir, "mipmaps");
                Directory.CreateDirectory(debugMipmapDir);

                // Вычисляем SHA256 hash для mip0 и mip1 для верификации
                string hash0 = "N/A", hash1 = "N/A";
                if (mipmaps.Count > 0) {
                    using var ms = new MemoryStream();
                    mipmaps[0].SaveAsPng(ms);
                    ms.Position = 0;
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    var hashBytes = sha256.ComputeHash(ms);
                    hash0 = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 12);
                }
                if (mipmaps.Count > 1) {
                    using var ms = new MemoryStream();
                    mipmaps[1].SaveAsPng(ms);
                    ms.Position = 0;
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    var hashBytes = sha256.ComputeHash(ms);
                    hash1 = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 12);
                }

                Logger.Info($"Сохраняем {mipmaps.Count} debug mipmaps{suffix}... (mip0: {hash0}, mip1: {hash1})");

                for (int i = 0; i < mipmaps.Count; i++) {
                    var debugPath = Path.Combine(debugMipmapDir, $"{fileName}{suffix}_mip{i}.png");
                    try {
                        Logger.Debug($"  Saving debug mip{suffix}[{i}]: {mipmaps[i].Width}x{mipmaps[i].Height} -> {debugPath}");

                        // КРИТИЧНО: Сохраняем синхронно в MemoryStream, затем записываем на диск
                        // Это гарантирует что данные зафиксированы ДО того как image может быть изменён
                        using var ms = new MemoryStream();
                        await mipmaps[i].SaveAsPngAsync(ms);
                        ms.Position = 0;

                        // Теперь записываем из MemoryStream на диск
                        using var fs = new FileStream(debugPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await ms.CopyToAsync(fs);
                        await fs.FlushAsync();

                        // КРИТИЧНО: Flush(true) заставляет ОС записать данные на диск НЕМЕДЛЕННО
                        fs.Flush(flushToDisk: true);

                        // Проверяем что файл реально создан И имеет правильный размер
                        if (!File.Exists(debugPath)) {
                            Logger.Error($"КРИТИЧЕСКАЯ ОШИБКА: файл {debugPath} НЕ СОЗДАН после SaveAsPngAsync!");
                        } else {
                            var fileInfo = new FileInfo(debugPath);
                            if (fileInfo.Length != ms.Length) {
                                Logger.Error($"КРИТИЧЕСКАЯ ОШИБКА: файл {debugPath} создан, но размер неверный! Expected: {ms.Length}, actual: {fileInfo.Length}");
                            }
                        }
                    } catch (ObjectDisposedException disposeEx) {
                        Logger.Error($"КРИТИЧЕСКАЯ ОШИБКА: debug mip{suffix}[{i}] УЖЕ ОСВОБОЖДЕН ПРИ СОХРАНЕНИИ!");
                        Logger.Error($"  debugPath: {debugPath}");
                        Logger.Error($"  Exception: {disposeEx.Message}");
                        throw;
                    }
                }

                Logger.Info($"✓ Debug mipmaps{suffix} сохранены в: {debugMipmapDir} ({mipmaps.Count} файлов)");
            } catch (Exception ex) {
                Logger.Warn($"Не удалось сохранить debug mipmaps{suffix}: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет что файлы на диске РЕАЛЬНО различаются (для отладки race condition)
        /// </summary>
        private async Task VerifyDebugMipmapsOnDisk(string inputPath, string suffix1, string suffix2) {
            try {
                var textureDir = Path.GetDirectoryName(inputPath);
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var debugMipmapDir = Path.Combine(textureDir!, "mipmaps");

                Logger.Info($"━━━ ВЕРИФИКАЦИЯ ФАЙЛОВ НА ДИСКЕ ━━━");
                Logger.Info($"  inputPath: {inputPath}");
                Logger.Info($"  fileName: {fileName}");
                Logger.Info($"  debugMipmapDir: {debugMipmapDir}");
                Logger.Info($"  Directory.Exists: {Directory.Exists(debugMipmapDir)}");

                if (!Directory.Exists(debugMipmapDir)) {
                    Logger.Error($"  ✗ Debug mipmap directory НЕ СУЩЕСТВУЕТ: {debugMipmapDir}");
                    Logger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    return;
                }

                // Проверяем первые 3 мипа (обычно mip0-2 самые важные)
                for (int i = 0; i < 3; i++) {
                    var path1 = Path.Combine(debugMipmapDir, $"{fileName}{suffix1}_mip{i}.png");
                    var path2 = Path.Combine(debugMipmapDir, $"{fileName}{suffix2}_mip{i}.png");

                    bool exists1 = File.Exists(path1);
                    bool exists2 = File.Exists(path2);

                    if (!exists1 || !exists2) {
                        Logger.Warn($"  ⚠ mip{i}: файлы не найдены (exists1={exists1}, exists2={exists2})");
                        Logger.Warn($"    path1: {path1}");
                        Logger.Warn($"    path2: {path2}");
                        continue;
                    }

                    // Вычисляем SHA256 hash для обоих файлов НА ДИСКЕ
                    string hash1, hash2;
                    using (var sha256 = System.Security.Cryptography.SHA256.Create()) {
                        using (var fs1 = File.OpenRead(path1)) {
                            var hashBytes1 = await sha256.ComputeHashAsync(fs1);
                            hash1 = BitConverter.ToString(hashBytes1).Replace("-", "").Substring(0, 12);
                        }
                        sha256.Initialize(); // Сбрасываем для второго файла
                        using (var fs2 = File.OpenRead(path2)) {
                            var hashBytes2 = await sha256.ComputeHashAsync(fs2);
                            hash2 = BitConverter.ToString(hashBytes2).Replace("-", "").Substring(0, 12);
                        }
                    }

                    // Сравниваем хеши
                    if (hash1 == hash2) {
                        Logger.Error($"  ✗ mip{i}: ФАЙЛЫ ИДЕНТИЧНЫ НА ДИСКЕ! hash={hash1}");
                        Logger.Error($"    {suffix1}: {path1}");
                        Logger.Error($"    {suffix2}: {path2}");
                    } else {
                        Logger.Info($"  ✓ mip{i}: файлы различаются ({suffix1}: {hash1}, {suffix2}: {hash2})");
                    }
                }

                Logger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            } catch (Exception ex) {
                Logger.Warn($"Не удалось верифицировать debug mipmaps: {ex.Message}");
            }
        }

        /// <summary>
        /// Конвертирует только мипмапы без сжатия (для тестирования)
        /// </summary>
        public async Task<List<string>> GenerateMipmapsOnlyAsync(
            string inputPath,
            string outputDirectory,
            MipGenerationProfile profile) {
            using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath);
            var mipmaps = _mipGenerator.GenerateMipmaps(sourceImage, profile);

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            Directory.CreateDirectory(outputDirectory);

            var outputPaths = new List<string>();

            for (int i = 0; i < mipmaps.Count; i++) {
                var outputPath = Path.Combine(outputDirectory, $"{fileName}_mip{i}.png");
                await mipmaps[i].SaveAsPngAsync(outputPath);
                outputPaths.Add(outputPath);
            }

            foreach (var mip in mipmaps) {
                mip.Dispose();
            }

            return outputPaths;
        }
    }

    /// <summary>
    /// Результат конвертации текстуры
    /// </summary>
    public class ConversionResult {
        /// <summary>
        /// Успешно ли выполнена конвертация
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Путь к входному файлу
        /// </summary>
        public string InputPath { get; set; } = string.Empty;

        /// <summary>
        /// Путь к выходному файлу
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Сообщение об ошибке (если есть)
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Вывод basisu encoder
        /// </summary>
        public string? BasisOutput { get; set; }

        /// <summary>
        /// Количество уровней мипмапов
        /// </summary>
        public int MipLevels { get; set; }

        /// <summary>
        /// Путь к директории с сохраненными отдельными мипмапами (если применимо)
        /// </summary>
        public string? MipmapsSavedPath { get; set; }

        /// <summary>
        /// Длительность конвертации
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Была ли применена Toksvig коррекция
        /// </summary>
        public bool ToksvigApplied { get; set; }

        /// <summary>
        /// Путь к использованной normal map (если Toksvig применён)
        /// </summary>
        public string? NormalMapUsed { get; set; }

        /// <summary>
        /// ВНУТРЕННЕЕ: Старые мипмапы для освобождения после завершения
        /// </summary>
        internal List<Image<Rgba32>>? OldMipmapsToDispose { get; set; }

        /// <summary>
        /// Результат анализа гистограммы (если применимо)
        /// </summary>
        public HistogramResult? HistogramAnalysisResult { get; set; }
    }
}

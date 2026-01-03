using System;
using System.Collections.Generic;
using System.IO;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.MipGeneration;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetProcessor.TextureConversion.Pipeline {
    /// <summary>
    /// Пайплайн для упаковки нескольких каналов (AO, Gloss, Metallic, Height) в единую RGBA текстуру
    /// </summary>
    public class ChannelPackingPipeline {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly MipGenerator _mipGenerator;
        private readonly ToksvigProcessor _toksvigProcessor;
        private readonly AOProcessor _aoProcessor;
        private readonly ORMTextureDetector _textureDetector;

        public ChannelPackingPipeline() {
            _mipGenerator = new MipGenerator();
            _toksvigProcessor = new ToksvigProcessor();
            _aoProcessor = new AOProcessor();
            _textureDetector = new ORMTextureDetector();
        }

        /// <summary>
        /// Упаковывает несколько каналов в единую RGBA текстуру с мипмапами
        /// </summary>
        /// <param name="packingSettings">Настройки упаковки каналов</param>
        /// <param name="outputSize">Размер выходной текстуры (если null - используется размер первого канала)</param>
        /// <returns>Список мипмапов упакованной текстуры</returns>
        public async Task<List<Image<Rgba32>>> PackChannelsAsync(
            ChannelPackingSettings packingSettings,
            (int width, int height)? outputSize = null) {

            if (!packingSettings.Validate(out var error)) {
                throw new ArgumentException($"Invalid packing settings: {error}");
            }

            Logger.Info($"=== CHANNEL PACKING START ===");
            Logger.Info($"  Mode: {packingSettings.Mode}");
            Logger.Info($"  Description: {packingSettings.GetModeDescription()}");

            // Шаг 1: Загружаем и генерируем мипмапы для каждого канала
            var channelMipmaps = new Dictionary<ChannelType, List<Image<Rgba32>>>();

            try {
                foreach (var channelSettings in packingSettings.GetActiveChannels()) {
                    Logger.Info($"--- Processing {channelSettings.ChannelType} channel ---");

                    if (string.IsNullOrEmpty(channelSettings.SourcePath)) {
                        throw new ArgumentException($"SourcePath is required for {channelSettings.ChannelType} channel");
                    }

                    // Загружаем и обрабатываем текстуру
                    var mipmaps = await ProcessChannelTextureAsync(channelSettings, outputSize);
                    channelMipmaps[channelSettings.ChannelType] = mipmaps;
                    Logger.Info($"  ✓ {channelSettings.ChannelType}: {mipmaps.Count} mipmap levels generated");
                }

                // Шаг 2: Определяем размер выходной текстуры
                var firstChannel = channelMipmaps.Values.First();
                int finalWidth = firstChannel[0].Width;
                int finalHeight = firstChannel[0].Height;
                int mipCount = firstChannel.Count;

                Logger.Info($"=== PACKING CHANNELS ===");
                Logger.Info($"  Output size: {finalWidth}x{finalHeight}");
                Logger.Info($"  Mip levels: {mipCount}");

                // Шаг 3: Упаковываем каналы в RGBA
                var packedMipmaps = new List<Image<Rgba32>>();

                for (int level = 0; level < mipCount; level++) {
                    int mipWidth = Math.Max(1, finalWidth >> level);
                    int mipHeight = Math.Max(1, finalHeight >> level);

                    var packedMip = PackMipmapLevel(
                        packingSettings,
                        channelMipmaps,
                        level,
                        mipWidth,
                        mipHeight
                    );
                    packedMipmaps.Add(packedMip);
                    Logger.Info($"  ✓ Mip{level} packed: {packedMip.Width}x{packedMip.Height}");
                }

                Logger.Info($"=== CHANNEL PACKING COMPLETE ===");
                Logger.Info($"  Total mipmaps: {packedMipmaps.Count}");

                return packedMipmaps;

            } finally {
                // Освобождаем память промежуточных мипмапов
                foreach (var mipmaps in channelMipmaps.Values) {
                    foreach (var mip in mipmaps) {
                        mip.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Обрабатывает текстуру для одного канала (загрузка, генерация мипов, специальная обработка)
        /// </summary>
        private async Task<List<Image<Rgba32>>> ProcessChannelTextureAsync(
            ChannelSourceSettings channelSettings,
            (int width, int height)? targetSize) {

            // Загружаем исходную текстуру
            using var sourceImage = await Image.LoadAsync<Rgba32>(channelSettings.SourcePath!);
            Logger.Info($"  Loaded source: {channelSettings.SourcePath}");
            Logger.Info($"  Source size: {sourceImage.Width}x{sourceImage.Height}");

            // Ресайзим если нужно
            Image<Rgba32> workingImage = sourceImage.Clone();
            if (targetSize.HasValue &&
                (sourceImage.Width != targetSize.Value.width || sourceImage.Height != targetSize.Value.height)) {
                Logger.Info($"  Resizing to {targetSize.Value.width}x{targetSize.Value.height}");
                workingImage.Mutate(ctx => ctx.Resize(targetSize.Value.width, targetSize.Value.height));
            }

            // Генерируем мипмапы
            var profile = channelSettings.MipProfile ?? MipGenerationProfile.CreateDefault(
                ConvertChannelTypeToTextureType(channelSettings.ChannelType)
            );

            Logger.Info($"  Generating mipmaps with filter: {profile.Filter}");
            var mipmaps = _mipGenerator.GenerateMipmaps(workingImage, profile);
            workingImage.Dispose(); // Освобождаем временное изображение

            // Применяем специальную обработку
            mipmaps = await ApplyChannelSpecificProcessingAsync(channelSettings, mipmaps);

            return mipmaps;
        }

        /// <summary>
        /// Применяет специфичную для канала обработку (Toksvig для Gloss, AO processing для AO и Metallic)
        /// </summary>
        private async Task<List<Image<Rgba32>>> ApplyChannelSpecificProcessingAsync(
            ChannelSourceSettings channelSettings,
            List<Image<Rgba32>> mipmaps) {

            switch (channelSettings.ChannelType) {
                case ChannelType.Gloss:
                    if (channelSettings.ApplyToksvig && channelSettings.ToksvigSettings != null) {
                        Logger.Info($"  Applying Toksvig correction to Gloss");
                        return await ApplyToksvigToGlossAsync(
                            mipmaps,
                            channelSettings.ToksvigSettings,
                            channelSettings.SourcePath
                        );
                    }
                    break;

                case ChannelType.AmbientOcclusion:
                    if (channelSettings.AOProcessingMode != AOProcessingMode.None) {
                        Logger.Info($"  Applying AO processing: {channelSettings.AOProcessingMode}");
                        var processedMipmaps = _aoProcessor.ProcessAOMipmaps(
                            mipmaps,
                            channelSettings.AOProcessingMode,
                            channelSettings.AOBias,
                            channelSettings.AOPercentile
                        );

                        // Освобождаем старые мипмапы
                        foreach (var mip in mipmaps) {
                            mip.Dispose();
                        }

                        return processedMipmaps;
                    }
                    break;

                case ChannelType.Metallic:
                    if (channelSettings.AOProcessingMode != AOProcessingMode.None) {
                        Logger.Info($"  Applying Metallic processing: {channelSettings.AOProcessingMode}");
                        var processedMipmaps = _aoProcessor.ProcessAOMipmaps(
                            mipmaps,
                            channelSettings.AOProcessingMode,
                            channelSettings.AOBias,
                            channelSettings.AOPercentile
                        );

                        // Освобождаем старые мипмапы
                        foreach (var mip in mipmaps) {
                            mip.Dispose();
                        }

                        return processedMipmaps;
                    }
                    break;
            }

            return mipmaps;
        }

        /// <summary>
        /// Применяет Toksvig коррекцию к Gloss каналу
        /// </summary>
        private async Task<List<Image<Rgba32>>> ApplyToksvigToGlossAsync(
            List<Image<Rgba32>> glossMipmaps,
            ToksvigSettings toksvigSettings,
            string? glossPath) {

            // Ищем normal map
            string? normalMapPath = toksvigSettings.NormalMapPath;
            if (string.IsNullOrEmpty(normalMapPath) && !string.IsNullOrEmpty(glossPath)) {
                var normalMapMatcher = new NormalMapMatcher();
                normalMapPath = normalMapMatcher.FindNormalMapAuto(glossPath, validateDimensions: true);
            }

            if (string.IsNullOrEmpty(normalMapPath)) {
                Logger.Warn("  Normal map not found, skipping Toksvig correction");
                return glossMipmaps;
            }

            Logger.Info($"  Using normal map: {normalMapPath}");

            using var normalMapImage = await Image.LoadAsync<Rgba32>(normalMapPath);

            // Применяем Toksvig (isGloss = true)
            var correctedMipmaps = _toksvigProcessor.ApplyToksvigCorrection(
                glossMipmaps,
                normalMapImage,
                toksvigSettings,
                isGloss: true
            );

            // Освобождаем старые мипмапы
            foreach (var mip in glossMipmaps) {
                mip.Dispose();
            }

            return correctedMipmaps;
        }


        /// <summary>
        /// Получает значение пикселя из мипмапа канала
        /// </summary>
        private byte GetChannelValue(
            List<Image<Rgba32>> mips,
            int level,
            int x,
            int y,
            int targetWidth,
            int targetHeight) {

            // Если нет мипмапов на этом уровне, используем последний доступный
            int actualLevel = Math.Min(level, mips.Count - 1);
            var mip = mips[actualLevel];

            // Масштабируем координаты если размер не совпадает
            int mipWidth = mip.Width;
            int mipHeight = mip.Height;

            if (mipWidth != targetWidth || mipHeight != targetHeight) {
                int scaledX = (int)((float)x * mipWidth / targetWidth);
                int scaledY = (int)((float)y * mipHeight / targetHeight);
                scaledX = Math.Clamp(scaledX, 0, mipWidth - 1);
                scaledY = Math.Clamp(scaledY, 0, mipHeight - 1);
                return mip[scaledX, scaledY].R;
            }

            return mip[x, y].R;
        }

        private Image<Rgba32> PackMipmapLevel(
            ChannelPackingSettings packingSettings,
            Dictionary<ChannelType, List<Image<Rgba32>>> channelMipmaps,
            int level,
            int width,
            int height) {

            var packed = new Image<Rgba32>(width, height);

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    byte r = 0, g = 0, b = 0, a = 255;

                    switch (packingSettings.Mode) {
                        case ChannelPackingMode.OG:
                            // RGB = AO, A = Gloss
                            if (channelMipmaps.TryGetValue(ChannelType.AmbientOcclusion, out var aoMips)) {
                                byte aoValue = GetChannelValue(aoMips, level, x, y, width, height);
                                r = g = b = aoValue;
                            }
                            if (channelMipmaps.TryGetValue(ChannelType.Gloss, out var glossMips)) {
                                a = GetChannelValue(glossMips, level, x, y, width, height);
                            }
                            break;

                        case ChannelPackingMode.OGM:
                        case ChannelPackingMode.OGMH:
                            // R = AO
                            if (channelMipmaps.TryGetValue(ChannelType.AmbientOcclusion, out var aoMips2)) {
                                r = GetChannelValue(aoMips2, level, x, y, width, height);
                            }
                            // G = Gloss
                            if (channelMipmaps.TryGetValue(ChannelType.Gloss, out var glossMips2)) {
                                g = GetChannelValue(glossMips2, level, x, y, width, height);
                            }
                            // B = Metallic
                            if (channelMipmaps.TryGetValue(ChannelType.Metallic, out var metallicMips)) {
                                b = GetChannelValue(metallicMips, level, x, y, width, height);
                            }
                            // A = Height (только для OGMH)
                            if (packingSettings.Mode == ChannelPackingMode.OGMH &&
                                channelMipmaps.TryGetValue(ChannelType.Height, out var heightMips)) {
                                a = GetChannelValue(heightMips, level, x, y, width, height);
                            }
                            break;
                    }

                    packed[x, y] = new Rgba32(r, g, b, a);
                }
            }

            return packed;
        }

        /// <summary>
        /// Конвертирует ChannelType в TextureType
        /// </summary>
        private TextureType ConvertChannelTypeToTextureType(ChannelType channelType) {
            return channelType switch {
                ChannelType.AmbientOcclusion => TextureType.AmbientOcclusion,
                ChannelType.Gloss => TextureType.Gloss,
                ChannelType.Metallic => TextureType.Metallic,
                ChannelType.Height => TextureType.Height,
                _ => TextureType.Generic
            };
        }

        /// <summary>
        /// Упаковывает каналы и сохраняет мипмапы как отдельные PNG файлы (для отладки)
        /// </summary>
        public async Task<List<string>> PackAndSaveAsync(
            ChannelPackingSettings packingSettings,
            string outputDirectory,
            string baseName) {

            var mipmaps = await PackChannelsAsync(packingSettings);

            Directory.CreateDirectory(outputDirectory);
            var savedPaths = new List<string>();

            for (int i = 0; i < mipmaps.Count; i++) {
                var outputPath = Path.Combine(outputDirectory, $"{baseName}_packed_mip{i}.png");
                await mipmaps[i].SaveAsPngAsync(outputPath);
                savedPaths.Add(outputPath);
                Logger.Info($"Saved packed mip{i}: {outputPath}");
            }

            // Освобождаем память
            foreach (var mip in mipmaps) {
                mip.Dispose();
            }

            return savedPaths;
        }
    }
}

using System.IO;
using AssetProcessor.TextureConversion.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Генератор мипмапов с поддержкой различных профилей
    /// </summary>
    public class MipGenerator {
        /// <summary>
        /// Генерирует цепочку мипмапов для изображения
        /// </summary>
        /// <param name="sourceImage">Исходное изображение</param>
        /// <param name="profile">Профиль генерации</param>
        /// <returns>Список мипмапов (включая оригинальное изображение на уровне 0)</returns>
        public List<Image<Rgba32>> GenerateMipmaps(Image<Rgba32> sourceImage, MipGenerationProfile profile) {
            var mipmaps = new List<Image<Rgba32>>();

            // Уровень 0 - оригинальное изображение (клон)
            mipmaps.Add(sourceImage.Clone());

            int currentWidth = sourceImage.Width;
            int currentHeight = sourceImage.Height;
            int mipLevel = 1;

            // Генерируем мипмапы до минимального размера
            while (currentWidth > profile.MinMipSize || currentHeight > profile.MinMipSize) {
                currentWidth = Math.Max(profile.MinMipSize, currentWidth / 2);
                currentHeight = Math.Max(profile.MinMipSize, currentHeight / 2);

                // Проверяем, нужно ли включать последний уровень
                if (!profile.IncludeLastLevel && currentWidth == profile.MinMipSize && currentHeight == profile.MinMipSize) {
                    break;
                }

                var previousMip = mipmaps[^1]; // Берем предыдущий мипмап
                var mipImage = GenerateSingleMipmap(previousMip, currentWidth, currentHeight, profile);

                // Применяем постобработку через модификаторы
                foreach (var modifier in profile.Modifiers) {
                    if (modifier.IsApplicable(profile.TextureType)) {
                        var modifiedImage = modifier.Apply(mipImage, mipLevel, sourceImage);
                        mipImage.Dispose();
                        mipImage = modifiedImage;
                    }
                }

                mipmaps.Add(mipImage);
                mipLevel++;
            }

            return mipmaps;
        }

        /// <summary>
        /// Генерирует один мипмап из исходного изображения
        /// </summary>
        private Image<Rgba32> GenerateSingleMipmap(Image<Rgba32> source, int targetWidth, int targetHeight, MipGenerationProfile profile) {
            var mipImage = source.Clone(ctx => {
                // Применяем гамма-коррекцию перед ресайзом (если нужно)
                if (profile.ApplyGammaCorrection && profile.Filter != FilterType.Min && profile.Filter != FilterType.Max) {
                    // Конвертируем в линейное пространство перед фильтрацией
                    ApplyGamma(ctx, 1.0f / profile.Gamma);
                }

                // Применяем дополнительный blur если указан
                if (profile.BlurRadius > 0 && profile.Filter != FilterType.Min && profile.Filter != FilterType.Max) {
                    ctx.GaussianBlur(profile.BlurRadius);
                }

                // Ресайзим с нужным фильтром
                var resampler = GetResampler(profile.Filter);
                ctx.Resize(targetWidth, targetHeight, resampler);

                // Для Min/Max фильтров применяем специальную обработку
                if (profile.Filter == FilterType.Min) {
                    ApplyMinFilter(ctx, source, targetWidth, targetHeight);
                } else if (profile.Filter == FilterType.Max) {
                    ApplyMaxFilter(ctx, source, targetWidth, targetHeight);
                }

                // Применяем обратную гамма-коррекцию после ресайза
                if (profile.ApplyGammaCorrection && profile.Filter != FilterType.Min && profile.Filter != FilterType.Max) {
                    ApplyGamma(ctx, profile.Gamma);
                }

                // Нормализуем нормали если это normal map
                if (profile.NormalizeNormals && profile.TextureType == TextureType.Normal) {
                    NormalizeNormalMap(ctx);
                }
            });

            return mipImage;
        }

        /// <summary>
        /// Генерирует мипмапы и сохраняет каждый уровень в отдельный файл
        /// </summary>
        public async Task GenerateAndSaveMipmapsAsync(string inputPath, string outputDirectory, MipGenerationProfile profile) {
            using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath);
            var mipmaps = GenerateMipmaps(sourceImage, profile);

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            Directory.CreateDirectory(outputDirectory);

            for (int i = 0; i < mipmaps.Count; i++) {
                var outputPath = Path.Combine(outputDirectory, $"{fileName}_mip{i}.png");
                await mipmaps[i].SaveAsPngAsync(outputPath);
            }

            // Освобождаем память
            foreach (var mip in mipmaps) {
                mip.Dispose();
            }
        }

        /// <summary>
        /// Получает IResampler на основе типа фильтра
        /// </summary>
        private IResampler GetResampler(FilterType filterType) {
            return filterType switch {
                FilterType.Box => KnownResamplers.Box,
                FilterType.Bilinear => KnownResamplers.Triangle,
                FilterType.Bicubic => KnownResamplers.Bicubic,
                FilterType.Lanczos3 => KnownResamplers.Lanczos3,
                FilterType.Mitchell => KnownResamplers.MitchellNetravali,
                FilterType.Kaiser => KnownResamplers.Lanczos3, // ImageSharp не имеет Kaiser, используем Lanczos3
                FilterType.Min => KnownResamplers.Box, // Min использует Box для начальной обработки
                FilterType.Max => KnownResamplers.Box, // Max использует Box для начальной обработки
                _ => KnownResamplers.Bicubic
            };
        }

        /// <summary>
        /// Применяет гамма-коррекцию к изображению
        /// </summary>
        private void ApplyGamma(IImageProcessingContext ctx, float gamma) {
            // ImageSharp не имеет встроенной гамма-коррекции, но мы можем использовать операцию с пикселями
            ctx.ProcessPixelRowsAsVector4((span) => {
                for (int i = 0; i < span.Length; i++) {
                    var pixel = span[i];
                    pixel.X = MathF.Pow(pixel.X, gamma);
                    pixel.Y = MathF.Pow(pixel.Y, gamma);
                    pixel.Z = MathF.Pow(pixel.Z, gamma);
                    // Alpha не трогаем
                    span[i] = pixel;
                }
            });
        }

        /// <summary>
        /// Нормализует карту нормалей
        /// </summary>
        private void NormalizeNormalMap(IImageProcessingContext ctx) {
            ctx.ProcessPixelRowsAsVector4((span) => {
                for (int i = 0; i < span.Length; i++) {
                    var pixel = span[i];

                    // Конвертируем из [0,1] в [-1,1]
                    float x = pixel.X * 2.0f - 1.0f;
                    float y = pixel.Y * 2.0f - 1.0f;
                    float z = pixel.Z * 2.0f - 1.0f;

                    // Нормализуем вектор
                    float length = MathF.Sqrt(x * x + y * y + z * z);
                    if (length > 0.0001f) {
                        x /= length;
                        y /= length;
                        z /= length;
                    }

                    // Конвертируем обратно в [0,1]
                    pixel.X = x * 0.5f + 0.5f;
                    pixel.Y = y * 0.5f + 0.5f;
                    pixel.Z = z * 0.5f + 0.5f;

                    span[i] = pixel;
                }
            });
        }

        /// <summary>
        /// Вычисляет количество уровней мипмапов для заданного размера
        /// </summary>
        public static int CalculateMipLevels(int width, int height, int minSize = 1) {
            int maxDimension = Math.Max(width, height);
            return (int)Math.Floor(Math.Log2(maxDimension / minSize)) + 1;
        }

        /// <summary>
        /// Применяет Min фильтр (выбирает минимальное значение в окрестности)
        /// Полезно для roughness/metallic карт для сохранения деталей
        /// </summary>
        private void ApplyMinFilter(IImageProcessingContext ctx, Image<Rgba32> source, int targetWidth, int targetHeight) {
            float scaleX = (float)source.Width / targetWidth;
            float scaleY = (float)source.Height / targetHeight;

            ctx.ProcessPixelRowsAsVector4((row, point) => {
                for (int x = 0; x < row.Length; x++) {
                    float srcX = (x + 0.5f) * scaleX;
                    float srcY = (point.Y + 0.5f) * scaleY;

                    int x0 = (int)(srcX - scaleX / 2);
                    int y0 = (int)(srcY - scaleY / 2);
                    int x1 = (int)(srcX + scaleX / 2);
                    int y1 = (int)(srcY + scaleY / 2);

                    x0 = Math.Max(0, Math.Min(source.Width - 1, x0));
                    y0 = Math.Max(0, Math.Min(source.Height - 1, y0));
                    x1 = Math.Max(0, Math.Min(source.Width - 1, x1));
                    y1 = Math.Max(0, Math.Min(source.Height - 1, y1));

                    var minPixel = new System.Numerics.Vector4(float.MaxValue);

                    for (int sy = y0; sy <= y1; sy++) {
                        for (int sx = x0; sx <= x1; sx++) {
                            var pixel = source[sx, sy].ToVector4();
                            minPixel.X = Math.Min(minPixel.X, pixel.X);
                            minPixel.Y = Math.Min(minPixel.Y, pixel.Y);
                            minPixel.Z = Math.Min(minPixel.Z, pixel.Z);
                            minPixel.W = Math.Min(minPixel.W, pixel.W);
                        }
                    }

                    row[x] = minPixel;
                }
            });
        }

        /// <summary>
        /// Применяет Max фильтр (выбирает максимальное значение в окрестности)
        /// Полезно для normal map и AO карт для сохранения четкости
        /// </summary>
        private void ApplyMaxFilter(IImageProcessingContext ctx, Image<Rgba32> source, int targetWidth, int targetHeight) {
            float scaleX = (float)source.Width / targetWidth;
            float scaleY = (float)source.Height / targetHeight;

            ctx.ProcessPixelRowsAsVector4((row, point) => {
                for (int x = 0; x < row.Length; x++) {
                    float srcX = (x + 0.5f) * scaleX;
                    float srcY = (point.Y + 0.5f) * scaleY;

                    int x0 = (int)(srcX - scaleX / 2);
                    int y0 = (int)(srcY - scaleY / 2);
                    int x1 = (int)(srcX + scaleX / 2);
                    int y1 = (int)(srcY + scaleY / 2);

                    x0 = Math.Max(0, Math.Min(source.Width - 1, x0));
                    y0 = Math.Max(0, Math.Min(source.Height - 1, y0));
                    x1 = Math.Max(0, Math.Min(source.Width - 1, x1));
                    y1 = Math.Max(0, Math.Min(source.Height - 1, y1));

                    var maxPixel = new System.Numerics.Vector4(float.MinValue);

                    for (int sy = y0; sy <= y1; sy++) {
                        for (int sx = x0; sx <= x1; sx++) {
                            var pixel = source[sx, sy].ToVector4();
                            maxPixel.X = Math.Max(maxPixel.X, pixel.X);
                            maxPixel.Y = Math.Max(maxPixel.Y, pixel.Y);
                            maxPixel.Z = Math.Max(maxPixel.Z, pixel.Z);
                            maxPixel.W = Math.Max(maxPixel.W, pixel.W);
                        }
                    }

                    row[x] = maxPixel;
                }
            });
        }
    }
}

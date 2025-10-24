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
                if (profile.ApplyGammaCorrection) {
                    // Конвертируем в линейное пространство перед фильтрацией
                    ApplyGamma(ctx, 1.0f / profile.Gamma);
                }

                // Применяем дополнительный blur если указан
                if (profile.BlurRadius > 0) {
                    ctx.GaussianBlur(profile.BlurRadius);
                }

                // Ресайзим с нужным фильтром
                var resampler = GetResampler(profile.Filter);
                ctx.Resize(targetWidth, targetHeight, resampler);

                // Применяем обратную гамма-коррекцию после ресайза
                if (profile.ApplyGammaCorrection) {
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
    }
}

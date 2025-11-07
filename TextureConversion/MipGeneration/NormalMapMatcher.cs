using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using NLog;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Утилита для автоматического поиска normal map и определения типа текстуры (gloss/roughness)
    /// </summary>
    public class NormalMapMatcher {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Автоматический поиск normal map по имени файла roughness/gloss текстуры
        /// </summary>
        /// <param name="roughnessGlossPath">Путь к roughness или gloss текстуре</param>
        /// <param name="validateDimensions">Проверять ли совпадение размеров</param>
        /// <returns>Путь к найденной normal map или null</returns>
        public string? FindNormalMapAuto(string roughnessGlossPath, bool validateDimensions) {
            if (string.IsNullOrEmpty(roughnessGlossPath) || !File.Exists(roughnessGlossPath)) {
                return null;
            }

            var directory = Path.GetDirectoryName(roughnessGlossPath);
            if (string.IsNullOrEmpty(directory)) {
                return null;
            }

            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(roughnessGlossPath);
            var extension = Path.GetExtension(roughnessGlossPath);

            // Список паттернов для поиска normal map
            var patterns = new[] {
                // Замена _roughness / _gloss на _normal
                fileNameWithoutExt.Replace("_roughness", "_normal"),
                fileNameWithoutExt.Replace("_gloss", "_normal"),
                fileNameWithoutExt.Replace("_Roughness", "_Normal"),
                fileNameWithoutExt.Replace("_Gloss", "_Normal"),

                // Добавление _normal в конец
                fileNameWithoutExt + "_normal",
                fileNameWithoutExt + "_Normal",

                // Замена суффиксов
                fileNameWithoutExt.Replace("_r", "_n"),
                fileNameWithoutExt.Replace("_g", "_n")
            };

            foreach (var pattern in patterns.Distinct()) {
                var candidatePath = Path.Combine(directory, pattern + extension);
                if (File.Exists(candidatePath)) {
                    // Опционально проверяем размеры
                    if (validateDimensions) {
                        try {
                            using var sourceImage = Image.Load(roughnessGlossPath);
                            using var normalImage = Image.Load(candidatePath);

                            if (sourceImage.Width == normalImage.Width && sourceImage.Height == normalImage.Height) {
                                Logger.Info($"Найдена normal map: {candidatePath}");
                                return candidatePath;
                            }
                        } catch {
                            // Игнорируем ошибки загрузки
                        }
                    } else {
                        Logger.Info($"Найдена normal map: {candidatePath}");
                        return candidatePath;
                    }
                }
            }

            Logger.Debug($"Normal map не найдена для: {roughnessGlossPath}");
            return null;
        }

        /// <summary>
        /// Определяет, является ли текстура gloss или roughness по имени файла
        /// </summary>
        /// <param name="path">Путь к файлу</param>
        /// <returns>true если gloss, false если roughness, null если не удалось определить</returns>
        public bool? IsGlossByName(string path) {
            if (string.IsNullOrEmpty(path)) {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            // Проверяем на gloss
            if (fileName.Contains("gloss") || fileName.Contains("_g_") || fileName.EndsWith("_g")) {
                return true;
            }

            // Проверяем на roughness
            if (fileName.Contains("roughness") || fileName.Contains("_r_") || fileName.EndsWith("_r")) {
                return false;
            }

            // Не удалось определить
            return null;
        }
    }

    /// <summary>
    /// Полная реализация Toksvig генератора с energy-preserving и поиском normal map
    /// УСТАРЕЛО: Этот класс заменён на ToksvigProcessor + MipGenerator с встроенной energy-preserving фильтрацией
    /// Оставлен для обратной совместимости
    /// </summary>
    [Obsolete("Используйте ToksvigProcessor + MipGenerator вместо этого класса")]
    public class ToksvigMipGenerator {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        // TODO: NormalMapMatcher не реализован, временно закомментировано
        // private readonly NormalMapMatcher normalMapMatcher;

        // Настройки генерации
        public class Settings {
            public float ToksvigStrength { get; set; } = 0.5f;
            public float MinRoughness { get; set; } = 0.01f;
            public float MaxRoughness { get; set; } = 1.0f;
            public bool UseEnergyPreserving { get; set; } = true;
            public bool UseAngleAwareCorrection { get; set; } = false;
            public int MaxMipLevels { get; set; } = -1; // -1 = auto
        }

        private readonly Settings settings;

        public ToksvigMipGenerator(Settings? settings = null) {
            // TODO: NormalMapMatcher не реализован
            // normalMapMatcher = new NormalMapMatcher();
            this.settings = settings ?? new Settings();
        }

        /// <summary>
        /// Основной метод генерации мипов
        /// </summary>
        public byte[] GenerateMips(string roughnessPath, bool isGlossMap = false) {
            Logger.Info($"Начало генерации мипов для: {roughnessPath}");
            Logger.Info($"Тип карты: {(isGlossMap ? "Gloss" : "Roughness")}");

            // TODO: NormalMapMatcher не реализован, всегда используем fallback
            Logger.Warn("NormalMapMatcher не реализован, используем базовый energy-preserving");
            return GenerateEnergyPreservingMips(roughnessPath, isGlossMap);

            // Автоматический поиск normal map
            // string? normalMapPath = normalMapMatcher.FindNormalMapAuto(roughnessPath, true);
            //
            // if (normalMapPath == null) {
            //     Logger.Warn("Normal map не найдена, используем базовый energy-preserving");
            //     return GenerateEnergyPreservingMips(roughnessPath, isGlossMap);
            // }
            //
            // Logger.Info($"Используем normal map: {normalMapPath}");
            // return GenerateToksvigMipsWithNormal(roughnessPath, normalMapPath, isGlossMap);
        }

        /// <summary>
        /// Генерация с учетом normal map
        /// </summary>
        private byte[] GenerateToksvigMipsWithNormal(
            string roughnessPath, 
            string normalMapPath,
            bool isGloss) {
            
            using var roughnessImage = Image.Load<Rgba32>(roughnessPath);
            using var normalImage = Image.Load<Rgba32>(normalMapPath);

            int width = roughnessImage.Width;
            int height = roughnessImage.Height;
            int mipCount = CalculateMipCount(width, height);
            
            Logger.Info($"Размер текстуры: {width}x{height}, уровней мипов: {mipCount}");

            var mipChain = new List<Image<Rgba32>>();
            mipChain.Add(roughnessImage.Clone());

            // Создаем копию normal map для модификации
            var currentNormalMap = normalImage.Clone();

            for (int level = 1; level < mipCount; level++) {
                int prevWidth = mipChain[level - 1].Width;
                int prevHeight = mipChain[level - 1].Height;
                int newWidth = Math.Max(1, prevWidth / 2);
                int newHeight = Math.Max(1, prevHeight / 2);

                Logger.Debug($"Генерация мип-уровня {level}: {newWidth}x{newHeight}");

                var newMip = new Image<Rgba32>(newWidth, newHeight);

                // Обрабатываем каждый пиксель нового мипа
                for (int y = 0; y < newHeight; y++) {
                    for (int x = 0; x < newWidth; x++) {
                        var pixel = ComputeToksvigPixel(
                            x, y,
                            mipChain[level - 1],
                            currentNormalMap,
                            level,
                            isGloss
                        );
                        newMip[x, y] = pixel;
                    }
                }

                mipChain.Add(newMip);

                // Уменьшаем normal map для следующего уровня
                currentNormalMap = DownsampleNormals(currentNormalMap);
            }

            // Конвертируем в байтовый массив для KTX
            return ConvertMipChainToBytes(mipChain);
        }

        /// <summary>
        /// Вычисление пикселя с Toksvig + Energy-Preserving
        /// </summary>
        private Rgba32 ComputeToksvigPixel(
            int x, int y,
            Image<Rgba32> prevLevel,
            Image<Rgba32> normals,
            int mipLevel,
            bool isGloss) {
            
            float alphaSum = 0;
            float normalVariance = 0;
            var avgNormal = new System.Numerics.Vector3(0);
            int sampleCount = 0;

            // Собираем 2x2 блок из предыдущего уровня
            for (int dy = 0; dy < 2; dy++) {
                for (int dx = 0; dx < 2; dx++) {
                    int sx = Math.Min(x * 2 + dx, prevLevel.Width - 1);
                    int sy = Math.Min(y * 2 + dy, prevLevel.Height - 1);

                    // Получаем roughness значение
                    var pixel = prevLevel[sx, sy];
                    float roughness = pixel.R / 255f;
                    
                    if (isGloss) {
                        roughness = 1.0f - roughness;
                    }

                    // Energy-preserving: работаем с alpha (roughness²)
                    if (settings.UseEnergyPreserving) {
                        float alpha = roughness * roughness;
                        alphaSum += alpha;
                    } else {
                        alphaSum += roughness;
                    }

                    // Получаем и накапливаем нормаль
                    if (sx < normals.Width && sy < normals.Height) {
                        var normalPixel = normals[sx, sy];
                        var normal = DecodeNormal(normalPixel);
                        avgNormal += normal;
                        sampleCount++;
                    }
                }
            }

            // Усредняем значения
            alphaSum /= 4.0f;
            
            if (sampleCount > 0) {
                avgNormal = System.Numerics.Vector3.Normalize(avgNormal);

                // Вычисляем variance нормалей
                for (int dy = 0; dy < 2; dy++) {
                    for (int dx = 0; dx < 2; dx++) {
                        int sx = Math.Min(x * 2 + dx, normals.Width - 1);
                        int sy = Math.Min(y * 2 + dy, normals.Height - 1);

                        if (sx < normals.Width && sy < normals.Height) {
                            var normalPixel = normals[sx, sy];
                            var normal = DecodeNormal(normalPixel);
                            float dot = System.Numerics.Vector3.Dot(normal, avgNormal);
                            normalVariance += (1.0f - Math.Max(0, dot));
                        }
                    }
                }
                normalVariance /= sampleCount;
            }

            // Применяем Toksvig коррекцию
            float toksvigFactor = GetAdaptiveToksvigFactor(mipLevel, normalVariance);
            float correctedAlpha = alphaSum + normalVariance * toksvigFactor * settings.ToksvigStrength;

            // Возвращаем к roughness
            float finalRoughness;
            if (settings.UseEnergyPreserving) {
                finalRoughness = (float)Math.Sqrt(correctedAlpha);
            } else {
                finalRoughness = correctedAlpha;
            }

            // Ограничиваем диапазон
            finalRoughness = Math.Max(settings.MinRoughness, Math.Min(settings.MaxRoughness, finalRoughness));

            // Конвертируем обратно в gloss если нужно
            if (isGloss) {
                finalRoughness = 1.0f - finalRoughness;
            }

            byte value = (byte)(finalRoughness * 255);
            return new Rgba32(value, value, value, 255);
        }

        /// <summary>
        /// Адаптивный коэффициент Toksvig
        /// </summary>
        private float GetAdaptiveToksvigFactor(int mipLevel, float variance) {
            // Уменьшаем влияние на высоких мипах
            float mipAttenuation = 1.0f / (1.0f + mipLevel * 0.2f);
            
            // Усиливаем эффект при высокой variance
            float varianceBoost = 1.0f + variance * 0.5f;
            
            return mipAttenuation * varianceBoost;
        }

        /// <summary>
        /// Декодирование нормали из RGB
        /// </summary>
        private System.Numerics.Vector3 DecodeNormal(Rgba32 pixel) {
            float x = (pixel.R / 255f) * 2.0f - 1.0f;
            float y = (pixel.G / 255f) * 2.0f - 1.0f;
            
            // Восстанавливаем Z компоненту
            float z = (float)Math.Sqrt(Math.Max(0, 1.0f - x * x - y * y));
            
            return new System.Numerics.Vector3(x, y, z);
        }

        /// <summary>
        /// Правильный downsample нормалей с перенормализацией
        /// </summary>
        private Image<Rgba32> DownsampleNormals(Image<Rgba32> normals) {
            int newWidth = Math.Max(1, normals.Width / 2);
            int newHeight = Math.Max(1, normals.Height / 2);
            var result = new Image<Rgba32>(newWidth, newHeight);

            for (int y = 0; y < newHeight; y++) {
                for (int x = 0; x < newWidth; x++) {
                    var avgNormal = new System.Numerics.Vector3(0);

                    // Усредняем 2x2 блок
                    for (int dy = 0; dy < 2; dy++) {
                        for (int dx = 0; dx < 2; dx++) {
                            int sx = Math.Min(x * 2 + dx, normals.Width - 1);
                            int sy = Math.Min(y * 2 + dy, normals.Height - 1);
                            
                            var pixel = normals[sx, sy];
                            avgNormal += DecodeNormal(pixel);
                        }
                    }

                    // Нормализуем
                    avgNormal = System.Numerics.Vector3.Normalize(avgNormal / 4.0f);

                    // Кодируем обратно в RGB
                    byte r = (byte)((avgNormal.X * 0.5f + 0.5f) * 255);
                    byte g = (byte)((avgNormal.Y * 0.5f + 0.5f) * 255);
                    byte b = (byte)((avgNormal.Z * 0.5f + 0.5f) * 255);

                    result[x, y] = new Rgba32(r, g, b, 255);
                }
            }

            return result;
        }

        /// <summary>
        /// Energy-preserving без normal map (fallback)
        /// </summary>
        private byte[] GenerateEnergyPreservingMips(string roughnessPath, bool isGloss) {
            using var image = Image.Load<Rgba32>(roughnessPath);
            
            int width = image.Width;
            int height = image.Height;
            int mipCount = CalculateMipCount(width, height);

            Logger.Info($"Energy-preserving генерация: {width}x{height}, мипов: {mipCount}");

            var mipChain = new List<Image<Rgba32>>();
            mipChain.Add(image.Clone());

            for (int level = 1; level < mipCount; level++) {
                int prevWidth = mipChain[level - 1].Width;
                int prevHeight = mipChain[level - 1].Height;
                int newWidth = Math.Max(1, prevWidth / 2);
                int newHeight = Math.Max(1, prevHeight / 2);

                var newMip = new Image<Rgba32>(newWidth, newHeight);

                for (int y = 0; y < newHeight; y++) {
                    for (int x = 0; x < newWidth; x++) {
                        float alphaSum = 0;

                        // Собираем 2x2 блок
                        for (int dy = 0; dy < 2; dy++) {
                            for (int dx = 0; dx < 2; dx++) {
                                int sx = Math.Min(x * 2 + dx, prevWidth - 1);
                                int sy = Math.Min(y * 2 + dy, prevHeight - 1);

                                float roughness = mipChain[level - 1][sx, sy].R / 255f;
                                if (isGloss) roughness = 1.0f - roughness;

                                float alpha = roughness * roughness;
                                alphaSum += alpha;
                            }
                        }

                        alphaSum /= 4.0f;
                        float finalRoughness = (float)Math.Sqrt(alphaSum);
                        
                        if (isGloss) finalRoughness = 1.0f - finalRoughness;

                        byte value = (byte)(finalRoughness * 255);
                        newMip[x, y] = new Rgba32(value, value, value, 255);
                    }
                }

                mipChain.Add(newMip);
            }

            return ConvertMipChainToBytes(mipChain);
        }

        /// <summary>
        /// Расчет количества мип-уровней
        /// </summary>
        private int CalculateMipCount(int width, int height) {
            int count = (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
            
            if (settings.MaxMipLevels > 0) {
                count = Math.Min(count, settings.MaxMipLevels);
            }
            
            return count;
        }

        /// <summary>
        /// Конвертация мип-цепочки в байтовый массив
        /// </summary>
        private byte[] ConvertMipChainToBytes(List<Image<Rgba32>> mipChain) {
            using var ms = new MemoryStream();
            
            // Записываем количество уровней
            ms.Write(BitConverter.GetBytes(mipChain.Count), 0, 4);

            foreach (var mip in mipChain) {
                // Записываем размеры
                ms.Write(BitConverter.GetBytes(mip.Width), 0, 4);
                ms.Write(BitConverter.GetBytes(mip.Height), 0, 4);

                // Записываем данные
                byte[] pixelData = new byte[mip.Width * mip.Height * 4];
                mip.CopyPixelDataTo(pixelData);
                ms.Write(pixelData, 0, pixelData.Length);
            }

            // Освобождаем память
            foreach (var mip in mipChain) {
                mip.Dispose();
            }

            return ms.ToArray();
        }
    }
}
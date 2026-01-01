using System;
using System.IO;
using System.Linq;
using NLog;
using SixLabors.ImageSharp;

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

                // CRITICAL: Skip if candidate is the same as input (avoid using gloss as normal map!)
                if (string.Equals(candidatePath, roughnessGlossPath, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (File.Exists(candidatePath)) {
                    // Опционально проверяем размеры
                    if (validateDimensions) {
                        try {
                            using var sourceImage = Image.Load(roughnessGlossPath);
                            using var normalImage = Image.Load(candidatePath);

                            if (sourceImage.Width == normalImage.Width && sourceImage.Height == normalImage.Height) {
                                Logger.Info($"Найдена normal map: {candidatePath}");
                                return candidatePath;
                            } else {
                                Logger.Debug($"Normal map candidate {candidatePath} has different dimensions: {normalImage.Width}x{normalImage.Height} vs {sourceImage.Width}x{sourceImage.Height}");
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
}
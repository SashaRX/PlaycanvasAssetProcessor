using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Утилита для поиска соответствующей normal map для gloss/roughness текстуры
    /// </summary>
    public class NormalMapMatcher {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Суффиксы для normal map
        private static readonly string[] NormalSuffixes = new[] {
            "_n", "_nor", "_normal", "_nrm",
            "_Normal", "_N", "_NRM"
        };

        // Суффиксы для gloss/roughness
        private static readonly string[] GlossRoughnessSuffixes = new[] {
            "_gloss", "_glossiness", "_g",
            "_rough", "_roughness", "_r",
            "_Gloss", "_Roughness"
        };

        /// <summary>
        /// Ищет normal map по имени файла gloss/roughness текстуры
        /// </summary>
        /// <param name="glossRoughnessPath">Путь к gloss/roughness текстуре</param>
        /// <returns>Путь к найденной normal map или null</returns>
        public string? FindNormalMapByName(string glossRoughnessPath) {
            if (string.IsNullOrEmpty(glossRoughnessPath) || !File.Exists(glossRoughnessPath)) {
                Logger.Warn($"Файл gloss/roughness не существует: {glossRoughnessPath}");
                return null;
            }

            var directory = Path.GetDirectoryName(glossRoughnessPath);
            if (string.IsNullOrEmpty(directory)) {
                Logger.Warn("Не удалось получить директорию файла");
                return null;
            }

            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(glossRoughnessPath);
            var extension = Path.GetExtension(glossRoughnessPath);

            // Попробуем удалить известные суффиксы gloss/roughness
            var baseName = RemoveKnownSuffixes(fileNameWithoutExt, GlossRoughnessSuffixes);

            Logger.Info($"Ищем normal map для: {glossRoughnessPath}");
            Logger.Info($"Базовое имя: {baseName}");

            // Ищем файлы с normal суффиксами
            var foundPaths = new List<string>();

            foreach (var normalSuffix in NormalSuffixes) {
                var normalFileName = baseName + normalSuffix + extension;
                var normalPath = Path.Combine(directory, normalFileName);

                if (File.Exists(normalPath)) {
                    Logger.Info($"Найдена normal map: {normalPath}");
                    foundPaths.Add(normalPath);
                }
            }

            // Также попробуем искать с другими расширениями
            var commonExtensions = new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };
            foreach (var normalSuffix in NormalSuffixes) {
                foreach (var ext in commonExtensions) {
                    if (ext == extension) continue; // Уже проверили

                    var normalFileName = baseName + normalSuffix + ext;
                    var normalPath = Path.Combine(directory, normalFileName);

                    if (File.Exists(normalPath) && !foundPaths.Contains(normalPath)) {
                        Logger.Info($"Найдена normal map с другим расширением: {normalPath}");
                        foundPaths.Add(normalPath);
                    }
                }
            }

            if (foundPaths.Count == 0) {
                Logger.Warn($"Normal map не найдена для: {glossRoughnessPath}");
                return null;
            }

            if (foundPaths.Count > 1) {
                Logger.Warn($"Найдено несколько normal map: {string.Join(", ", foundPaths)}. Используем первую.");
            }

            return foundPaths[0];
        }

        /// <summary>
        /// Ищет normal map по материалу (заглушка для будущей интеграции с PlayCanvas API)
        /// </summary>
        /// <param name="glossRoughnessTextureId">ID gloss/roughness текстуры в PlayCanvas</param>
        /// <param name="materials">Список материалов проекта</param>
        /// <returns>ID или путь к normal map</returns>
        public string? FindNormalMapByMaterial(string glossRoughnessTextureId, object? materials = null) {
            // TODO: Реализовать поиск через материалы PlayCanvas
            // Логика:
            // 1. Найти материалы, где glossRoughnessTextureId используется как glossMap/roughnessMap
            // 2. Взять normalMap из того же материала
            // 3. Вернуть путь к normal map

            Logger.Info($"Поиск normal map по материалу для текстуры ID: {glossRoughnessTextureId}");
            Logger.Warn("Поиск по материалу пока не реализован, используйте FindNormalMapByName");

            return null;
        }

        /// <summary>
        /// Проверяет, подходит ли размер normal map к gloss/roughness
        /// </summary>
        /// <param name="glossRoughnessPath">Путь к gloss/roughness</param>
        /// <param name="normalMapPath">Путь к normal map</param>
        /// <returns>true если размеры совпадают</returns>
        public bool ValidateDimensions(string glossRoughnessPath, string normalMapPath) {
            try {
                using var glossImage = SixLabors.ImageSharp.Image.Load(glossRoughnessPath);
                using var normalImage = SixLabors.ImageSharp.Image.Load(normalMapPath);

                bool match = glossImage.Width == normalImage.Width &&
                            glossImage.Height == normalImage.Height;

                if (!match) {
                    Logger.Warn($"Размеры не совпадают: " +
                               $"gloss/roughness={glossImage.Width}x{glossImage.Height}, " +
                               $"normal={normalImage.Width}x{normalImage.Height}");
                } else {
                    Logger.Info($"Размеры совпадают: {glossImage.Width}x{glossImage.Height}");
                }

                return match;
            } catch (Exception ex) {
                Logger.Error(ex, "Ошибка при проверке размеров текстур");
                return false;
            }
        }

        /// <summary>
        /// Автоматический поиск normal map с проверкой размеров
        /// </summary>
        /// <param name="glossRoughnessPath">Путь к gloss/roughness текстуре</param>
        /// <param name="validateDimensions">Проверять ли совпадение размеров</param>
        /// <returns>Путь к подходящей normal map или null</returns>
        public string? FindNormalMapAuto(string glossRoughnessPath, bool validateDimensions = true) {
            Logger.Info($"Автоматический поиск normal map для: {glossRoughnessPath}");

            // Сначала ищем по имени файла
            var normalPath = FindNormalMapByName(glossRoughnessPath);

            if (normalPath == null) {
                Logger.Info("Normal map не найдена по имени файла");
                return null;
            }

            // Проверяем размеры если требуется
            if (validateDimensions) {
                if (!ValidateDimensions(glossRoughnessPath, normalPath)) {
                    Logger.Warn("Размеры не совпадают, пропускаем эту normal map");
                    return null;
                }
            }

            Logger.Info($"Найдена подходящая normal map: {normalPath}");
            return normalPath;
        }

        /// <summary>
        /// Удаляет известные суффиксы из имени файла
        /// </summary>
        private string RemoveKnownSuffixes(string fileName, string[] suffixes) {
            foreach (var suffix in suffixes) {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                    return fileName.Substring(0, fileName.Length - suffix.Length);
                }
            }

            return fileName;
        }

        /// <summary>
        /// Определяет, является ли текстура gloss или roughness по имени
        /// </summary>
        /// <param name="texturePath">Путь к текстуре</param>
        /// <returns>true если gloss, false если roughness, null если не определено</returns>
        public bool? IsGlossByName(string texturePath) {
            var fileNameLower = Path.GetFileNameWithoutExtension(texturePath).ToLower();

            if (fileNameLower.Contains("gloss")) {
                return true;
            }

            if (fileNameLower.Contains("rough")) {
                return false;
            }

            Logger.Warn($"Не удалось определить тип текстуры (gloss/roughness) по имени: {texturePath}");
            return null;
        }
    }
}

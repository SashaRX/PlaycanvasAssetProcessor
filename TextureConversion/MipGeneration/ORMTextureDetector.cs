using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetProcessor.TextureConversion.Core;
using NLog;
using SixLabors.ImageSharp;

namespace AssetProcessor.TextureConversion.MipGeneration {
    /// <summary>
    /// Утилита для автоматического поиска исходных текстур для ORM упаковки
    /// </summary>
    public class ORMTextureDetector {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Суффиксы для поиска текстур
        private static readonly string[] AOSuffixes = { "_ao", "_AO", "_ambientocclusion", "_AmbientOcclusion", "_occlusion", "_Occlusion" };
        private static readonly string[] GlossSuffixes = { "_gloss", "_Gloss", "_glossiness", "_Glossiness", "_smoothness", "_Smoothness" };
        private static readonly string[] MetallicSuffixes = { "_metallic", "_Metallic", "_metalness", "_Metalness", "_metal", "_Metal" };
        private static readonly string[] HeightSuffixes = { "_height", "_Height", "_displacement", "_Displacement", "_disp", "_Disp" };

        /// <summary>
        /// Результат поиска текстур
        /// </summary>
        public class DetectionResult {
            public string? AOPath { get; set; }
            public string? GlossPath { get; set; }
            public string? MetallicPath { get; set; }
            public string? HeightPath { get; set; }

            public bool HasAO => !string.IsNullOrEmpty(AOPath);
            public bool HasGloss => !string.IsNullOrEmpty(GlossPath);
            public bool HasMetallic => !string.IsNullOrEmpty(MetallicPath);
            public bool HasHeight => !string.IsNullOrEmpty(HeightPath);

            public int FoundCount => (HasAO ? 1 : 0) + (HasGloss ? 1 : 0) + (HasMetallic ? 1 : 0) + (HasHeight ? 1 : 0);

            /// <summary>
            /// Определяет оптимальный режим упаковки на основе найденных текстур
            /// </summary>
            public ChannelPackingMode GetRecommendedPackingMode() {
                if (HasAO && HasGloss && HasMetallic && HasHeight) {
                    return ChannelPackingMode.OGMH;
                } else if (HasAO && HasGloss && HasMetallic) {
                    return ChannelPackingMode.OGM;
                } else if (HasAO && HasGloss) {
                    return ChannelPackingMode.OG;
                } else {
                    return ChannelPackingMode.None;
                }
            }

            public override string ToString() {
                var parts = new List<string>();
                if (HasAO) parts.Add($"AO={Path.GetFileName(AOPath)}");
                if (HasGloss) parts.Add($"Gloss={Path.GetFileName(GlossPath)}");
                if (HasMetallic) parts.Add($"Metallic={Path.GetFileName(MetallicPath)}");
                if (HasHeight) parts.Add($"Height={Path.GetFileName(HeightPath)}");
                return parts.Count > 0 ? string.Join(", ", parts) : "No textures found";
            }
        }

        /// <summary>
        /// Автоматический поиск всех ORM текстур по базовому пути
        /// </summary>
        /// <param name="basePath">Базовый путь (например, путь к albedo или любой текстуре материала)</param>
        /// <param name="validateDimensions">Проверять ли совпадение размеров</param>
        /// <returns>Результат поиска</returns>
        public DetectionResult DetectORMTextures(string basePath, bool validateDimensions = true) {
            if (string.IsNullOrEmpty(basePath) || !File.Exists(basePath)) {
                Logger.Warn($"Base path not found: {basePath}");
                return new DetectionResult();
            }

            Logger.Info($"Detecting ORM textures for: {basePath}");

            var result = new DetectionResult {
                AOPath = FindTexture(basePath, AOSuffixes, validateDimensions),
                GlossPath = FindTexture(basePath, GlossSuffixes, validateDimensions),
                MetallicPath = FindTexture(basePath, MetallicSuffixes, validateDimensions),
                HeightPath = FindTexture(basePath, HeightSuffixes, validateDimensions)
            };

            Logger.Info($"Detection result: {result}");
            Logger.Info($"Recommended mode: {result.GetRecommendedPackingMode()}");

            return result;
        }

        /// <summary>
        /// Поиск конкретной текстуры по списку суффиксов
        /// </summary>
        private string? FindTexture(string basePath, string[] suffixes, bool validateDimensions) {
            var directory = Path.GetDirectoryName(basePath);
            if (string.IsNullOrEmpty(directory)) {
                return null;
            }

            var baseFileNameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
            var extension = Path.GetExtension(basePath);

            // Удаляем известные суффиксы из базового имени для поиска
            var cleanBaseName = RemoveKnownSuffixes(baseFileNameWithoutExt);

            // Список всех возможных паттернов
            var patterns = new List<string>();

            foreach (var suffix in suffixes) {
                // 1. Заменяем существующий суффикс на новый
                patterns.Add(baseFileNameWithoutExt.Replace("_albedo", suffix));
                patterns.Add(baseFileNameWithoutExt.Replace("_Albedo", suffix));
                patterns.Add(baseFileNameWithoutExt.Replace("_diffuse", suffix));
                patterns.Add(baseFileNameWithoutExt.Replace("_Diffuse", suffix));
                patterns.Add(baseFileNameWithoutExt.Replace("_color", suffix));
                patterns.Add(baseFileNameWithoutExt.Replace("_Color", suffix));

                // 2. Добавляем суффикс к чистому базовому имени
                patterns.Add(cleanBaseName + suffix);

                // 3. Прямая замена (если базовое имя уже содержит другой суффикс)
                patterns.Add(baseFileNameWithoutExt.Replace("_normal", suffix));
                patterns.Add(baseFileNameWithoutExt.Replace("_roughness", suffix));
            }

            // Удаляем дубликаты
            patterns = patterns.Distinct().ToList();

            foreach (var pattern in patterns) {
                var candidatePath = Path.Combine(directory, pattern + extension);
                if (File.Exists(candidatePath)) {
                    // Опционально проверяем размеры
                    if (validateDimensions) {
                        try {
                            using var sourceImage = Image.Load(basePath);
                            using var candidateImage = Image.Load(candidatePath);

                            if (sourceImage.Width == candidateImage.Width &&
                                sourceImage.Height == candidateImage.Height) {
                                Logger.Debug($"Found matching texture: {candidatePath}");
                                return candidatePath;
                            } else {
                                Logger.Debug($"Texture found but dimensions don't match: {candidatePath} " +
                                           $"({candidateImage.Width}x{candidateImage.Height} vs {sourceImage.Width}x{sourceImage.Height})");
                            }
                        } catch (Exception ex) {
                            Logger.Warn($"Failed to validate texture: {candidatePath} - {ex.Message}");
                        }
                    } else {
                        Logger.Debug($"Found texture (no validation): {candidatePath}");
                        return candidatePath;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Удаляет известные суффиксы текстур из имени файла
        /// </summary>
        private string RemoveKnownSuffixes(string fileName) {
            var knownSuffixes = new[] {
                "_albedo", "_Albedo",
                "_diffuse", "_Diffuse",
                "_color", "_Color",
                "_normal", "_Normal",
                "_roughness", "_Roughness",
                "_ao", "_AO",
                "_gloss", "_Gloss",
                "_metallic", "_Metallic",
                "_height", "_Height"
            };

            foreach (var suffix in knownSuffixes) {
                if (fileName.EndsWith(suffix, StringComparison.Ordinal)) {
                    return fileName.Substring(0, fileName.Length - suffix.Length);
                }
            }

            return fileName;
        }

        /// <summary>
        /// Поиск конкретной текстуры по типу канала
        /// </summary>
        public string? FindTextureByType(string basePath, ChannelType channelType, bool validateDimensions = true) {
            var suffixes = channelType switch {
                ChannelType.AmbientOcclusion => AOSuffixes,
                ChannelType.Gloss => GlossSuffixes,
                ChannelType.Metallic => MetallicSuffixes,
                ChannelType.Height => HeightSuffixes,
                _ => throw new ArgumentException($"Unknown channel type: {channelType}")
            };

            return FindTexture(basePath, suffixes, validateDimensions);
        }

        /// <summary>
        /// Создает ChannelPackingSettings на основе автоматического поиска
        /// </summary>
        public ChannelPackingSettings? CreateSettingsFromDetection(string basePath, bool validateDimensions = true) {
            var detection = DetectORMTextures(basePath, validateDimensions);

            if (detection.FoundCount == 0) {
                Logger.Warn("No ORM textures found, cannot create packing settings");
                return null;
            }

            var mode = detection.GetRecommendedPackingMode();
            if (mode == ChannelPackingMode.None) {
                Logger.Warn("Not enough textures for ORM packing");
                return null;
            }

            var settings = ChannelPackingSettings.CreateDefault(mode);

            // Устанавливаем пути к найденным текстурам
            switch (mode) {
                case ChannelPackingMode.OG:
                    settings.RedChannel!.SourcePath = detection.AOPath;
                    settings.AlphaChannel!.SourcePath = detection.GlossPath;
                    break;

                case ChannelPackingMode.OGM:
                    settings.RedChannel!.SourcePath = detection.AOPath;
                    settings.GreenChannel!.SourcePath = detection.GlossPath;
                    settings.BlueChannel!.SourcePath = detection.MetallicPath;
                    break;

                case ChannelPackingMode.OGMH:
                    settings.RedChannel!.SourcePath = detection.AOPath;
                    settings.GreenChannel!.SourcePath = detection.GlossPath;
                    settings.BlueChannel!.SourcePath = detection.MetallicPath;
                    settings.AlphaChannel!.SourcePath = detection.HeightPath;
                    break;
            }

            Logger.Info($"Created packing settings for mode: {mode}");
            return settings;
        }
    }
}

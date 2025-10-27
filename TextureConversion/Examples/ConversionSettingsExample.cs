using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using NLog;

namespace AssetProcessor.TextureConversion.Examples {
    /// <summary>
    /// Примеры использования системы настроек конвертации
    /// </summary>
    public static class ConversionSettingsExample {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Пример 1: Базовое использование с пресетом
        /// </summary>
        public static void Example1_BasicPresetUsage() {
            Logger.Info("=== Example 1: Basic Preset Usage ===");

            // Загружаем глобальные настройки
            var globalSettings = TextureConversionSettingsManager.LoadSettings();

            // Создаем менеджер настроек
            var settingsManager = new ConversionSettingsManager(globalSettings);

            // Применяем пресет для Normal Map
            settingsManager.ApplyPresetByName("Normal (Linear)");

            // Генерируем CLI аргументы
            var outputPath = "output/normal_map.ktx2";
            var inputPaths = new List<string> {
                "temp/mip0.png",
                "temp/mip1.png",
                "temp/mip2.png"
            };

            var args = settingsManager.GenerateToktxArguments(outputPath, inputPaths);

            Logger.Info($"Generated {args.Count} CLI arguments");
            Logger.Info($"Command: toktx {string.Join(" ", args)}");
        }

        /// <summary>
        /// Пример 2: Кастомные настройки для Gloss с Toksvig
        /// </summary>
        public static void Example2_CustomGlossWithToksvig() {
            Logger.Info("=== Example 2: Custom Gloss with Toksvig ===");

            var globalSettings = TextureConversionSettingsManager.LoadSettings();
            var settingsManager = new ConversionSettingsManager(globalSettings);

            // Применяем базовый пресет
            settingsManager.ApplyPresetByName("AO/Gloss/Roughness (Linear + Toksvig)");

            // Настраиваем параметры Toksvig
            settingsManager.SetValue("enableToksvig", true);
            settingsManager.SetValue("compositePower", 1.5);
            settingsManager.SetValue("toksvigNormalMapPath", "textures/normalMap.png");

            // Настраиваем качество компрессии
            settingsManager.SetValue("compressionFormat", "etc1s");
            settingsManager.SetValue("qualityLevel", 192);

            // Получаем внутренние настройки для препроцессинга
            var internalSettings = settingsManager.GetInternalSettings();

            Logger.Info($"Toksvig enabled: {internalSettings.EnableToksvig}");
            Logger.Info($"Composite power: {internalSettings.CompositePower}");
            Logger.Info($"Normal map path: {internalSettings.ToksvigNormalMapPath}");

            // Здесь применили бы Toksvig коррекцию к мипмапам...
        }

        /// <summary>
        /// Пример 3: Высококачественная UASTC конвертация
        /// </summary>
        public static void Example3_HighQualityUASTC() {
            Logger.Info("=== Example 3: High Quality UASTC ===");

            var globalSettings = TextureConversionSettingsManager.LoadSettings();
            var settingsManager = new ConversionSettingsManager(globalSettings);

            // Настраиваем UASTC с максимальным качеством
            settingsManager.SetValue("compressionFormat", "uastc");
            settingsManager.SetValue("uastcQuality", 4); // Максимальное качество
            settingsManager.SetValue("useRDO", true);
            settingsManager.SetValue("rdoLambda", 0.5); // Малый lambda = больше качество

            // Supercompression для меньшего размера
            settingsManager.SetValue("supercompression", 15);

            // Threads
            settingsManager.SetValue("threads", "8");

            var outputPath = "output/high_quality.ktx2";
            var inputPaths = new List<string> { "temp/mip0.png" };

            var commandLine = settingsManager.GenerateToktxCommandLine("toktx", outputPath, inputPaths);
            Logger.Info($"Full command: {commandLine}");
        }

        /// <summary>
        /// Пример 4: Работа с условной видимостью параметров
        /// </summary>
        public static void Example4_ConditionalVisibility() {
            Logger.Info("=== Example 4: Conditional Visibility ===");

            var globalSettings = TextureConversionSettingsManager.LoadSettings();
            var settingsManager = new ConversionSettingsManager(globalSettings);

            // Получаем параметр compressionLevel
            var compressionLevelParam = settingsManager.GetParameter("compressionLevel");
            if (compressionLevelParam != null) {
                // Проверяем видимость при ETC1S
                settingsManager.SetValue("compressionFormat", "etc1s");
                bool visibleETC1S = settingsManager.IsParameterVisible(compressionLevelParam);
                Logger.Info($"compressionLevel visible with ETC1S: {visibleETC1S}");

                // Проверяем видимость при UASTC
                settingsManager.SetValue("compressionFormat", "uastc");
                bool visibleUASTC = settingsManager.IsParameterVisible(compressionLevelParam);
                Logger.Info($"compressionLevel visible with UASTC: {visibleUASTC}");
            }

            // Проверяем видимость параметров Toksvig
            var toksvigParams = new[] {
                "smoothVariance",
                "compositePower",
                "toksvigMinMipLevel",
                "toksvigNormalMapPath"
            };

            settingsManager.SetValue("enableToksvig", false);
            Logger.Info("Toksvig disabled:");
            foreach (var paramId in toksvigParams) {
                var param = settingsManager.GetParameter(paramId);
                if (param != null) {
                    bool visible = settingsManager.IsParameterVisible(param);
                    Logger.Info($"  {paramId}: {visible}");
                }
            }

            settingsManager.SetValue("enableToksvig", true);
            Logger.Info("Toksvig enabled:");
            foreach (var paramId in toksvigParams) {
                var param = settingsManager.GetParameter(paramId);
                if (param != null) {
                    bool visible = settingsManager.IsParameterVisible(param);
                    Logger.Info($"  {paramId}: {visible}");
                }
            }
        }

        /// <summary>
        /// Пример 5: Экспорт и импорт настроек
        /// </summary>
        public static void Example5_ExportImportSettings() {
            Logger.Info("=== Example 5: Export/Import Settings ===");

            var globalSettings = TextureConversionSettingsManager.LoadSettings();
            var settingsManager1 = new ConversionSettingsManager(globalSettings);

            // Настраиваем параметры
            settingsManager1.ApplyPresetByName("Albedo/Color (sRGB)");
            settingsManager1.SetValue("qualityLevel", 200);
            settingsManager1.SetValue("supercompression", 12);

            // Экспортируем настройки
            var exportedSettings = settingsManager1.ExportSettings();
            Logger.Info($"Exported {exportedSettings.Count} settings");

            // Создаем новый менеджер и импортируем настройки
            var settingsManager2 = new ConversionSettingsManager(globalSettings);
            settingsManager2.ImportSettings(exportedSettings);

            // Проверяем, что настройки совпадают
            var quality1 = settingsManager1.GetValue("qualityLevel");
            var quality2 = settingsManager2.GetValue("qualityLevel");
            Logger.Info($"Quality level match: {quality1} == {quality2}");
        }

        /// <summary>
        /// Пример 6: Получение всех групп параметров для UI
        /// </summary>
        public static void Example6_ParameterGroupsForUI() {
            Logger.Info("=== Example 6: Parameter Groups for UI ===");

            var groups = ConversionSettingsSchema.GetAllParameterGroups();

            Logger.Info($"Total sections: {groups.Count}");

            foreach (var group in groups.OrderBy(g => g.Order)) {
                Logger.Info($"\nSection: {group.DisplayName}");
                Logger.Info($"  Description: {group.Description}");
                Logger.Info($"  Parameters: {group.Parameters.Count}");

                foreach (var param in group.Parameters) {
                    var internalTag = param.IsInternal ? " [INTERNAL]" : "";
                    var cliTag = !string.IsNullOrEmpty(param.CliFlag) ? $" → {param.CliFlag}" : "";
                    Logger.Info($"    - {param.DisplayName} ({param.UIType}){internalTag}{cliTag}");

                    if (param.Visibility != null) {
                        Logger.Info($"      Visibility: depends on '{param.Visibility.DependsOnParameter}' = {param.Visibility.RequiredValue}");
                    }
                }
            }
        }

        /// <summary>
        /// Пример 7: Получение всех пресетов
        /// </summary>
        public static void Example7_ListAllPresets() {
            Logger.Info("=== Example 7: List All Presets ===");

            var presets = ConversionSettingsSchema.GetPredefinedPresets();

            Logger.Info($"Available presets: {presets.Count}");

            foreach (var preset in presets) {
                Logger.Info($"\nPreset: {preset.Name}");
                Logger.Info($"  Texture Type: {preset.TextureType}");
                Logger.Info($"  Description: {preset.Description}");
                Logger.Info($"  Parameters: {preset.ParameterValues.Count}");

                foreach (var kvp in preset.ParameterValues) {
                    Logger.Info($"    {kvp.Key} = {kvp.Value}");
                }
            }
        }

        /// <summary>
        /// Пример 8: Валидация значений параметров
        /// </summary>
        public static void Example8_ParameterValidation() {
            Logger.Info("=== Example 8: Parameter Validation ===");

            var globalSettings = TextureConversionSettingsManager.LoadSettings();
            var settingsManager = new ConversionSettingsManager(globalSettings);

            // Пытаемся установить значение вне диапазона
            settingsManager.SetValue("qualityLevel", 300); // Max = 255
            var actualQuality = settingsManager.GetValue("qualityLevel");
            Logger.Info($"Tried to set 300, actual value: {actualQuality}");

            // Пытаемся установить отрицательное значение
            settingsManager.SetValue("rdoLambda", -1.0); // Min = 0.01
            var actualLambda = settingsManager.GetValue("rdoLambda");
            Logger.Info($"Tried to set -1.0, actual value: {actualLambda}");

            // Валидное значение
            settingsManager.SetValue("uastcQuality", 2);
            var actualUASTCQuality = settingsManager.GetValue("uastcQuality");
            Logger.Info($"Set 2, actual value: {actualUASTCQuality}");
        }

        /// <summary>
        /// Пример 9: Специальная обработка Threads (Auto)
        /// </summary>
        public static void Example9_ThreadsAutoHandling() {
            Logger.Info("=== Example 9: Threads Auto Handling ===");

            // Случай 1: Auto с ThreadCount > 0
            var globalSettings1 = new GlobalTextureConversionSettings {
                ThreadCount = 8
            };
            var settingsManager1 = new ConversionSettingsManager(globalSettings1);
            settingsManager1.SetValue("threads", "Auto");

            var args1 = settingsManager1.GenerateToktxArguments("out.ktx2", new List<string> { "in.png" });
            var hasThreadsFlag1 = args1.Contains("--threads") && args1.Contains("8");
            Logger.Info($"Auto with ThreadCount=8: --threads flag present: {hasThreadsFlag1}");

            // Случай 2: Auto с ThreadCount = 0
            var globalSettings2 = new GlobalTextureConversionSettings {
                ThreadCount = 0
            };
            var settingsManager2 = new ConversionSettingsManager(globalSettings2);
            settingsManager2.SetValue("threads", "Auto");

            var args2 = settingsManager2.GenerateToktxArguments("out.ktx2", new List<string> { "in.png" });
            var hasThreadsFlag2 = args2.Contains("--threads");
            Logger.Info($"Auto with ThreadCount=0: --threads flag present: {hasThreadsFlag2}");

            // Случай 3: Явное значение
            var settingsManager3 = new ConversionSettingsManager(globalSettings1);
            settingsManager3.SetValue("threads", "4");

            var args3 = settingsManager3.GenerateToktxArguments("out.ktx2", new List<string> { "in.png" });
            var hasThreadsFlag3 = args3.Contains("--threads") && args3.Contains("4");
            Logger.Info($"Explicit value 4: --threads flag present with value 4: {hasThreadsFlag3}");
        }
    }
}

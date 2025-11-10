using System.Text;
using NLog;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Менеджер для управления настройками конвертации и генерации CLI команд
    /// </summary>
    public class ConversionSettingsManager {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Текущие значения параметров
        /// </summary>
        public Dictionary<string, object?> CurrentValues { get; private set; } = new();

        /// <summary>
        /// Схема параметров
        /// </summary>
        private readonly List<ParameterGroup> _parameterGroups;

        /// <summary>
        /// Глобальные настройки проекта
        /// </summary>
        private readonly GlobalTextureConversionSettings _globalSettings;

        public ConversionSettingsManager(GlobalTextureConversionSettings globalSettings) {
            _globalSettings = globalSettings;
            _parameterGroups = ConversionSettingsSchema.GetAllParameterGroups();
            InitializeDefaults();
        }

        /// <summary>
        /// Инициализирует значения по умолчанию
        /// </summary>
        private void InitializeDefaults() {
            foreach (var group in _parameterGroups) {
                foreach (var param in group.Parameters) {
                    CurrentValues[param.Id] = param.DefaultValue;
                }
            }
        }

        /// <summary>
        /// Получает все группы параметров
        /// </summary>
        public List<ParameterGroup> GetParameterGroups() {
            return _parameterGroups;
        }

        /// <summary>
        /// Получает параметр по ID
        /// </summary>
        public ConversionParameter? GetParameter(string id) {
            foreach (var group in _parameterGroups) {
                var param = group.Parameters.FirstOrDefault(p => p.Id == id);
                if (param != null) {
                    return param;
                }
            }
            return null;
        }

        /// <summary>
        /// Устанавливает значение параметра
        /// </summary>
        public void SetValue(string parameterId, object? value) {
            var param = GetParameter(parameterId);
            if (param == null) {
                Logger.Warn($"Parameter not found: {parameterId}");
                return;
            }

            // Валидация для числовых параметров
            if (param.UIType == ParameterUIType.Slider || param.UIType == ParameterUIType.NumericInput) {
                if (value is double doubleValue) {
                    if (param.MinValue.HasValue && doubleValue < param.MinValue.Value) {
                        value = param.MinValue.Value;
                    }
                    if (param.MaxValue.HasValue && doubleValue > param.MaxValue.Value) {
                        value = param.MaxValue.Value;
                    }
                }
            }

            CurrentValues[parameterId] = value;
            Logger.Info($"Parameter set: {parameterId} = {value}");
        }

        /// <summary>
        /// Получает значение параметра
        /// </summary>
        public object? GetValue(string parameterId) {
            return CurrentValues.TryGetValue(parameterId, out var value) ? value : null;
        }

        /// <summary>
        /// Применяет пресет
        /// </summary>
        public void ApplyPreset(ConversionPreset preset) {
            Logger.Info($"Applying preset: {preset.Name}");
            preset.ApplyToSettings(CurrentValues);
        }

        /// <summary>
        /// Применяет пресет по имени
        /// </summary>
        public void ApplyPresetByName(string presetName) {
            var presets = ConversionSettingsSchema.GetPredefinedPresets();
            var preset = presets.FirstOrDefault(p => p.Name == presetName);

            if (preset != null) {
                ApplyPreset(preset);
            } else {
                Logger.Warn($"Preset not found: {presetName}");
            }
        }

        /// <summary>
        /// Сбрасывает настройки к значениям по умолчанию
        /// </summary>
        public void ResetToDefaults() {
            Logger.Info("Resetting to default values");
            InitializeDefaults();
        }

        /// <summary>
        /// Проверяет видимость параметра
        /// </summary>
        public bool IsParameterVisible(ConversionParameter parameter) {
            if (parameter.Visibility == null) {
                return true;
            }
            return parameter.Visibility.IsVisible(CurrentValues);
        }

        /// <summary>
        /// Генерирует CLI аргументы для ktx create на основе текущих настроек
        /// </summary>
        /// <param name="outputPath">Путь к выходному файлу</param>
        /// <param name="inputPaths">Пути к входным файлам (мипмапы)</param>
        /// <returns>Список аргументов командной строки</returns>
        public List<string> GenerateToktxArguments(string outputPath, List<string> inputPaths) {
            var args = new List<string>();

            Logger.Info("=== GENERATING KTX CREATE CLI ARGUMENTS ===");

            // Обрабатываем параметры по порядку секций
            foreach (var group in _parameterGroups.OrderBy(g => g.Order)) {
                foreach (var param in group.Parameters) {
                    // Пропускаем невидимые параметры
                    if (!IsParameterVisible(param)) {
                        continue;
                    }

                    // Пропускаем внутренние параметры (они не генерируют CLI)
                    if (param.IsInternal) {
                        continue;
                    }

                    // Получаем значение
                    var value = GetValue(param.Id);
                    if (value == null) {
                        continue;
                    }

                    // Специальная обработка для threads
                    if (param.Id == "threads") {
                        var threadsValue = value.ToString();
                        if (threadsValue == "Auto") {
                            // Используем значение из глобальных настроек
                            if (_globalSettings.ThreadCount > 0) {
                                args.Add("--threads");
                                args.Add(_globalSettings.ThreadCount.ToString());
                            }
                            // Если ThreadCount == 0, не добавляем флаг (ktx create использует автоопределение)
                        } else {
                            args.Add("--threads");
                            args.Add(threadsValue!);
                        }
                        continue;
                    }

                    // Генерируем CLI аргумент
                    var cliArg = param.GenerateCliArgument(value);
                    if (!string.IsNullOrEmpty(cliArg)) {
                        // Разделяем на отдельные аргументы
                        var parts = cliArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts) {
                            args.Add(part);
                        }
                        Logger.Info($"  Added: {cliArg}");
                    }
                }
            }

            // Добавляем выходной файл
            args.Add($"\"{outputPath}\"");
            Logger.Info($"  Output: {outputPath}");

            // Добавляем входные файлы (мипмапы)
            foreach (var inputPath in inputPaths) {
                args.Add($"\"{inputPath}\"");
            }
            Logger.Info($"  Input files: {inputPaths.Count} mipmaps");

            Logger.Info($"=== TOKTX ARGUMENTS GENERATED: {args.Count} args ===");

            return args;
        }

        /// <summary>
        /// Генерирует полную CLI команду для toktx (для отображения/отладки)
        /// </summary>
        public string GenerateToktxCommandLine(string toktxPath, string outputPath, List<string> inputPaths) {
            var args = GenerateToktxArguments(outputPath, inputPaths);
            var sb = new StringBuilder();
            sb.Append(toktxPath);
            foreach (var arg in args) {
                sb.Append(' ');
                sb.Append(arg);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Экспортирует текущие настройки в словарь
        /// </summary>
        public Dictionary<string, object?> ExportSettings() {
            return new Dictionary<string, object?>(CurrentValues);
        }

        /// <summary>
        /// Импортирует настройки из словаря
        /// </summary>
        public void ImportSettings(Dictionary<string, object?> settings) {
            foreach (var kvp in settings) {
                if (CurrentValues.ContainsKey(kvp.Key)) {
                    CurrentValues[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Получает значения внутренних параметров для препроцессинга
        /// </summary>
        public ConversionInternalSettings GetInternalSettings() {
            return new ConversionInternalSettings {
                GenerateMipmaps = GetValue("generateMipmaps") as bool? ?? true,
                MipFilter = GetValue("mipFilter") as string ?? "kaiser",
                LinearMipFiltering = GetValue("linearMipFiltering") as bool? ?? false,
                RemoveTemporalMipmaps = GetValue("removeTemporalMipmaps") as bool? ?? true,

                // Toksvig
                EnableToksvig = GetValue("enableToksvig") as bool? ?? false,
                SmoothVariance = GetValue("smoothVariance") as bool? ?? false,
                CompositePower = Convert.ToDouble(GetValue("compositePower") ?? 1.0),
                ToksvigMinMipLevel = Convert.ToInt32(GetValue("toksvigMinMipLevel") ?? 0),
                ToksvigNormalMapPath = GetValue("toksvigNormalMapPath") as string,

                // Alpha
                SeparateRGAlpha = GetValue("separateRGAlpha") as bool? ?? false,

                // Color Space
                ColorSpace = GetValue("colorSpace") as string ?? "auto",

                // Perceptual
                PerceptualMode = GetValue("perceptualMode") as bool? ?? true
            };
        }
    }

    /// <summary>
    /// Внутренние настройки для препроцессинга
    /// </summary>
    public class ConversionInternalSettings {
        public bool GenerateMipmaps { get; set; }
        public string MipFilter { get; set; } = "kaiser";
        public bool LinearMipFiltering { get; set; }
        public bool RemoveTemporalMipmaps { get; set; }

        // Toksvig
        public bool EnableToksvig { get; set; }
        public bool SmoothVariance { get; set; }
        public double CompositePower { get; set; }
        public int ToksvigMinMipLevel { get; set; }
        public string? ToksvigNormalMapPath { get; set; }

        // Alpha
        public bool SeparateRGAlpha { get; set; }

        // Color Space
        public string ColorSpace { get; set; } = "auto";

        // Perceptual
        public bool PerceptualMode { get; set; }
    }
}

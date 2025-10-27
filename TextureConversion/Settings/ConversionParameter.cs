using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Тип элемента интерфейса для параметра конвертации
    /// </summary>
    public enum ParameterUIType {
        Checkbox,
        Dropdown,
        Slider,
        NumericInput,
        FilePath,
        RadioGroup,
        Vector2,
        Button
    }

    /// <summary>
    /// Секция параметров конвертации
    /// </summary>
    public enum ParameterSection {
        Preset,
        Compression,
        Alpha,
        ColorSpace,
        Mipmaps,
        NormalMaps,
        Toksvig,
        Actions
    }

    /// <summary>
    /// Условие видимости параметра
    /// </summary>
    public class VisibilityCondition {
        /// <summary>
        /// Имя параметра, от которого зависит видимость
        /// </summary>
        public string? DependsOnParameter { get; set; }

        /// <summary>
        /// Требуемое значение параметра для отображения
        /// </summary>
        public object? RequiredValue { get; set; }

        /// <summary>
        /// Проверяет, должен ли параметр быть видимым
        /// </summary>
        public bool IsVisible(Dictionary<string, object?> currentValues) {
            if (string.IsNullOrEmpty(DependsOnParameter)) {
                return true;
            }

            if (!currentValues.TryGetValue(DependsOnParameter, out var value)) {
                return false;
            }

            return Equals(value, RequiredValue);
        }
    }

    /// <summary>
    /// Описание параметра конвертации
    /// </summary>
    public class ConversionParameter {
        /// <summary>
        /// Уникальный идентификатор параметра
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Отображаемое имя параметра
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Тип UI элемента
        /// </summary>
        public ParameterUIType UIType { get; set; }

        /// <summary>
        /// Секция, к которой относится параметр
        /// </summary>
        public ParameterSection Section { get; set; }

        /// <summary>
        /// Значение по умолчанию
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// CLI флаг toktx (null если внутренний параметр)
        /// </summary>
        public string? CliFlag { get; set; }

        /// <summary>
        /// Описание для tooltip
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Условие видимости
        /// </summary>
        public VisibilityCondition? Visibility { get; set; }

        /// <summary>
        /// Возможные значения (для dropdown, radio)
        /// </summary>
        public List<string>? Options { get; set; }

        /// <summary>
        /// Минимальное значение (для slider, numeric)
        /// </summary>
        public double? MinValue { get; set; }

        /// <summary>
        /// Максимальное значение (для slider, numeric)
        /// </summary>
        public double? MaxValue { get; set; }

        /// <summary>
        /// Шаг изменения (для slider, numeric)
        /// </summary>
        public double? Step { get; set; }

        /// <summary>
        /// Является ли параметр внутренним (не генерирует CLI)
        /// </summary>
        public bool IsInternal { get; set; }

        /// <summary>
        /// Генерирует CLI аргумент из значения
        /// </summary>
        public virtual string? GenerateCliArgument(object? value) {
            if (IsInternal || string.IsNullOrEmpty(CliFlag) || value == null) {
                return null;
            }

            if (UIType == ParameterUIType.Checkbox) {
                if (value is bool boolValue && boolValue) {
                    return CliFlag;
                }
                return null;
            }

            if (UIType == ParameterUIType.Dropdown || UIType == ParameterUIType.RadioGroup) {
                var stringValue = value.ToString();
                if (string.IsNullOrEmpty(stringValue)) {
                    return null;
                }
                return $"{CliFlag} {stringValue}";
            }

            if (UIType == ParameterUIType.Slider || UIType == ParameterUIType.NumericInput) {
                return $"{CliFlag} {value}";
            }

            if (UIType == ParameterUIType.FilePath) {
                var path = value.ToString();
                if (string.IsNullOrEmpty(path)) {
                    return null;
                }
                return $"{CliFlag} \"{path}\"";
            }

            return null;
        }
    }

    /// <summary>
    /// Группа параметров (секция)
    /// </summary>
    public class ParameterGroup {
        /// <summary>
        /// Название секции
        /// </summary>
        public ParameterSection Section { get; set; }

        /// <summary>
        /// Отображаемое имя секции
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Описание секции
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Параметры в этой секции
        /// </summary>
        public List<ConversionParameter> Parameters { get; set; } = new();

        /// <summary>
        /// Порядок отображения
        /// </summary>
        public int Order { get; set; }
    }

    /// <summary>
    /// Пресет настроек конвертации
    /// </summary>
    public class ConversionPreset {
        /// <summary>
        /// Имя пресета
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Описание пресета
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Тип текстуры для которого оптимизирован пресет
        /// </summary>
        public TextureType TextureType { get; set; }

        /// <summary>
        /// Значения параметров в этом пресете
        /// </summary>
        public Dictionary<string, object?> ParameterValues { get; set; } = new();

        /// <summary>
        /// Применяет пресет к настройкам
        /// </summary>
        public void ApplyToSettings(Dictionary<string, object?> currentSettings) {
            foreach (var kvp in ParameterValues) {
                currentSettings[kvp.Key] = kvp.Value;
            }
        }
    }
}

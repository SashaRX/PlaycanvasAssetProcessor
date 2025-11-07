using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Расширение для ConversionSettingsSchema - упрощённая группа параметров гистограммы
    /// </summary>
    public static class HistogramGroupExtension {
        /// <summary>
        /// Секция 7.5: Histogram Preprocessing (упрощённая версия с 2 режимами)
        /// </summary>
        public static ParameterGroup GetHistogramGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Histogram,
                DisplayName = "Histogram Preprocessing",
                Description = "Нормализация текстуры перед сжатием с записью scale/offset в KVD для восстановления на GPU",
                Order = 8,
                Parameters = new List<ConversionParameter> {
                    // Enable Histogram Preprocessing
                    new ConversionParameter {
                        Id = "enableHistogram",
                        DisplayName = "Enable Histogram Preprocessing",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Histogram,
                        DefaultValue = false,
                        Description = "Включить нормализацию текстуры с robust анализом гистограммы (PercentileWithKnee)",
                        IsInternal = true
                    },

                    // Quality Mode (2 режима)
                    new ConversionParameter {
                        Id = "histogramQuality",
                        DisplayName = "Quality Mode",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Histogram,
                        DefaultValue = "HighQuality",
                        Options = new List<string> {
                            "HighQuality",
                            "Fast"
                        },
                        Description = "HighQuality (рекомендуется): PercentileWithKnee (0.5%, 99.5%), soft-knee сглаживание. Fast: Percentile (1%, 99%), жёсткое клампирование",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Channel Mode (упрощённо)
                    new ConversionParameter {
                        Id = "histogramChannelMode",
                        DisplayName = "Channel Mode",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Histogram,
                        DefaultValue = "AverageLuminance",
                        Options = new List<string> {
                            "AverageLuminance",
                            "PerChannel"
                        },
                        Description = "AverageLuminance = один общий scale/offset (рекомендуется). PerChannel = отдельные scale/offset для RGB",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    }
                }
            };
        }
    }
}

using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Расширение для ConversionSettingsSchema - группа параметров гистограммы
    /// </summary>
    public static class HistogramGroupExtension {
        /// <summary>
        /// Секция 7.5: Histogram Analysis
        /// </summary>
        public static ParameterGroup GetHistogramGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Histogram,
                DisplayName = "Histogram Analysis",
                Description = "Автоматический анализ диапазона значений для оптимизации сжатия",
                Order = 8,
                Parameters = new List<ConversionParameter> {
                    // Enable Histogram Analysis
                    new ConversionParameter {
                        Id = "enableHistogram",
                        DisplayName = "Enable Histogram Analysis",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Histogram,
                        DefaultValue = false,
                        Description = "Включить robust анализ диапазона значений с записью scale/offset в KTX2",
                        IsInternal = true
                    },

                    // Histogram Mode
                    new ConversionParameter {
                        Id = "histogramMode",
                        DisplayName = "Analysis Mode",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Histogram,
                        DefaultValue = "PercentileWithKnee",
                        Options = new List<string> {
                            "Percentile",
                            "PercentileWithKnee"
                        },
                        Description = "Percentile = жёсткое клампирование, PercentileWithKnee = мягкое сглаживание (рекомендуется)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Processing Mode
                    new ConversionParameter {
                        Id = "histogramProcessing",
                        DisplayName = "Processing Mode",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Histogram,
                        DefaultValue = "MetadataOnly",
                        Options = new List<string> {
                            "MetadataOnly",
                            "Preprocessing"
                        },
                        Description = "MetadataOnly (lossless) = только scale/offset в KTX2, GPU применяет FMA. Preprocessing (lossy) = применить к пикселям перед сжатием",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Channel Mode
                    new ConversionParameter {
                        Id = "histogramChannelMode",
                        DisplayName = "Channel Mode",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Histogram,
                        DefaultValue = "AverageLuminance",
                        Options = new List<string> {
                            "AverageLuminance",
                            "PerChannel",
                            "RGBOnly",
                            "PerChannelRGBA"
                        },
                        Description = "AverageLuminance = один scale/offset, PerChannel = отдельные scale/offset для каждого канала",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Quantization
                    new ConversionParameter {
                        Id = "histogramQuantization",
                        DisplayName = "Quantization",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Histogram,
                        DefaultValue = "Half16",
                        Options = new List<string> {
                            "Half16",
                            "PackedUInt32",
                            "Float32"
                        },
                        Description = "Half16 = 4 байта (рекомендуется), PackedUInt32 = 4 байта (для [0,1]), Float32 = 8 байт (максимальная точность)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Percentile Low
                    new ConversionParameter {
                        Id = "histogramPercentileLow",
                        DisplayName = "Percentile Low (%)",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Histogram,
                        DefaultValue = 0.5,
                        MinValue = 0.0,
                        MaxValue = 50.0,
                        Step = 0.1,
                        Description = "Нижний перцентиль (0.5% = игнорировать самые тёмные 0.5% пикселей)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Percentile High
                    new ConversionParameter {
                        Id = "histogramPercentileHigh",
                        DisplayName = "Percentile High (%)",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Histogram,
                        DefaultValue = 99.5,
                        MinValue = 50.0,
                        MaxValue = 100.0,
                        Step = 0.1,
                        Description = "Верхний перцентиль (99.5% = игнорировать самые яркие 0.5% пикселей)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Knee Width
                    new ConversionParameter {
                        Id = "histogramKneeWidth",
                        DisplayName = "Knee Width",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Histogram,
                        DefaultValue = 0.02,
                        MinValue = 0.0,
                        MaxValue = 0.2,
                        Step = 0.01,
                        Description = "Ширина мягкого колена в долях диапазона (0.02 = 2%). Только для PercentileWithKnee",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "histogramMode",
                            RequiredValue = "PercentileWithKnee"
                        }
                    },

                    // Min Range Threshold
                    new ConversionParameter {
                        Id = "histogramMinRange",
                        DisplayName = "Min Range Threshold",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Histogram,
                        DefaultValue = 0.01,
                        MinValue = 0.0,
                        MaxValue = 0.5,
                        Step = 0.001,
                        Description = "Минимальный диапазон для нормализации (0.01 = игнорировать почти константные текстуры)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableHistogram",
                            RequiredValue = true
                        }
                    },

                    // Write Histogram Params
                    new ConversionParameter {
                        Id = "writeHistogramParams",
                        DisplayName = "Write Analysis Params to KTX2",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Histogram,
                        DefaultValue = true,
                        Description = "Записать параметры анализа (pLow, pHigh, knee) в KTX2 метаданные",
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

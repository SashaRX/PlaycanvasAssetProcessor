using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Схема всех параметров конвертации текстур
    /// </summary>
    public static class ConversionSettingsSchema {
        /// <summary>
        /// Получает все группы параметров
        /// </summary>
        public static List<ParameterGroup> GetAllParameterGroups() {
            return new List<ParameterGroup> {
                GetPresetGroup(),
                GetCompressionGroup(),
                GetAlphaGroup(),
                GetColorSpaceGroup(),
                GetMipmapsGroup(),
                GetNormalMapsGroup(),
                GetToksvigGroup(),
                HistogramGroupExtension.GetHistogramGroup(),
                GetActionsGroup()
            };
        }

        /// <summary>
        /// Секция 1: Preset
        /// </summary>
        private static ParameterGroup GetPresetGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Preset,
                DisplayName = "Preset",
                Description = "Выбор базового профиля (меняет дефолты других секций)",
                Order = 1,
                Parameters = new List<ConversionParameter> {
                    new ConversionParameter {
                        Id = "preset",
                        DisplayName = "Preset",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Preset,
                        DefaultValue = "Custom",
                        Options = new List<string> {
                            "Custom",
                            "Albedo/Color (sRGB)",
                            "Normal (Linear)",
                            "AO/Gloss/Roughness (Linear + Toksvig)",
                            "Height (Linear with Clamp)"
                        },
                        Description = "Базовый профиль настроек для разных типов текстур",
                        IsInternal = true
                    }
                }
            };
        }

        /// <summary>
        /// Секция 2: Compression Settings
        /// </summary>
        private static ParameterGroup GetCompressionGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Compression,
                DisplayName = "Compression Settings",
                Description = "Управляет типом кодека и параметрами качества",
                Order = 2,
                Parameters = new List<ConversionParameter> {
                    // Compression Format
                    new ConversionParameter {
                        Id = "compressionFormat",
                        DisplayName = "Compression Format",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Compression,
                        DefaultValue = "etc1s",
                        Options = new List<string> { "etc1s", "uastc", "astc" },
                        CliFlag = "--encode",
                        Description = "Формат сжатия: ETC1S (малый размер), UASTC (высокое качество), ASTC (adaptive)"
                    },

                    // Compression Level (только для ETC1S)
                    new ConversionParameter {
                        Id = "compressionLevel",
                        DisplayName = "Compression Level",
                        UIType = ParameterUIType.Slider,
                        Section = ParameterSection.Compression,
                        DefaultValue = 1,
                        MinValue = 0,
                        MaxValue = 5,
                        Step = 1,
                        CliFlag = "--clevel",
                        Description = "Уровень компрессии (0=быстро, 5=медленно/лучше). Только для ETC1S",
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "compressionFormat",
                            RequiredValue = "etc1s"
                        }
                    },

                    // Quality Level
                    new ConversionParameter {
                        Id = "qualityLevel",
                        DisplayName = "Quality",
                        UIType = ParameterUIType.Slider,
                        Section = ParameterSection.Compression,
                        DefaultValue = 128,
                        MinValue = 1,
                        MaxValue = 255,
                        Step = 1,
                        CliFlag = "--qlevel",
                        Description = "Качество сжатия (1=низкое, 255=высокое). Для ETC1S",
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "compressionFormat",
                            RequiredValue = "etc1s"
                        }
                    },

                    // UASTC Quality
                    new ConversionParameter {
                        Id = "uastcQuality",
                        DisplayName = "UASTC Quality",
                        UIType = ParameterUIType.Slider,
                        Section = ParameterSection.Compression,
                        DefaultValue = 2,
                        MinValue = 0,
                        MaxValue = 4,
                        Step = 1,
                        CliFlag = "--uastc_quality",
                        Description = "Качество UASTC (0=fastest, 4=slowest/best)",
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "compressionFormat",
                            RequiredValue = "uastc"
                        }
                    },

                    // ASTC Quality
                    new ConversionParameter {
                        Id = "astcQuality",
                        DisplayName = "ASTC Quality",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Compression,
                        DefaultValue = "medium",
                        Options = new List<string> { "fastest", "fast", "medium", "thorough", "exhaustive" },
                        CliFlag = "--astc_quality",
                        Description = "Качество ASTC компрессии",
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "compressionFormat",
                            RequiredValue = "astc"
                        }
                    },

                    // Perceptual Mode
                    new ConversionParameter {
                        Id = "perceptualMode",
                        DisplayName = "Perceptual Mode",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Compression,
                        DefaultValue = true,
                        Description = "Визуальное улучшение качества. Только для ETC1S/ASTC",
                        IsInternal = true // Handled by toktx automatically for ETC1S
                    },

                    // RDO Optimizer
                    new ConversionParameter {
                        Id = "useRDO",
                        DisplayName = "RDO Optimizer",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Compression,
                        DefaultValue = true,
                        Description = "Rate-Distortion Optimization: баланс размера/качества",
                        IsInternal = true
                    },

                    // RDO Lambda (UASTC)
                    new ConversionParameter {
                        Id = "rdoLambda",
                        DisplayName = "RDO Lambda (λ)",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Compression,
                        DefaultValue = 1.0,
                        MinValue = 0.01,
                        MaxValue = 10.0,
                        Step = 0.1,
                        CliFlag = "--uastc_rdo_l",
                        Description = "Параметр RDO для UASTC (больше = меньше размер, меньше качество)",
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "compressionFormat",
                            RequiredValue = "uastc"
                        }
                    },

                    // Supercompression (Zstd)
                    new ConversionParameter {
                        Id = "supercompression",
                        DisplayName = "Supercompression (Zstd)",
                        UIType = ParameterUIType.Slider,
                        Section = ParameterSection.Compression,
                        DefaultValue = 9,
                        MinValue = 1,
                        MaxValue = 22,
                        Step = 1,
                        CliFlag = "--zcmp",
                        Description = "Уровень Zstandard суперкомпрессии (1=быстро, 22=максимум)"
                    },

                    // Threads
                    new ConversionParameter {
                        Id = "threads",
                        DisplayName = "Threads",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Compression,
                        DefaultValue = "Auto",
                        Options = new List<string> { "Auto", "1", "2", "4", "8", "16", "32" },
                        CliFlag = "--threads",
                        Description = "Количество потоков (Auto = настройки проекта)"
                    }
                }
            };
        }

        /// <summary>
        /// Секция 3: Alpha Options
        /// </summary>
        private static ParameterGroup GetAlphaGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Alpha,
                DisplayName = "Alpha Options",
                Description = "Управление альфа-каналом",
                Order = 3,
                Parameters = new List<ConversionParameter> {
                    // Force Alpha Channel
                    new ConversionParameter {
                        Id = "forceAlpha",
                        DisplayName = "Force Alpha Channel",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Alpha,
                        DefaultValue = false,
                        CliFlag = "--target_type RGBA",
                        Description = "Принудительно добавить альфа-канал (даже если его нет)"
                    },

                    // Remove Alpha Channel
                    new ConversionParameter {
                        Id = "removeAlpha",
                        DisplayName = "Remove Alpha Channel",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Alpha,
                        DefaultValue = false,
                        CliFlag = "--target_type RGB",
                        Description = "Удалить альфа-канал"
                    },

                    // Separate RG to Color/Alpha
                    new ConversionParameter {
                        Id = "separateRGAlpha",
                        DisplayName = "Separate RG to Color/Alpha (Normals)",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Alpha,
                        DefaultValue = false,
                        Description = "Разделить RG каналы для XY нормалей (внутренний препроцессинг)",
                        IsInternal = true
                    }
                }
            };
        }

        /// <summary>
        /// Секция 4: Color & Space
        /// </summary>
        private static ParameterGroup GetColorSpaceGroup() {
            return new ParameterGroup {
                Section = ParameterSection.ColorSpace,
                DisplayName = "Color Space",
                Description = "Управление цветовым пространством",
                Order = 4,
                Parameters = new List<ConversionParameter> {
                    // Color Space Mode
                    new ConversionParameter {
                        Id = "colorSpace",
                        DisplayName = "Color Space",
                        UIType = ParameterUIType.RadioGroup,
                        Section = ParameterSection.ColorSpace,
                        DefaultValue = "auto",
                        Options = new List<string> { "auto", "linear", "srgb" },
                        Description = "Цветовое пространство текстуры",
                        IsInternal = true
                    },

                    // Treat as Linear
                    new ConversionParameter {
                        Id = "treatAsLinear",
                        DisplayName = "Treat as Linear",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.ColorSpace,
                        DefaultValue = false,
                        CliFlag = "--assign_oetf linear",
                        Description = "Принудительно обрабатывать как линейное пространство"
                    },

                    // Treat as sRGB
                    new ConversionParameter {
                        Id = "treatAsSRGB",
                        DisplayName = "Treat as sRGB",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.ColorSpace,
                        DefaultValue = false,
                        CliFlag = "--assign_oetf srgb",
                        Description = "Принудительно обрабатывать как sRGB"
                    }
                }
            };
        }

        /// <summary>
        /// Секция 5: Mipmaps
        /// </summary>
        private static ParameterGroup GetMipmapsGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Mipmaps,
                DisplayName = "Mipmaps",
                Description = "Настройки генерации мипмапов",
                Order = 5,
                Parameters = new List<ConversionParameter> {
                    // Generate Mipmaps
                    new ConversionParameter {
                        Id = "generateMipmaps",
                        DisplayName = "Generate Mipmaps",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Mipmaps,
                        DefaultValue = true,
                        Description = "Генерировать мипмапы (всегда true, так как мы генерируем их сами)",
                        IsInternal = true
                    },

                    // Filter
                    new ConversionParameter {
                        Id = "mipFilter",
                        DisplayName = "Filter",
                        UIType = ParameterUIType.Dropdown,
                        Section = ParameterSection.Mipmaps,
                        DefaultValue = "kaiser",
                        Options = new List<string> {
                            "box", "tent", "bell", "b-spline", "mitchell",
                            "lanczos3", "lanczos4", "lanczos6", "lanczos12",
                            "blackman", "kaiser", "gaussian", "catmullrom",
                            "quadratic_interp", "quadratic_approx", "quadratic_mix",
                            "min", "max"
                        },
                        Description = "Фильтр для генерации мипмапов (используется внутренним MipGenerator)",
                        IsInternal = true
                    },

                    // Linear Mip Filtering
                    new ConversionParameter {
                        Id = "linearMipFiltering",
                        DisplayName = "Linear Mip Filtering",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Mipmaps,
                        DefaultValue = false,
                        Description = "Линейная фильтрация при генерации мипмапов (внутренний параметр)",
                        IsInternal = true
                    },

                    // Clamp Mip Edges
                    new ConversionParameter {
                        Id = "clampMipEdges",
                        DisplayName = "Clamp Mip Edges",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Mipmaps,
                        DefaultValue = false,
                        CliFlag = "--wmode clamp",
                        Description = "Ограничить края мипмапов (wrapping mode)"
                    },

                    // Remove Temporal Mipmaps
                    new ConversionParameter {
                        Id = "removeTemporalMipmaps",
                        DisplayName = "Remove Temporal Mipmaps",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Mipmaps,
                        DefaultValue = true,
                        Description = "Очистить временные мипмапы после сборки (по умолчанию ВКЛ, отключить для отладки)",
                        IsInternal = true
                    },

                    // Normalize Mipmaps (для Normal Maps)
                    new ConversionParameter {
                        Id = "normalizeMipmaps",
                        DisplayName = "Normalize Mipmaps (Normal Maps)",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Mipmaps,
                        DefaultValue = false,
                        CliFlag = "--normalize",
                        Description = "Нормализовать векторы в мипмапах (только для Normal Maps с 2+ компонентами)"
                    }
                }
            };
        }

        /// <summary>
        /// Секция 6: Normal Maps
        /// </summary>
        private static ParameterGroup GetNormalMapsGroup() {
            return new ParameterGroup {
                Section = ParameterSection.NormalMaps,
                DisplayName = "Normal Maps",
                Description = "Специальные настройки для карт нормалей",
                Order = 6,
                Parameters = new List<ConversionParameter> {
                    // Convert to XY Normal Map
                    new ConversionParameter {
                        Id = "convertToXYNormal",
                        DisplayName = "Convert to XY(RGB/A) Normal Map",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.NormalMaps,
                        DefaultValue = false,
                        CliFlag = "--normal_mode",
                        Description = "Конвертировать RGB→XY для хранения нормали в R-X/G-Y/B-Z или RGB-X/A-Y"
                    },

                    // Normalize Vectors
                    new ConversionParameter {
                        Id = "normalizeVectors",
                        DisplayName = "Normalize Vectors",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.NormalMaps,
                        DefaultValue = false,
                        CliFlag = "--normalize",
                        Description = "Принудительно нормализовать векторы нормалей (для карт нормалей)"
                    },

                    // Keep RGB Layout
                    new ConversionParameter {
                        Id = "keepRGBLayout",
                        DisplayName = "Keep RGB Layout (Custom)",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.NormalMaps,
                        DefaultValue = false,
                        CliFlag = "--input_swizzle rgb1",
                        Description = "Оставить RGB-структуру нормалей без преобразования"
                    }
                }
            };
        }

        /// <summary>
        /// Секция 7: Toksvig (Anti-Aliasing)
        /// </summary>
        private static ParameterGroup GetToksvigGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Toksvig,
                DisplayName = "Toksvig (Anti-Aliasing)",
                Description = "Коррекция Toksvig для gloss/roughness текстур",
                Order = 7,
                Parameters = new List<ConversionParameter> {
                    // Enable Toksvig Correction
                    new ConversionParameter {
                        Id = "enableToksvig",
                        DisplayName = "Enable Toksvig Correction",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Toksvig,
                        DefaultValue = false,
                        Description = "Включить коррекцию Toksvig для Gloss/Roughness (внутренний препроцесс)",
                        IsInternal = true
                    },

                    // Smooth Variance
                    new ConversionParameter {
                        Id = "smoothVariance",
                        DisplayName = "Smooth Variance",
                        UIType = ParameterUIType.Checkbox,
                        Section = ParameterSection.Toksvig,
                        DefaultValue = false,
                        Description = "Сглаживание вариации углов (внутренний препроцесс)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableToksvig",
                            RequiredValue = true
                        }
                    },

                    // Composite Power (k)
                    new ConversionParameter {
                        Id = "compositePower",
                        DisplayName = "Composite Power (k)",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Toksvig,
                        DefaultValue = 1.0,
                        MinValue = 0.1,
                        MaxValue = 5.0,
                        Step = 0.1,
                        Description = "Степень смешивания нормалей (внутренний параметр)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableToksvig",
                            RequiredValue = true
                        }
                    },

                    // Min Mip Level
                    new ConversionParameter {
                        Id = "toksvigMinMipLevel",
                        DisplayName = "Min Mip Level",
                        UIType = ParameterUIType.NumericInput,
                        Section = ParameterSection.Toksvig,
                        DefaultValue = 0,
                        MinValue = 0,
                        MaxValue = 12,
                        Step = 1,
                        Description = "Базовый уровень мипмапов для применения Toksvig (внутренний параметр)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableToksvig",
                            RequiredValue = true
                        }
                    },

                    // Normal Map Path
                    new ConversionParameter {
                        Id = "toksvigNormalMapPath",
                        DisplayName = "Normal Map Path",
                        UIType = ParameterUIType.FilePath,
                        Section = ParameterSection.Toksvig,
                        DefaultValue = null,
                        Description = "Путь к карте нормалей (автоподстановка: nameTex_normal для nameTex_gloss)",
                        IsInternal = true,
                        Visibility = new VisibilityCondition {
                            DependsOnParameter = "enableToksvig",
                            RequiredValue = true
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Секция 8: Actions
        /// </summary>
        private static ParameterGroup GetActionsGroup() {
            return new ParameterGroup {
                Section = ParameterSection.Actions,
                DisplayName = "Actions",
                Description = "Действия конвертации",
                Order = 8,
                Parameters = new List<ConversionParameter> {
                    // Convert Selected
                    new ConversionParameter {
                        Id = "convertSelected",
                        DisplayName = "Convert Selected",
                        UIType = ParameterUIType.Button,
                        Section = ParameterSection.Actions,
                        Description = "Конвертировать выбранные текстуры",
                        IsInternal = true
                    },

                    // Apply Preset
                    new ConversionParameter {
                        Id = "applyPreset",
                        DisplayName = "Apply",
                        UIType = ParameterUIType.Button,
                        Section = ParameterSection.Actions,
                        Description = "Применить и сохранить текущие настройки как пресет",
                        IsInternal = true
                    },

                    // Reset
                    new ConversionParameter {
                        Id = "reset",
                        DisplayName = "Reset",
                        UIType = ParameterUIType.Button,
                        Section = ParameterSection.Actions,
                        Description = "Сбросить настройки к значениям по умолчанию",
                        IsInternal = true
                    }
                }
            };
        }

        /// <summary>
        /// Получает предопределенные пресеты
        /// </summary>
        public static List<ConversionPreset> GetPredefinedPresets() {
            return new List<ConversionPreset> {
                // Albedo / Color (sRGB)
                new ConversionPreset {
                    Name = "Albedo/Color (sRGB)",
                    Description = "Оптимизирован для цветных текстур в sRGB",
                    TextureType = TextureType.Albedo,
                    ParameterValues = new Dictionary<string, object?> {
                        { "compressionFormat", "etc1s" },
                        { "qualityLevel", 128 },
                        { "treatAsSRGB", true },
                        { "mipFilter", "kaiser" },
                        { "perceptualMode", true }
                    }
                },

                // Normal (Linear)
                new ConversionPreset {
                    Name = "Normal (Linear)",
                    Description = "Оптимизирован для карт нормалей",
                    TextureType = TextureType.Normal,
                    ParameterValues = new Dictionary<string, object?> {
                        { "compressionFormat", "uastc" },
                        { "uastcQuality", 3 },
                        { "treatAsLinear", true },
                        { "convertToXYNormal", true },
                        { "normalizeVectors", true },
                        { "mipFilter", "kaiser" }
                    }
                },

                // Gloss (Linear + Toksvig) - также подходит для Roughness и AO
                new ConversionPreset {
                    Name = "Gloss (Linear + Toksvig)",
                    Description = "Оптимизирован для gloss/roughness с коррекцией Toksvig",
                    TextureType = TextureType.Gloss,
                    ParameterValues = new Dictionary<string, object?> {
                        { "compressionFormat", "etc1s" },
                        { "qualityLevel", 128 },
                        { "treatAsLinear", true },
                        { "mipFilter", "kaiser" },
                        { "enableToksvig", true },
                        { "compositePower", 1.0 }
                    }
                },

                // Height (Linear with Clamp)
                new ConversionPreset {
                    Name = "Height (Linear with Clamp)",
                    Description = "Оптимизирован для карт высот",
                    TextureType = TextureType.Height,
                    ParameterValues = new Dictionary<string, object?> {
                        { "compressionFormat", "etc1s" },
                        { "qualityLevel", 128 },
                        { "treatAsLinear", true },
                        { "mipFilter", "mitchell" },
                        { "clampMipEdges", true }
                    }
                }
            };
        }
    }
}

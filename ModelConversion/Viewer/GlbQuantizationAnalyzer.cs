using System.IO;
using System.Text;
using System.Text.Json;
using NLog;

namespace AssetProcessor.ModelConversion.Viewer {
    /// <summary>
    /// Анализатор квантования GLB файлов
    /// Определяет параметры квантования UV координат для корректного отображения
    /// </summary>
    public class GlbQuantizationAnalyzer {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Информация о квантовании UV из GLB файла
        /// </summary>
        public class UVQuantizationInfo {
            /// <summary>
            /// Файл содержит квантованные UV (KHR_mesh_quantization)
            /// </summary>
            public bool IsQuantized { get; set; }

            /// <summary>
            /// Файл содержит meshopt compression (EXT_meshopt_compression)
            /// </summary>
            public bool HasMeshOptCompression { get; set; }

            /// <summary>
            /// Тип компонента TEXCOORD_0 accessor
            /// 5126 = FLOAT, 5123 = UNSIGNED_SHORT, 5121 = UNSIGNED_BYTE
            /// </summary>
            public int ComponentType { get; set; }

            /// <summary>
            /// Normalized flag из accessor
            /// </summary>
            public bool Normalized { get; set; }

            /// <summary>
            /// Минимальные UV значения из accessor (если есть)
            /// </summary>
            public float[]? Min { get; set; }

            /// <summary>
            /// Максимальные UV значения из accessor (если есть)
            /// </summary>
            public float[]? Max { get; set; }

            /// <summary>
            /// Расчетное количество бит квантования (на основе max значения)
            /// 16 бит = max около 1.0, 12 бит = max около 0.0625
            /// </summary>
            public int EstimatedBits { get; set; } = 16;

            /// <summary>
            /// Масштаб для коррекции U (1.0 = без коррекции)
            /// </summary>
            public float UVScaleU { get; set; } = 1.0f;

            /// <summary>
            /// Масштаб для коррекции V (1.0 = без коррекции)
            /// </summary>
            public float UVScaleV { get; set; } = 1.0f;

            /// <summary>
            /// Масштаб для коррекции UV (для обратной совместимости, берёт max из U и V)
            /// </summary>
            public float UVScale => Math.Max(UVScaleU, UVScaleV);

            /// <summary>
            /// Описание проблемы с квантованием (если есть)
            /// </summary>
            public string? Issue { get; set; }
        }

        /// <summary>
        /// Анализирует GLB файл и определяет параметры квантования UV
        /// </summary>
        /// <param name="glbPath">Путь к GLB файлу</param>
        /// <returns>Информация о квантовании UV</returns>
        public static UVQuantizationInfo AnalyzeQuantization(string glbPath) {
            var info = new UVQuantizationInfo();

            try {
                using var stream = File.OpenRead(glbPath);
                using var reader = new BinaryReader(stream);

                // Read GLB header (12 bytes)
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint length = reader.ReadUInt32();

                // Verify GLB magic number ("glTF")
                if (magic != 0x46546C67) {
                    info.Issue = "Invalid GLB file (bad magic)";
                    return info;
                }

                // Read JSON chunk
                uint chunkLength = reader.ReadUInt32();
                uint chunkType = reader.ReadUInt32();

                // Verify JSON chunk type ("JSON")
                if (chunkType != 0x4E4F534A) {
                    info.Issue = "Invalid GLB file (no JSON chunk)";
                    return info;
                }

                // Read JSON data
                byte[] jsonBytes = reader.ReadBytes((int)chunkLength);
                string jsonString = Encoding.UTF8.GetString(jsonBytes);

                // Parse JSON
                using var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                // Check extensions
                if (root.TryGetProperty("extensionsUsed", out var extensionsUsed)) {
                    foreach (var ext in extensionsUsed.EnumerateArray()) {
                        var extName = ext.GetString();
                        if (extName == "KHR_mesh_quantization") {
                            info.IsQuantized = true;
                        }
                        if (extName == "EXT_meshopt_compression") {
                            info.HasMeshOptCompression = true;
                        }
                    }
                }

                // Find TEXCOORD_0 accessor
                if (root.TryGetProperty("meshes", out var meshes)) {
                    foreach (var mesh in meshes.EnumerateArray()) {
                        if (mesh.TryGetProperty("primitives", out var primitives)) {
                            foreach (var primitive in primitives.EnumerateArray()) {
                                if (primitive.TryGetProperty("attributes", out var attributes)) {
                                    if (attributes.TryGetProperty("TEXCOORD_0", out var texCoord0)) {
                                        int accessorIndex = texCoord0.GetInt32();

                                        // Get accessor info
                                        if (root.TryGetProperty("accessors", out var accessors)) {
                                            var accessorList = accessors.EnumerateArray().ToList();
                                            if (accessorIndex < accessorList.Count) {
                                                var accessor = accessorList[accessorIndex];

                                                // Component type
                                                if (accessor.TryGetProperty("componentType", out var compType)) {
                                                    info.ComponentType = compType.GetInt32();
                                                }

                                                // Normalized
                                                if (accessor.TryGetProperty("normalized", out var normalized)) {
                                                    info.Normalized = normalized.GetBoolean();
                                                }

                                                // Min/Max
                                                if (accessor.TryGetProperty("min", out var minArray)) {
                                                    info.Min = minArray.EnumerateArray()
                                                        .Select(v => v.GetSingle())
                                                        .ToArray();
                                                }

                                                if (accessor.TryGetProperty("max", out var maxArray)) {
                                                    info.Max = maxArray.EnumerateArray()
                                                        .Select(v => v.GetSingle())
                                                        .ToArray();
                                                }

                                                // Calculate UV scale based on quantization
                                                CalculateUVScale(info);

                                                // Found first TEXCOORD_0, stop
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            if (info.ComponentType != 0) break;
                        }
                    }
                }

                LogQuantizationInfo(glbPath, info);

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to analyze GLB quantization: {glbPath}");
                info.Issue = ex.Message;
            }

            return info;
        }

        /// <summary>
        /// Вычисляет масштаб UV на основе параметров квантования (раздельно для U и V)
        /// </summary>
        private static void CalculateUVScale(UVQuantizationInfo info) {
            // Если float - масштаб не нужен
            if (info.ComponentType == 5126) { // GL_FLOAT
                info.EstimatedBits = 32;
                info.UVScaleU = 1.0f;
                info.UVScaleV = 1.0f;
                return;
            }

            // Если UNSIGNED_SHORT normalized
            if (info.ComponentType == 5123 && info.Normalized) { // GL_UNSIGNED_SHORT
                // Проверяем max значение чтобы понять количество бит для каждой оси
                if (info.Max != null && info.Max.Length >= 2) {
                    float maxU = info.Max[0];
                    float maxV = info.Max[1];

                    // Вычисляем масштаб отдельно для U и V
                    info.UVScaleU = CalculateAxisScale(maxU, out int bitsU);
                    info.UVScaleV = CalculateAxisScale(maxV, out int bitsV);
                    info.EstimatedBits = Math.Min(bitsU, bitsV);

                    // Логируем если оси имеют разный масштаб
                    if (Math.Abs(info.UVScaleU - info.UVScaleV) > 0.01f) {
                        info.Issue = $"Different UV scale per axis: U={info.UVScaleU:F2}x (max={maxU:F4}), V={info.UVScaleV:F2}x (max={maxV:F4})";
                    } else if (info.UVScaleU > 1.01f) {
                        info.Issue = $"{info.EstimatedBits}-bit UV quantization detected, applying {info.UVScaleU:F1}x scale correction";
                    }
                }
            }

            // UNSIGNED_BYTE normalized
            if (info.ComponentType == 5121 && info.Normalized) { // GL_UNSIGNED_BYTE
                info.EstimatedBits = 8;
                info.UVScaleU = 1.0f;
                info.UVScaleV = 1.0f;
            }
        }

        /// <summary>
        /// Вычисляет масштаб для одной оси UV
        /// </summary>
        private static float CalculateAxisScale(float maxVal, out int estimatedBits) {
            // Если max около 1.0 - нормальные 16-бит UV
            if (maxVal > 0.9f) {
                estimatedBits = 16;
                return 1.0f;
            }
            // Если max около 0.0625 - это 12-бит квантование
            else if (maxVal < 0.1f && maxVal > 0.05f) {
                estimatedBits = 12;
                return 65535.0f / 4095.0f; // ~16
            }
            // Если max меньше - определяем по формуле
            else if (maxVal > 0.0f) {
                estimatedBits = (int)Math.Ceiling(Math.Log2((maxVal * 65535) + 1));
                estimatedBits = Math.Clamp(estimatedBits, 1, 16);
                int maxIntValue = (1 << estimatedBits) - 1;
                return 65535.0f / maxIntValue;
            }

            estimatedBits = 16;
            return 1.0f;
        }

        /// <summary>
        /// Логирует информацию о квантовании
        /// </summary>
        private static void LogQuantizationInfo(string glbPath, UVQuantizationInfo info) {
            var fileName = Path.GetFileName(glbPath);
            Logger.Info($"GLB Quantization Analysis: {fileName}");
            Logger.Info($"  KHR_mesh_quantization: {info.IsQuantized}");
            Logger.Info($"  EXT_meshopt_compression: {info.HasMeshOptCompression}");
            Logger.Info($"  UV componentType: {GetComponentTypeName(info.ComponentType)}");
            Logger.Info($"  UV normalized: {info.Normalized}");

            if (info.Min != null) {
                Logger.Info($"  UV min: [{string.Join(", ", info.Min.Select(v => v.ToString("F4")))}]");
            }
            if (info.Max != null) {
                Logger.Info($"  UV max: [{string.Join(", ", info.Max.Select(v => v.ToString("F4")))}]");
            }

            Logger.Info($"  Estimated bits: {info.EstimatedBits}");
            Logger.Info($"  UV scale correction: U={info.UVScaleU:F3}, V={info.UVScaleV:F3}");

            if (!string.IsNullOrEmpty(info.Issue)) {
                Logger.Warn($"  Issue: {info.Issue}");
            }
        }

        private static string GetComponentTypeName(int componentType) {
            return componentType switch {
                5120 => "BYTE",
                5121 => "UNSIGNED_BYTE",
                5122 => "SHORT",
                5123 => "UNSIGNED_SHORT",
                5125 => "UNSIGNED_INT",
                5126 => "FLOAT",
                _ => $"Unknown ({componentType})"
            };
        }

        /// <summary>
        /// Определяет нужно ли применять масштаб UV при загрузке
        /// </summary>
        public static bool NeedsUVScaling(UVQuantizationInfo info) {
            return info.UVScaleU > 1.001f || info.UVScaleU < 0.999f ||
                   info.UVScaleV > 1.001f || info.UVScaleV < 0.999f;
        }
    }
}

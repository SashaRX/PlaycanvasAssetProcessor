using System.IO;
using AssetProcessor.ModelConversion.Analysis;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Wrappers;
using NLog;

namespace AssetProcessor.ModelConversion.Pipeline {
    /// <summary>
    /// Главный пайплайн конвертации моделей FBX → GLB + LOD
    /// Объединяет FBX2glTF и gltfpack для генерации оптимизированных моделей с LOD цепочкой
    /// </summary>
    public class ModelConversionPipeline {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly FBX2glTFWrapper _fbx2glTFWrapper;
        private readonly GltfPackWrapper _gltfPackWrapper;
        private readonly LodManifestGenerator _manifestGenerator;

        public ModelConversionPipeline(
            string? fbx2glTFPath = null,
            string? gltfPackPath = null) {
            Logger.Info($"Initializing ModelConversionPipeline with FBX2glTF: {fbx2glTFPath ?? "default"}, gltfpack: {gltfPackPath ?? "default"}");
            _fbx2glTFWrapper = new FBX2glTFWrapper(fbx2glTFPath);
            _gltfPackWrapper = new GltfPackWrapper(gltfPackPath);
            _manifestGenerator = new LodManifestGenerator();
        }

        /// <summary>
        /// Конвертирует FBX/glTF/GLB в оптимизированные GLB с LOD цепочкой
        /// </summary>
        /// <param name="inputPath">Путь к FBX, glTF или GLB файлу</param>
        /// <param name="outputDirectory">Директория для выходных файлов</param>
        /// <param name="settings">Настройки конвертации</param>
        /// <returns>Результат конвертации</returns>
        public async Task<ModelConversionResult> ConvertAsync(
            string inputPath,
            string outputDirectory,
            ModelConversionSettings settings) {

            var result = new ModelConversionResult {
                InputPath = inputPath,
                OutputDirectory = outputDirectory
            };

            var startTime = DateTime.Now;

            try {
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] ConvertAsync START\n");
                Logger.Info($"=== MODEL CONVERSION PIPELINE START ===");
                Logger.Info($"Input: {inputPath}");
                Logger.Info($"Output: {outputDirectory}");
                Logger.Info($"Settings: Generate LODs={settings.GenerateLods}, Compression={settings.CompressionMode}");

                // Определяем формат входного файла
                var inputExtension = Path.GetExtension(inputPath).ToLowerInvariant();
                var isGltfInput = inputExtension is ".gltf" or ".glb";

                // Проверяем доступность инструментов
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Checking FBX2glTF availability\n");
                if (!isGltfInput && !await _fbx2glTFWrapper.IsAvailableAsync()) {
                    throw new Exception("FBX2glTF not available. Please install it or specify path.");
                }
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] FBX2glTF OK, checking gltfpack\n");

                if (!await _gltfPackWrapper.IsAvailableAsync()) {
                    throw new Exception("gltfpack not available. Please install meshoptimizer or specify path.");
                }
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] gltfpack OK\n");

                // Создаём директории
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Creating directories\n");
                Directory.CreateDirectory(outputDirectory);
                var buildDir = Path.Combine(outputDirectory, "build");
                Directory.CreateDirectory(buildDir);

                var modelName = Path.GetFileNameWithoutExtension(inputPath);
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Model name: {modelName}, isGltfInput: {isGltfInput}\n");

                string baseGltfPath;

                if (isGltfInput) {
                    // ШАГ A: Прямой glTF/GLB вход (из 3ds Max или другого DCC)
                    Logger.Info("=== STEP A: DIRECT glTF/GLB INPUT ===");
                    Logger.Info($"Input format: {inputExtension.ToUpper()} (skipping FBX2glTF conversion)");

                    // Используем входной файл напрямую
                    baseGltfPath = inputPath;
                    result.BaseGlbPath = inputPath;

                    // DEBUG: Проверяем UV
                    InspectGlbUV(inputPath, $"INPUT {inputExtension.ToUpper()} (direct)");
                } else {
                    // ШАГ A: FBX → базовый GLB (без сжатия)
                    File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Starting FBX2glTF conversion\n");
                    Logger.Info("=== STEP A: FBX → BASE GLB ===");
                    Logger.Info($"Exclude textures: {settings.ExcludeTextures} (textures will be processed separately)");
                    var baseGlbPathNoExt = Path.Combine(buildDir, modelName);
                    var fbxResult = await _fbx2glTFWrapper.ConvertToGlbAsync(inputPath, baseGlbPathNoExt, settings.ExcludeTextures);
                    File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] FBX2glTF done, Success: {fbxResult.Success}\n");

                    if (!fbxResult.Success) {
                        throw new Exception($"FBX2glTF conversion failed: {fbxResult.Error}");
                    }

                    Logger.Info($"Base GLB created: {fbxResult.OutputFilePath} ({fbxResult.OutputFileSize} bytes)");
                    baseGltfPath = fbxResult.OutputFilePath!;
                    result.BaseGlbPath = baseGltfPath;

                    // DEBUG: Проверяем UV после FBX2glTF
                    File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Calling InspectGlbUV\n");
                    InspectGlbUV(baseGltfPath, "BASE GLB (after FBX2glTF)");
                    File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] InspectGlbUV done\n");
                }

                // ШАГ B & C: Генерация LOD цепочки
                if (settings.GenerateLods) {
                    File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Starting LOD generation\n");
                    Logger.Info("=== STEP B & C: LOD GENERATION ===");

                    // Сохраняем LOD файлы напрямую в outputDirectory
                    var lodFiles = new Dictionary<LodLevel, string>();
                    var lodMetrics = new Dictionary<string, MeshMetrics>();

                    foreach (var lodSettings in settings.LodChain) {
                        var lodName = $"LOD{(int)lodSettings.Level}";
                        var lodFileName = $"{modelName}_lod{(int)lodSettings.Level}.glb";
                        var lodOutputPath = Path.Combine(outputDirectory, lodFileName);

                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Generating {lodName}\n");
                        Logger.Info($"  Generating {lodName}: simplification={lodSettings.SimplificationRatio:F2}, aggressive={lodSettings.AggressiveSimplification}");

                        // Используем gltfpack для симплификации
                        var gltfResult = await _gltfPackWrapper.OptimizeAsync(
                            baseGltfPath,
                            lodOutputPath,
                            lodSettings,
                            settings.CompressionMode,
                            settings.Quantization,
                            settings.AdvancedSettings,
                            generateReport: settings.GenerateQAReport,
                            excludeTextures: settings.ExcludeTextures
                        );
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] {lodName} done, Success: {gltfResult.Success}\n");

                        if (!gltfResult.Success) {
                            Logger.Error($"Failed to generate {lodName}: {gltfResult.Error}");
                            result.Errors.Add($"{lodName} generation failed: {gltfResult.Error}");
                            continue;
                        }

                        Logger.Info($"  {lodName} created: {gltfResult.OutputFileSize} bytes, {gltfResult.TriangleCount} tris, {gltfResult.VertexCount} verts");

                        // DEBUG: Проверяем UV после обработки
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Calling InspectGlbUV for {lodName}\n");
                        InspectGlbUV(lodOutputPath, $"{lodName} (after gltfpack)");
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] InspectGlbUV for {lodName} done\n");

                        lodFiles[lodSettings.Level] = lodOutputPath;

                        // Собираем метрики
                        lodMetrics[lodName] = new MeshMetrics {
                            TriangleCount = gltfResult.TriangleCount,
                            VertexCount = gltfResult.VertexCount,
                            FileSize = gltfResult.OutputFileSize,
                            SimplificationRatio = lodSettings.SimplificationRatio,
                            CompressionMode = settings.CompressionMode.ToString()
                        };
                    }

                    File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] LOD loop complete\n");
                    // Сохраняем LOD файлы и метрики в результат
                    result.LodFiles = lodFiles;
                    result.LodMetrics = lodMetrics;

                    // ШАГ D: Генерация манифеста
                    if (settings.GenerateManifest && lodFiles.Count > 0) {
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Generating manifest\n");
                        Logger.Info($"=== STEP D: MANIFEST GENERATION ===");

                        var manifestPath = _manifestGenerator.GenerateManifest(
                            modelName,
                            lodFiles,
                            settings,
                            outputDirectory
                        );

                        Logger.Info($"Manifest created: {manifestPath}");
                        result.ManifestPath = manifestPath;
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Manifest done\n");
                    }

                    // ШАГ E: QA отчёт
                    if (settings.GenerateQAReport && lodMetrics.Count > 0) {
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Generating QA report\n");
                        Logger.Info($"=== STEP E: QA REPORT ===");

                        var qaReport = new QualityReport {
                            ModelName = modelName,
                            LodMetrics = lodMetrics
                        };

                        // Оцениваем критерии приёмки
                        qaReport.EvaluateAcceptanceCriteria(settings);

                        // Сохраняем отчёт в outputDirectory
                        var reportPath = Path.Combine(outputDirectory, $"{modelName}_qa_report.json");
                        qaReport.SaveToFile(reportPath);

                        Logger.Info($"QA Report saved: {reportPath}");
                        Logger.Info(qaReport.ToTextReport());

                        result.QAReport = qaReport;
                        result.QAReportPath = reportPath;

                        // Добавляем warnings/errors из отчёта
                        result.Warnings.AddRange(qaReport.Warnings);
                        result.Errors.AddRange(qaReport.Errors);
                        File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] QA report done\n");
                    }
                }

                // Cleanup промежуточных файлов
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] Cleanup phase\n");
                if (settings.CleanupIntermediateFiles) {
                    Logger.Info("=== CLEANUP INTERMEDIATE FILES ===");
                    try {
                        // КРИТИЧНО: Удаляем только buildDir, который содержит все промежуточные файлы
                        // НЕ удаляем файлы из input directory - это может удалить пользовательские текстуры!
                        // FBX2glTF создаёт текстуры в buildDir или его поддиректориях, которые удаляются вместе с buildDir
                        if (Directory.Exists(buildDir)) {
                            Directory.Delete(buildDir, recursive: true);
                            Logger.Info($"Deleted build directory: {buildDir}");
                        }

                        // ПРИМЕЧАНИЕ: Удалён опасный код, который искал текстуры в input directory
                        // Старый код мог удалить пользовательские файлы с именами типа "model_basecolor.png"
                        // Все промежуточные текстуры уже удаляются вместе с buildDir (рекурсивное удаление)
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to cleanup build directory: {ex.Message}");
                    }
                }

                result.Success = result.Errors.Count == 0;
                result.Duration = DateTime.Now - startTime;

                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] ConvertAsync COMPLETE, Success: {result.Success}\n");
                Logger.Info($"=== MODEL CONVERSION COMPLETE ===");
                Logger.Info($"Success: {result.Success}");
                Logger.Info($"Duration: {result.Duration.TotalSeconds:F2}s");
                Logger.Info($"LOD files: {result.LodFiles.Count}");

            } catch (Exception ex) {
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] EXCEPTION: {ex.Message}\n");
                Logger.Error(ex, "Model conversion failed");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Duration = DateTime.Now - startTime;
            }

            File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [PIPELINE] About to return result\n");
            return result;
        }

        /// <summary>
        /// Проверяет UV координаты в GLB файле для диагностики
        /// </summary>
        private void InspectGlbUV(string glbPath, string stage) {
            try {
                Logger.Info($"=== UV INSPECTION: {stage} ===");
                Logger.Info($"File: {glbPath}");

                // Читаем GLB файл
                using var fileStream = File.OpenRead(glbPath);
                using var reader = new BinaryReader(fileStream);

                // Читаем GLB header (12 bytes)
                var magic = reader.ReadUInt32();  // должно быть 0x46546C67 ('glTF')
                var version = reader.ReadUInt32();
                var length = reader.ReadUInt32();

                // Читаем JSON chunk header (8 bytes)
                var jsonLength = reader.ReadUInt32();
                var jsonType = reader.ReadUInt32();  // должно быть 0x4E4F534A ('JSON')

                // Читаем JSON content
                var jsonBytes = reader.ReadBytes((int)jsonLength);
                var jsonText = System.Text.Encoding.UTF8.GetString(jsonBytes);

                // Парсим JSON
                var gltf = System.Text.Json.JsonDocument.Parse(jsonText);

                // Проверяем наличие TEXCOORD в примитивах
                bool foundTexcoord = false;
                if (gltf.RootElement.TryGetProperty("meshes", out var meshes)) {
                    int meshIndex = 0;
                    foreach (var mesh in meshes.EnumerateArray()) {
                        if (mesh.TryGetProperty("primitives", out var primitives)) {
                            int primIndex = 0;
                            foreach (var prim in primitives.EnumerateArray()) {
                                if (prim.TryGetProperty("attributes", out var attrs)) {
                                    if (attrs.TryGetProperty("TEXCOORD_0", out var texcoord)) {
                                        var accessorIdx = texcoord.GetInt32();
                                        Logger.Info($"  Mesh {meshIndex} Primitive {primIndex}: TEXCOORD_0 found (accessor {accessorIdx})");

                                        // Читаем информацию об accessor
                                        if (gltf.RootElement.TryGetProperty("accessors", out var accessors)) {
                                            var accessor = accessors[accessorIdx];
                                            var componentType = accessor.GetProperty("componentType").GetInt32();
                                            var count = accessor.GetProperty("count").GetInt32();
                                            var normalized = accessor.TryGetProperty("normalized", out var n) ? n.GetBoolean() : false;

                                            string compTypeName = componentType switch {
                                                5126 => "FLOAT",
                                                5123 => "UNSIGNED_SHORT",
                                                _ => componentType.ToString()
                                            };

                                            Logger.Info($"    Component Type: {compTypeName} ({componentType}), Count: {count}, Normalized: {normalized}");

                                            // Проверяем min/max если есть
                                            if (accessor.TryGetProperty("min", out var min) && accessor.TryGetProperty("max", out var max)) {
                                                var minU = min[0].GetDouble();
                                                var minV = min[1].GetDouble();
                                                var maxU = max[0].GetDouble();
                                                var maxV = max[1].GetDouble();
                                                Logger.Info($"    UV Range: U=[{minU:F4}, {maxU:F4}], V=[{minV:F4}, {maxV:F4}]");
                                            } else {
                                                Logger.Warn("    No min/max data for UV accessor");
                                            }
                                        }

                                        foundTexcoord = true;
                                    }
                                }
                                primIndex++;
                            }
                        }
                        meshIndex++;
                    }
                }

                if (!foundTexcoord) {
                    Logger.Warn("  NO TEXCOORD_0 found in any mesh primitive!");
                }

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to inspect UV in {glbPath}");
            }
        }

        /// <summary>
        /// Проверяет доступность всех необходимых инструментов
        /// </summary>
        public async Task<ToolsAvailability> CheckToolsAsync() {
            var availability = new ToolsAvailability();

            try {
                availability.FBX2glTFAvailable = await _fbx2glTFWrapper.IsAvailableAsync();
                availability.GltfPackAvailable = await _gltfPackWrapper.IsAvailableAsync();
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to check tools availability");
            }

            return availability;
        }
    }

    /// <summary>
    /// Результат конвертации модели
    /// </summary>
    public class ModelConversionResult {
        public bool Success { get; set; }
        public string InputPath { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;
        public string? BaseGlbPath { get; set; }
        public Dictionary<LodLevel, string> LodFiles { get; set; } = new();
        public Dictionary<string, MeshMetrics> LodMetrics { get; set; } = new();
        public string? ManifestPath { get; set; }
        public string? QAReportPath { get; set; }
        public QualityReport? QAReport { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Информация о доступности инструментов
    /// </summary>
    public class ToolsAvailability {
        public bool FBX2glTFAvailable { get; set; }
        public bool GltfPackAvailable { get; set; }
        public bool AllAvailable => FBX2glTFAvailable && GltfPackAvailable;
    }
}

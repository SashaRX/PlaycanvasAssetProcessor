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
        /// Конвертирует FBX в оптимизированные GLB с LOD цепочкой
        /// </summary>
        /// <param name="inputFbxPath">Путь к FBX файлу</param>
        /// <param name="outputDirectory">Директория для выходных файлов</param>
        /// <param name="settings">Настройки конвертации</param>
        /// <returns>Результат конвертации</returns>
        public async Task<ModelConversionResult> ConvertAsync(
            string inputFbxPath,
            string outputDirectory,
            ModelConversionSettings settings) {

            var result = new ModelConversionResult {
                InputPath = inputFbxPath,
                OutputDirectory = outputDirectory
            };

            var startTime = DateTime.Now;

            try {
                Logger.Info($"=== MODEL CONVERSION PIPELINE START ===");
                Logger.Info($"Input: {inputFbxPath}");
                Logger.Info($"Output: {outputDirectory}");
                Logger.Info($"Settings: Generate LODs={settings.GenerateLods}, Compression={settings.CompressionMode}");

                // Проверяем доступность инструментов
                if (!await _fbx2glTFWrapper.IsAvailableAsync()) {
                    throw new Exception("FBX2glTF not available. Please install it or specify path.");
                }

                if (!await _gltfPackWrapper.IsAvailableAsync()) {
                    throw new Exception("gltfpack not available. Please install meshoptimizer or specify path.");
                }

                // Создаём директории
                Directory.CreateDirectory(outputDirectory);
                var buildDir = Path.Combine(outputDirectory, "build");
                Directory.CreateDirectory(buildDir);

                var modelName = Path.GetFileNameWithoutExtension(inputFbxPath);

                // ШАГ A: FBX → базовый GLB (без сжатия)
                Logger.Info("=== STEP A: FBX → BASE GLB ===");
                Logger.Info($"Exclude textures: {settings.ExcludeTextures} (textures will be processed separately)");
                var baseGlbPath = Path.Combine(buildDir, modelName);
                var fbxResult = await _fbx2glTFWrapper.ConvertToGlbAsync(inputFbxPath, baseGlbPath, settings.ExcludeTextures);

                if (!fbxResult.Success) {
                    throw new Exception($"FBX2glTF conversion failed: {fbxResult.Error}");
                }

                Logger.Info($"Base GLB created: {fbxResult.OutputFilePath} ({fbxResult.OutputFileSize} bytes)");
                result.BaseGlbPath = fbxResult.OutputFilePath!;

                // DEBUG: Проверяем UV после FBX2glTF
                InspectGlbUV(fbxResult.OutputFilePath!, "BASE GLB (after FBX2glTF)");

                // ШАГ B & C: Генерация LOD цепочки
                if (settings.GenerateLods) {
                    Logger.Info("=== STEP B & C: LOD GENERATION ===");

                    // Сохраняем LOD файлы напрямую в outputDirectory
                    var lodFiles = new Dictionary<LodLevel, string>();
                    var lodMetrics = new Dictionary<string, MeshMetrics>();

                    foreach (var lodSettings in settings.LodChain) {
                        var lodName = $"LOD{(int)lodSettings.Level}";
                        var lodFileName = $"{modelName}_lod{(int)lodSettings.Level}.glb";
                        var lodOutputPath = Path.Combine(outputDirectory, lodFileName);

                        Logger.Info($"  Generating {lodName}: simplification={lodSettings.SimplificationRatio:F2}, aggressive={lodSettings.AggressiveSimplification}");

                        var gltfResult = await _gltfPackWrapper.OptimizeAsync(
                            fbxResult.OutputFilePath!,
                            lodOutputPath,
                            lodSettings,
                            settings.CompressionMode,
                            settings.Quantization,
                            generateReport: settings.GenerateQAReport,
                            excludeTextures: settings.ExcludeTextures
                        );

                        if (!gltfResult.Success) {
                            Logger.Error($"Failed to generate {lodName}: {gltfResult.Error}");
                            result.Errors.Add($"{lodName} generation failed: {gltfResult.Error}");
                            continue;
                        }

                        Logger.Info($"  {lodName} created: {gltfResult.OutputFileSize} bytes, {gltfResult.TriangleCount} tris, {gltfResult.VertexCount} verts");

                        // DEBUG: Проверяем UV после gltfpack
                        InspectGlbUV(lodOutputPath, $"{lodName} (after gltfpack)");

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

                    // Сохраняем LOD файлы и метрики в результат
                    result.LodFiles = lodFiles;
                    result.LodMetrics = lodMetrics;

                    // ШАГ D: Генерация манифеста
                    if (settings.GenerateManifest && lodFiles.Count > 0) {
                        Logger.Info($"=== STEP D: MANIFEST GENERATION ===");

                        var manifestPath = _manifestGenerator.GenerateManifest(
                            modelName,
                            lodFiles,
                            settings,
                            outputDirectory
                        );

                        Logger.Info($"Manifest created: {manifestPath}");
                        result.ManifestPath = manifestPath;
                    }

                    // ШАГ E: QA отчёт
                    if (settings.GenerateQAReport && lodMetrics.Count > 0) {
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
                    }
                }

                // Cleanup промежуточных файлов
                if (settings.CleanupIntermediateFiles) {
                    Logger.Info("=== CLEANUP INTERMEDIATE FILES ===");
                    try {
                        if (Directory.Exists(buildDir)) {
                            Directory.Delete(buildDir, recursive: true);
                            Logger.Info($"Deleted build directory: {buildDir}");
                        }
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to cleanup build directory: {ex.Message}");
                    }
                }

                result.Success = result.Errors.Count == 0;
                result.Duration = DateTime.Now - startTime;

                Logger.Info($"=== MODEL CONVERSION COMPLETE ===");
                Logger.Info($"Success: {result.Success}");
                Logger.Info($"Duration: {result.Duration.TotalSeconds:F2}s");
                Logger.Info($"LOD files: {result.LodFiles.Count}");

            } catch (Exception ex) {
                Logger.Error(ex, "Model conversion failed");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Duration = DateTime.Now - startTime;
            }

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

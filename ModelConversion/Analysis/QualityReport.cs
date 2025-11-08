using System.IO;
using System.Text.Json;
using AssetProcessor.ModelConversion.Core;

namespace AssetProcessor.ModelConversion.Analysis {
    /// <summary>
    /// QA отчет для конвертации модели
    /// </summary>
    public class QualityReport {
        /// <summary>
        /// Имя модели
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// Дата/время генерации отчета
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Метрики для каждого LOD уровня
        /// </summary>
        public Dictionary<string, MeshMetrics> LodMetrics { get; set; } = new();

        /// <summary>
        /// Критерии приёмки (Pass/Fail)
        /// </summary>
        public Dictionary<string, AcceptanceCriteria> AcceptanceTests { get; set; } = new();

        /// <summary>
        /// Все критерии пройдены?
        /// </summary>
        public bool AllTestsPassed => AcceptanceTests.All(t => t.Value.Passed);

        /// <summary>
        /// Предупреждения
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Ошибки
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Добавляет критерий приёмки
        /// </summary>
        public void AddAcceptanceTest(string name, bool passed, string? message = null) {
            AcceptanceTests[name] = new AcceptanceCriteria {
                Name = name,
                Passed = passed,
                Message = message ?? (passed ? "OK" : "FAILED")
            };
        }

        /// <summary>
        /// Проверяет критерии приёмки согласно требованиям из промпта
        /// </summary>
        public void EvaluateAcceptanceCriteria(ModelConversionSettings settings) {
            // 1. LOD-цепочка сгенерирована корректно
            var expectedLods = new[] { "LOD0", "LOD1", "LOD2", "LOD3" };
            var hasFourLods = expectedLods.All(lod => LodMetrics.ContainsKey(lod));
            AddAcceptanceTest(
                "LOD Chain Generated",
                hasFourLods,
                hasFourLods ? "All 4 LOD levels present" : $"Missing LOD levels: {string.Join(", ", expectedLods.Where(lod => !LodMetrics.ContainsKey(lod)))}"
            );

            // 2. LOD1 размер <= 60% от LOD0
            if (LodMetrics.ContainsKey("LOD0") && LodMetrics.ContainsKey("LOD1")) {
                var lod0Size = LodMetrics["LOD0"].FileSize;
                var lod1Size = LodMetrics["LOD1"].FileSize;
                var ratio = lod0Size > 0 ? (float)lod1Size / lod0Size : 0;
                var passed = ratio <= 0.60f;
                AddAcceptanceTest(
                    "LOD1 Size Reduction",
                    passed,
                    $"LOD1 is {ratio:P1} of LOD0 size {(passed ? "(OK)" : "(FAILED: should be ≤60%)")}"
                );
            }

            // 3. Проверка упрощения геометрии
            if (LodMetrics.ContainsKey("LOD0")) {
                var lod0Tris = LodMetrics["LOD0"].TriangleCount;

                foreach (var lodPair in LodMetrics.Where(p => p.Key != "LOD0")) {
                    var lodName = lodPair.Key;
                    var lodTris = lodPair.Value.TriangleCount;
                    var expectedRatio = lodPair.Value.SimplificationRatio;

                    if (lod0Tris > 0) {
                        var actualRatio = (float)lodTris / lod0Tris;
                        // Даём небольшой допуск (±10%)
                        var tolerance = 0.1f;
                        var passed = Math.Abs(actualRatio - expectedRatio) <= tolerance;

                        AddAcceptanceTest(
                            $"{lodName} Triangle Count",
                            passed,
                            $"{lodName}: {lodTris} tris ({actualRatio:P1} of LOD0), expected ~{expectedRatio:P1}"
                        );
                    }
                }
            }

            // 4. Bounding box consistency
            if (LodMetrics.Count > 1) {
                var bboxConsistent = CheckBoundingBoxConsistency();
                AddAcceptanceTest(
                    "Bounding Box Consistency",
                    bboxConsistent,
                    bboxConsistent ? "All LODs have similar bbox" : "LOD bboxes differ significantly"
                );
            }

            // 5. Файлы существуют
            var allFilesExist = LodMetrics.All(m => m.Value.FileSize > 0);
            AddAcceptanceTest(
                "Output Files Exist",
                allFilesExist,
                allFilesExist ? "All GLB files generated" : "Some GLB files missing"
            );
        }

        /// <summary>
        /// Проверяет консистентность bounding box между LOD уровнями
        /// </summary>
        private bool CheckBoundingBoxConsistency() {
            if (LodMetrics.Count == 0) return true;

            var firstBbox = LodMetrics.First().Value;
            if (firstBbox.BboxMin == null || firstBbox.BboxMax == null) return true;

            foreach (var metric in LodMetrics.Values) {
                if (metric.BboxMin == null || metric.BboxMax == null) continue;

                // Проверяем что разница в bbox < 10%
                for (int i = 0; i < 3; i++) {
                    var diff = Math.Abs(metric.BboxMin[i] - firstBbox.BboxMin[i]);
                    var maxDiff = Math.Abs(firstBbox.BboxMax[i] - firstBbox.BboxMin[i]) * 0.1f;
                    if (diff > maxDiff) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Сохраняет отчет в JSON файл
        /// </summary>
        public void SaveToFile(string filePath) {
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Загружает отчет из JSON файла
        /// </summary>
        public static QualityReport? LoadFromFile(string filePath) {
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Deserialize<QualityReport>(json, options);
        }

        /// <summary>
        /// Генерирует текстовый отчет для консоли
        /// </summary>
        public string ToTextReport() {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== QA REPORT: {ModelName} ===");
            sb.AppendLine($"Generated: {GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("LOD Metrics:");
            foreach (var kvp in LodMetrics.OrderBy(k => k.Key)) {
                var lod = kvp.Key;
                var metrics = kvp.Value;
                sb.AppendLine($"  {lod}:");
                sb.AppendLine($"    Triangles: {metrics.TriangleCount:N0}");
                sb.AppendLine($"    Vertices: {metrics.VertexCount:N0}");
                sb.AppendLine($"    File Size: {metrics.FileSize:N0} bytes ({metrics.FileSize / 1024.0:F2} KB)");
                if (metrics.BboxMin != null && metrics.BboxMax != null) {
                    sb.AppendLine($"    BBox: [{string.Join(", ", metrics.BboxMin.Select(v => v.ToString("F2")))}] - [{string.Join(", ", metrics.BboxMax.Select(v => v.ToString("F2")))}]");
                }
            }
            sb.AppendLine();

            sb.AppendLine("Acceptance Criteria:");
            foreach (var test in AcceptanceTests.Values) {
                var status = test.Passed ? "✓ PASS" : "✗ FAIL";
                sb.AppendLine($"  {status}: {test.Name} - {test.Message}");
            }
            sb.AppendLine();

            if (Warnings.Any()) {
                sb.AppendLine("Warnings:");
                foreach (var warning in Warnings) {
                    sb.AppendLine($"  ⚠ {warning}");
                }
                sb.AppendLine();
            }

            if (Errors.Any()) {
                sb.AppendLine("Errors:");
                foreach (var error in Errors) {
                    sb.AppendLine($"  ✗ {error}");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"Overall Result: {(AllTestsPassed ? "✓ PASSED" : "✗ FAILED")}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Критерий приёмки
    /// </summary>
    public class AcceptanceCriteria {
        public string Name { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

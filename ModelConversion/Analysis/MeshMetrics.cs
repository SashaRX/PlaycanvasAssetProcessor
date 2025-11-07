using System.Text.Json;

namespace AssetProcessor.ModelConversion.Analysis {
    /// <summary>
    /// Метрики геометрии модели
    /// </summary>
    public class MeshMetrics {
        /// <summary>
        /// Количество треугольников
        /// </summary>
        public int TriangleCount { get; set; }

        /// <summary>
        /// Количество вершин
        /// </summary>
        public int VertexCount { get; set; }

        /// <summary>
        /// Размер файла (байты)
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Bounding box (минимальные координаты)
        /// </summary>
        public float[]? BboxMin { get; set; }

        /// <summary>
        /// Bounding box (максимальные координаты)
        /// </summary>
        public float[]? BboxMax { get; set; }

        /// <summary>
        /// Степень упрощения геометрии (0.0-1.0)
        /// </summary>
        public float SimplificationRatio { get; set; }

        /// <summary>
        /// Режим сжатия
        /// </summary>
        public string? CompressionMode { get; set; }

        /// <summary>
        /// Дополнительные метаданные
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Сравнивает с другими метриками и возвращает процентное изменение
        /// </summary>
        public MetricsDelta CompareTo(MeshMetrics other) {
            return new MetricsDelta {
                TriangleReduction = CalculatePercentage(other.TriangleCount, TriangleCount),
                VertexReduction = CalculatePercentage(other.VertexCount, VertexCount),
                FileSizeReduction = CalculatePercentage(other.FileSize, FileSize)
            };
        }

        private float CalculatePercentage(long original, long current) {
            if (original == 0) return 0;
            return ((float)(original - current) / original) * 100f;
        }

        /// <summary>
        /// Сериализует метрики в JSON
        /// </summary>
        public string ToJson() {
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Serialize(this, options);
        }

        /// <summary>
        /// Загружает метрики из JSON
        /// </summary>
        public static MeshMetrics? FromJson(string json) {
            var options = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Deserialize<MeshMetrics>(json, options);
        }
    }

    /// <summary>
    /// Изменение метрик между двумя уровнями LOD
    /// </summary>
    public class MetricsDelta {
        /// <summary>
        /// Уменьшение треугольников (%)
        /// </summary>
        public float TriangleReduction { get; set; }

        /// <summary>
        /// Уменьшение вершин (%)
        /// </summary>
        public float VertexReduction { get; set; }

        /// <summary>
        /// Уменьшение размера файла (%)
        /// </summary>
        public float FileSizeReduction { get; set; }
    }
}

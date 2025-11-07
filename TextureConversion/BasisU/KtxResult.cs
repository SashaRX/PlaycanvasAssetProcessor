namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Результат выполнения ktx команды (create, encode, deflate, etc.)
    /// </summary>
    public class KtxResult {
        public bool Success { get; set; }
        public string? OutputPath { get; set; }
        public string? Error { get; set; }
        public string? Output { get; set; }
        public string? ErrorOutput { get; set; }
        public long OutputFileSize { get; set; }
    }
}

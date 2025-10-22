namespace AssetProcessor.Exceptions {
    /// <summary>
    /// Exception thrown when file integrity check fails (e.g., MD5 hash mismatch)
    /// </summary>
    public class FileIntegrityException : Exception {
        public string? FilePath { get; }
        public string? ExpectedHash { get; }
        public string? ActualHash { get; }

        public FileIntegrityException() { }

        public FileIntegrityException(string message) : base(message) { }

        public FileIntegrityException(string message, Exception innerException)
            : base(message, innerException) { }

        public FileIntegrityException(
            string message,
            string? filePath,
            string? expectedHash = null,
            string? actualHash = null)
            : base(message) {
            FilePath = filePath;
            ExpectedHash = expectedHash;
            ActualHash = actualHash;
        }

        public override string ToString() {
            var baseMessage = base.ToString();
            if (FilePath != null) {
                baseMessage += $"\nFile Path: {FilePath}";
            }
            if (ExpectedHash != null) {
                baseMessage += $"\nExpected Hash: {ExpectedHash}";
            }
            if (ActualHash != null) {
                baseMessage += $"\nActual Hash: {ActualHash}";
            }
            return baseMessage;
        }
    }
}

namespace AssetProcessor.Helpers;

/// <summary>
/// Универсальный санитайзер для строковых путей.
/// </summary>
public static class PathSanitizer {
    /// <summary>
    /// Удаляет символы новой строки и лишние пробелы из пути.
    /// </summary>
    public static string SanitizePath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        return path
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();
    }
}

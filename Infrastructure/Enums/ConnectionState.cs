namespace AssetProcessor.Infrastructure.Enums;

/// <summary>
/// Состояние подключения к PlayCanvas.
/// </summary>
public enum ConnectionState {
    /// <summary>
    /// Не подключены к PlayCanvas.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Список ассетов актуален, можно обновлять.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Требуется загрузка ассетов.
    /// </summary>
    NeedsDownload,
}

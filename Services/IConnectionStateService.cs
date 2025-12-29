using AssetProcessor.Infrastructure.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

/// <summary>
/// Сервис управления состоянием подключения к PlayCanvas.
/// Управляет переходами между состояниями Disconnected → NeedsDownload → UpToDate.
/// </summary>
public interface IConnectionStateService {
    /// <summary>
    /// Текущее состояние подключения.
    /// </summary>
    ConnectionState CurrentState { get; }

    /// <summary>
    /// Событие изменения состояния подключения.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Устанавливает новое состояние подключения.
    /// </summary>
    void SetState(ConnectionState newState);

    /// <summary>
    /// Проверяет наличие обновлений на сервере.
    /// </summary>
    /// <param name="projectFolderPath">Путь к папке проекта.</param>
    /// <param name="projectId">ID проекта PlayCanvas.</param>
    /// <param name="branchId">ID ветки.</param>
    /// <param name="apiKey">API ключ.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>True если есть обновления.</returns>
    Task<bool> CheckForUpdatesAsync(string projectFolderPath, string projectId, string branchId, string apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Проверяет наличие отсутствующих файлов в локальном проекте.
    /// </summary>
    /// <param name="textures">Коллекция текстур для проверки.</param>
    /// <param name="models">Коллекция моделей для проверки.</param>
    /// <param name="materials">Коллекция материалов для проверки.</param>
    /// <returns>True если есть отсутствующие файлы.</returns>
    bool HasMissingFiles<TTexture, TModel, TMaterial>(
        System.Collections.Generic.IEnumerable<TTexture> textures,
        System.Collections.Generic.IEnumerable<TModel> models,
        System.Collections.Generic.IEnumerable<TMaterial> materials)
        where TTexture : Resources.BaseResource
        where TModel : Resources.BaseResource
        where TMaterial : Resources.BaseResource;

    /// <summary>
    /// Определяет состояние на основе наличия обновлений и отсутствующих файлов.
    /// </summary>
    ConnectionState DetermineState(bool hasUpdates, bool hasMissingFiles);

    /// <summary>
    /// Получает информацию для UI кнопки на основе текущего состояния.
    /// </summary>
    ConnectionButtonInfo GetButtonInfo(bool hasProjectSelection);
}

/// <summary>
/// Аргументы события изменения состояния подключения.
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs {
    public ConnectionState OldState { get; }
    public ConnectionState NewState { get; }

    public ConnectionStateChangedEventArgs(ConnectionState oldState, ConnectionState newState) {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// Информация для отображения кнопки подключения.
/// </summary>
public class ConnectionButtonInfo {
    public string Content { get; init; } = "Connect";
    public string ToolTip { get; init; } = "";
    public bool IsEnabled { get; init; } = true;
    public byte ColorR { get; init; } = 240;
    public byte ColorG { get; init; } = 240;
    public byte ColorB { get; init; } = 240;
}

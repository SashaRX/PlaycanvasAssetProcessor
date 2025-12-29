using AssetProcessor.Resources;
using System;
using System.Collections.Generic;

namespace AssetProcessor.Services;

/// <summary>
/// Сервис для сканирования и обновления статусов файлов ассетов.
/// </summary>
public interface IFileStatusScannerService {
    /// <summary>
    /// Событие при обнаружении удалённых файлов.
    /// </summary>
    event EventHandler<FilesDeletedEventArgs>? FilesDeleted;

    /// <summary>
    /// Сканирует текстуры и обновляет статусы для отсутствующих файлов.
    /// </summary>
    /// <param name="textures">Коллекция текстур для сканирования.</param>
    /// <returns>Результат сканирования.</returns>
    ScanResult ScanTextures(IEnumerable<TextureResource> textures);

    /// <summary>
    /// Сканирует модели и обновляет статусы для отсутствующих файлов.
    /// </summary>
    /// <param name="models">Коллекция моделей для сканирования.</param>
    /// <returns>Результат сканирования.</returns>
    ScanResult ScanModels(IEnumerable<ModelResource> models);

    /// <summary>
    /// Сканирует материалы и обновляет статусы для отсутствующих файлов.
    /// </summary>
    /// <param name="materials">Коллекция материалов для сканирования.</param>
    /// <returns>Результат сканирования.</returns>
    ScanResult ScanMaterials(IEnumerable<MaterialResource> materials);

    /// <summary>
    /// Сканирует все ассеты (текстуры, модели и материалы).
    /// </summary>
    /// <param name="textures">Коллекция текстур.</param>
    /// <param name="models">Коллекция моделей.</param>
    /// <param name="materials">Коллекция материалов.</param>
    /// <returns>Общий результат сканирования.</returns>
    ScanResult ScanAll(IEnumerable<TextureResource> textures, IEnumerable<ModelResource> models, IEnumerable<MaterialResource> materials);

    /// <summary>
    /// Обрабатывает список удалённых путей и обновляет статусы соответствующих ассетов.
    /// </summary>
    /// <param name="deletedPaths">Пути удалённых файлов.</param>
    /// <param name="textures">Коллекция текстур.</param>
    /// <param name="models">Коллекция моделей.</param>
    /// <param name="materials">Коллекция материалов.</param>
    /// <returns>Количество обновлённых ассетов.</returns>
    int ProcessDeletedPaths(IEnumerable<string> deletedPaths, IEnumerable<TextureResource> textures, IEnumerable<ModelResource> models, IEnumerable<MaterialResource> materials);

    /// <summary>
    /// Проверяет, является ли статус локальным (файл должен существовать).
    /// </summary>
    /// <param name="status">Статус для проверки.</param>
    /// <returns>True если статус указывает на локальный файл.</returns>
    bool IsLocalStatus(string? status);
}

/// <summary>
/// Результат сканирования файлов.
/// </summary>
public class ScanResult {
    public int CheckedCount { get; init; }
    public int MissingFilesCount { get; init; }
    public int UpdatedCount { get; init; }
    public List<string> UpdatedAssetNames { get; init; } = new();
}

/// <summary>
/// Аргументы события удаления файлов.
/// </summary>
public class FilesDeletedEventArgs : EventArgs {
    public IReadOnlyList<string> DeletedPaths { get; init; } = Array.Empty<string>();
    public int UpdatedAssetsCount { get; init; }
}

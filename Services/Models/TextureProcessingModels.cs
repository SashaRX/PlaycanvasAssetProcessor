using AssetProcessor.Resources;
using AssetProcessor.TextureViewer;

namespace AssetProcessor.Services.Models;

/// <summary>
/// Запрос на пакетную обработку текстур.
/// </summary>
public sealed class TextureProcessingRequest {
    public required IReadOnlyCollection<TextureResource> Textures { get; init; }

    public required ITextureConversionSettingsProvider SettingsProvider { get; init; }

    public TextureResource? SelectedTexture { get; init; }
}

/// <summary>
/// Результат пакетной обработки текстур.
/// </summary>
public sealed class TextureProcessingResult {
    public required int SuccessCount { get; init; }

    public required int ErrorCount { get; init; }

    public required IReadOnlyList<string> ErrorMessages { get; init; }

    /// <summary>
    /// Текстура, для которой требуется обновить превью после конвертации.
    /// </summary>
    public TextureResource? PreviewTexture { get; init; }

    /// <summary>
    /// Путь к сгенерированному файлу KTX2 (если доступен).
    /// </summary>
    public string? PreviewTexturePath { get; init; }
}

/// <summary>
/// Результат загрузки превью KTX2.
/// </summary>
public sealed class TexturePreviewResult {
    public required string KtxPath { get; init; }

    public required TextureData TextureData { get; init; }

    public required bool ShouldEnableNormalReconstruction { get; init; }

    public string? AutoEnableReason { get; init; }
}

/// <summary>
/// Результат массового автоопределения пресетов.
/// </summary>
public sealed class TextureAutoDetectResult {
    public required int MatchedCount { get; init; }

    public required int NotMatchedCount { get; init; }
}

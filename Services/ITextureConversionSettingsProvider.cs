using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Services;

/// <summary>
/// Интерфейс для поставщика настроек конвертации текстур.
/// Реализуется визуальной панелью настроек и используется сервисами.
/// </summary>
public interface ITextureConversionSettingsProvider {
    CompressionSettingsData GetCompressionSettings();

    HistogramSettings? GetHistogramSettings();

    bool SaveSeparateMipmaps { get; }

    ToksvigSettings GetToksvigSettings(string texturePath);

    string? PresetName { get; }
}

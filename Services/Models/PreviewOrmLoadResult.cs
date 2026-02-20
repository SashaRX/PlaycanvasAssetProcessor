namespace AssetProcessor.Services.Models;

public sealed class PreviewOrmLoadResult {
    public bool Loaded { get; init; }
    public bool ShouldExtractHistogram { get; init; }
    public bool WasCancelled { get; init; }
}

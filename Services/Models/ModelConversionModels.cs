using AssetProcessor.ModelConversion.Pipeline;

namespace AssetProcessor.Services.Models {
    public record ModelConversionServiceResult(
        bool Success,
        string Message,
        ModelConversionResult? Result);
}

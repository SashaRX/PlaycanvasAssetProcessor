using AssetProcessor.ModelConversion.Pipeline;

namespace AssetProcessor.ViewModels {
    public enum NotificationKind {
        Info,
        Warning,
        Error
    }

    public record UserNotification(NotificationKind Kind, string Title, string Message);

    public record ModelConversionUiResult(
        bool Success,
        string Message,
        ModelConversionResult? Result);
}

using NLog;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Centralized helper for executing async operations from WPF event handlers.
    /// Provides uniform error handling, logging, and optional user notification.
    ///
    /// Usage in event handlers:
    /// <code>
    /// private async void Button_Click(object sender, RoutedEventArgs e) {
    ///     await UiAsyncHelper.ExecuteAsync(
    ///         () => DoWorkAsync(),
    ///         "Button_Click");
    /// }
    /// </code>
    /// </summary>
    public static class UiAsyncHelper {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Executes an async operation with centralized error handling.
        /// Logs errors and optionally shows a MessageBox to the user.
        /// </summary>
        /// <param name="action">The async operation to execute.</param>
        /// <param name="operationName">Name for logging context (e.g. method or handler name).</param>
        /// <param name="showMessageBox">If true, shows a MessageBox on error. Default: false.</param>
        public static async Task ExecuteAsync(Func<Task> action, string operationName, bool showMessageBox = false) {
            try {
                await action();
            } catch (OperationCanceledException) {
                logger.Info($"{operationName}: Operation cancelled");
            } catch (Exception ex) {
                logger.Error(ex, $"{operationName}: Unhandled exception");
                if (showMessageBox) {
                    MessageBox.Show(
                        $"Error: {ex.Message}",
                        operationName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Executes an async operation with centralized error handling and a custom error callback.
        /// </summary>
        /// <param name="action">The async operation to execute.</param>
        /// <param name="operationName">Name for logging context.</param>
        /// <param name="onError">Custom error handler invoked on the calling (UI) thread.</param>
        public static async Task ExecuteAsync(Func<Task> action, string operationName, Action<Exception> onError) {
            try {
                await action();
            } catch (OperationCanceledException) {
                logger.Info($"{operationName}: Operation cancelled");
            } catch (Exception ex) {
                logger.Error(ex, $"{operationName}: Unhandled exception");
                onError(ex);
            }
        }
    }
}

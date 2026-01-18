using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Helper for clipboard operations with retry logic to handle CLIPBRD_E_CANT_OPEN errors
    /// </summary>
    public static class ClipboardHelper {
        private const int MaxRetries = 3;

        /// <summary>
        /// Sets text to clipboard with retry logic for when clipboard is locked.
        /// </summary>
        public static bool SetText(string text) {
            for (int i = 0; i < MaxRetries; i++) {
                try {
                    // Use SetText directly - simpler and doesn't call OleFlushClipboard
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return true;
                } catch (COMException) {
                    // Clipboard locked - retry
                } catch (Exception) {
                    // Other error - retry
                }
            }
            return false;
        }

        /// <summary>
        /// Sets text to clipboard with retry, shows message on failure
        /// </summary>
        public static void SetTextWithFeedback(string text, string successMessage = "Copied to clipboard") {
            if (!SetText(text)) {
                MessageBox.Show(
                    "Failed to copy to clipboard. Please try again.",
                    "Clipboard Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}

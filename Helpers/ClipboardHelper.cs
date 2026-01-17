using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Helper for clipboard operations with retry logic to handle CLIPBRD_E_CANT_OPEN errors
    /// </summary>
    public static class ClipboardHelper {
        private const int MaxRetries = 10;
        private const int RetryDelayMs = 50;

        /// <summary>
        /// Sets text to clipboard with retry logic for when clipboard is locked
        /// </summary>
        public static bool SetText(string text) {
            for (int i = 0; i < MaxRetries; i++) {
                try {
                    Clipboard.SetText(text);
                    return true;
                } catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x800401D0)) {
                    // CLIPBRD_E_CANT_OPEN - clipboard is locked by another app
                    if (i < MaxRetries - 1) {
                        Thread.Sleep(RetryDelayMs);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Sets text to clipboard with retry, shows message on failure
        /// </summary>
        public static void SetTextWithFeedback(string text, string successMessage = "Copied to clipboard") {
            if (SetText(text)) {
                // Success - caller can show their own message if needed
            } else {
                MessageBox.Show(
                    "Failed to copy to clipboard. Please try again.",
                    "Clipboard Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}

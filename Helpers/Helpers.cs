using Microsoft.VisualBasic.Logging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Data;

namespace TexTool.Helpers {
    public static class FileHelper {
        public static bool VerifyFileHash(string filePath, string expectedHash) {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            string fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return fileHash.Equals(expectedHash.ToLowerInvariant());
        }

        public static bool FileExists(string path) {
            return File.Exists(path);
        }

        public static long GetFileSize(string filePath) {
            return new FileInfo(filePath).Length;
        }

        public static string GetFileHash(string filePath) {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static bool IsFileIntact(string filePath, string expectedHash, long expectedSize) {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            string fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            var fileInfo = new FileInfo(filePath);
            bool sizeMatches = fileInfo.Length == expectedSize;

            return fileHash.Equals(expectedHash.ToLowerInvariant()) && sizeMatches;
        }

        public static async Task<FileStream> OpenFileStreamWithRetryAsync(string path, FileMode mode, FileAccess access, FileShare share, int maxRetries = 5, int delayMilliseconds = 2000) {
            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    return new FileStream(path, mode, access, share);
                } catch (IOException ex) {
                    if (attempt == maxRetries) {
                        throw new IOException($"Failed to open file after {maxRetries} attempts: {path}", ex);
                    }
                    await Task.Delay(delayMilliseconds);  // Ждем перед следующей попыткой
                }
            }
            throw new IOException($"Failed to open file after {maxRetries} attempts: {path}");
        }

        public static async Task<FileStream> OpenFileWithRetryAsync(string path, FileMode mode, FileAccess access, FileShare share, int maxRetries, int delayMilliseconds) {
            for (int attempt = 0; attempt < maxRetries; attempt++) {
                try {
                    return new FileStream(path, mode, access, share);
                } catch (IOException ex) when (attempt < maxRetries - 1) {
                    Console.WriteLine($"Attempt {attempt + 1} to open file {path} failed with IOException: {ex.Message}. Retrying...");
                    await Task.Delay(delayMilliseconds);
                }
            }
            throw new IOException($"Failed to open file {path} after {maxRetries} attempts.");
        }
    }
}

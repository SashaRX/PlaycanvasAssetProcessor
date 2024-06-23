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

        public static async Task<FileStream> OpenFileStreamWithRetryAsync(string path, FileMode mode, FileAccess access, FileShare share, int maxRetries = 3, int delayMilliseconds = 1000) {
            for (int i = 0; i < maxRetries; i++) {
                try {
                    return new FileStream(path, mode, access, share);
                } catch (IOException) when (i < maxRetries - 1) {
                    await Task.Delay(delayMilliseconds);
                }
            }
            throw new IOException($"Unable to open file {path} after {maxRetries} attempts.");
        }
    }
}

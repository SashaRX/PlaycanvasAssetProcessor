﻿using System.IO;
using System.Security.Cryptography;

namespace AssetProcessor.Helpers {
    public static class FileHelper {
        public static bool VerifyFileHash(string filePath, string expectedHash) {
            using MD5 md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            string fileHash = Convert.ToHexStringLower(hash);
            return fileHash.Equals(expectedHash.ToLowerInvariant());
        }

        public static bool FileExists(string path) {
            return File.Exists(path);
        }

        public static long GetFileSize(string filePath) {
            return new FileInfo(filePath).Length;
        }

        public static string GetFileHash(string filePath) {
            using MD5 md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return Convert.ToHexStringLower(hash);
        }

        public static bool IsFileIntact(string filePath, string expectedHash, long expectedSize) {
            using MD5 md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            string fileHash = Convert.ToHexStringLower(hash);

            FileInfo fileInfo = new(filePath);
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

using System.IO;
using System.Security.Cryptography;

namespace AssetProcessor.Helpers {
    public static class FileHelper {
        public static bool IsFileIntact(string filePath, string expectedHash, long expectedSize) {
            using MD5 md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            string fileHash = Convert.ToHexStringLower(hash);

            FileInfo fileInfo = new(filePath);
            bool sizeMatches = fileInfo.Length == expectedSize;

            return fileHash.Equals(expectedHash.ToLowerInvariant()) && sizeMatches;
        }
    }
}

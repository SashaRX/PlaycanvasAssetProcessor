using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Data;

namespace TexTool.Helpers {
    public static class Helper {
        public static bool VerifyFileHash(string filePath, string expectedHash) {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            string fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return fileHash.Equals(expectedHash.ToLowerInvariant());
        }
    }
}

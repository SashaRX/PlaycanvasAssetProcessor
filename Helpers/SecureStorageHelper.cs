using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AssetProcessor.Helpers;

internal static class SecureStorageHelper {
    private const string DpapiPrefix = "dpapi:";
    private const string AesPrefix = "aes:";
    private const string MasterPasswordEnvironmentVariable = "PLAYCANVAS_MASTER_PASSWORD";
    private const int SaltSize = 32;

    public static string Protect(string plaintext) {
        if (string.IsNullOrEmpty(plaintext)) {
            return string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            byte[] rawData = Encoding.UTF8.GetBytes(plaintext);
            byte[] protectedData = ProtectedData.Protect(rawData, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(protectedData);
        }

        return AesPrefix + Convert.ToBase64String(EncryptWithMasterPassword(plaintext));
    }

    public static bool TryUnprotect(string? storedValue, out string? plaintext, out bool wasProtected) {
        plaintext = null;
        wasProtected = false;

        if (string.IsNullOrEmpty(storedValue)) {
            return true;
        }

        if (storedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal)) {
            wasProtected = true;
            string payload = storedValue[DpapiPrefix.Length..];
            try {
                byte[] protectedBytes = Convert.FromBase64String(payload);
                byte[] rawBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                plaintext = Encoding.UTF8.GetString(rawBytes);
                return true;
            } catch (FormatException) {
                return false;
            } catch (CryptographicException) {
                return false;
            }
        }

        if (storedValue.StartsWith(AesPrefix, StringComparison.Ordinal)) {
            wasProtected = true;
            string payload = storedValue[AesPrefix.Length..];
            try {
                byte[] cipherBytes = Convert.FromBase64String(payload);
                plaintext = DecryptWithMasterPassword(cipherBytes);
                return true;
            } catch (FormatException) {
                return false;
            } catch (CryptographicException) {
                return false;
            }
        }

        plaintext = storedValue;
        wasProtected = false;
        return true;
    }

    private static byte[] EncryptWithMasterPassword(string plaintext) {
        string masterPassword = GetMasterPassword();
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] salt = GenerateRandomSalt();
        using var keyDerivation = new Rfc2898DeriveBytes(masterPassword, salt, 100_000, HashAlgorithmName.SHA256);
        aes.Key = keyDerivation.GetBytes(32);
        aes.GenerateIV();

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        byte[] result = new byte[salt.Length + aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(aes.IV, 0, result, salt.Length, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, salt.Length + aes.IV.Length, cipherBytes.Length);
        return result;
    }

    private static string DecryptWithMasterPassword(byte[] cipherBytes) {
        string masterPassword = GetMasterPassword();
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] iv = new byte[aes.BlockSize / 8];
        if (cipherBytes.Length < iv.Length) {
            throw new CryptographicException("Encrypted payload is too short.");
        }

        if (cipherBytes.Length >= SaltSize + iv.Length) {
            byte[] salt = new byte[SaltSize];
            Buffer.BlockCopy(cipherBytes, 0, salt, 0, SaltSize);
            using var keyDerivation = new Rfc2898DeriveBytes(masterPassword, salt, 100_000, HashAlgorithmName.SHA256);
            aes.Key = keyDerivation.GetBytes(32);

            Buffer.BlockCopy(cipherBytes, SaltSize, iv, 0, iv.Length);
            aes.IV = iv;

            int payloadLength = cipherBytes.Length - SaltSize - iv.Length;
            if (payloadLength <= 0) {
                throw new CryptographicException("Encrypted payload is too short.");
            }

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(cipherBytes, SaltSize + iv.Length, payload, 0, payload.Length);

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plaintextBytes = decryptor.TransformFinalBlock(payload, 0, payload.Length);
            return Encoding.UTF8.GetString(plaintextBytes);
        }

        byte[] legacySalt = GetLegacySalt();
        using var legacyDerivation = new Rfc2898DeriveBytes(masterPassword, legacySalt, 100_000, HashAlgorithmName.SHA256);
        aes.Key = legacyDerivation.GetBytes(32);

        Buffer.BlockCopy(cipherBytes, 0, iv, 0, iv.Length);
        aes.IV = iv;

        byte[] legacyPayload = new byte[cipherBytes.Length - iv.Length];
        Buffer.BlockCopy(cipherBytes, iv.Length, legacyPayload, 0, legacyPayload.Length);

        using ICryptoTransform legacyDecryptor = aes.CreateDecryptor();
        byte[] legacyPlaintextBytes = legacyDecryptor.TransformFinalBlock(legacyPayload, 0, legacyPayload.Length);
        return Encoding.UTF8.GetString(legacyPlaintextBytes);
    }

    private static byte[] GenerateRandomSalt() {
        byte[] salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    private static byte[] GetLegacySalt() {
        using var sha256 = SHA256.Create();
        string entropySource = Environment.UserName + "|" + Environment.MachineName;
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(entropySource));
    }

    private static string GetMasterPassword() {
        string? masterPassword = Environment.GetEnvironmentVariable(MasterPasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(masterPassword)) {
            throw new InvalidOperationException(
                $"Master password is required on this platform. Set the '{MasterPasswordEnvironmentVariable}' environment variable.");
        }

        return masterPassword;
    }
}

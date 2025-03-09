using System;
using System.Security.Cryptography;
using System.Text;

namespace RiotAutoLogin.Services
{
    public static class EncryptionService
    {
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("RiotClientAutoLoginSalt");

        public static string EncryptString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            byte[] encryptedData = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(input),
                _entropy,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encryptedData);
        }

        public static string DecryptString(string encryptedData)
        {
            if (string.IsNullOrEmpty(encryptedData))
                return string.Empty;

            try
            {
                byte[] decryptedData = ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedData),
                    _entropy,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decryptedData);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

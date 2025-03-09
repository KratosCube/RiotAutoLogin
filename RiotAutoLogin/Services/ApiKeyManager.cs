using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class ApiKeyManager
    {
        private static readonly string ApiKeyFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "apikey.txt");

        public static string GetApiKey()
        {
            try
            {
                if (File.Exists(ApiKeyFilePath))
                {
                    return File.ReadAllText(ApiKeyFilePath).Trim();
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading API key: {ex.Message}");
                return string.Empty;
            }
        }

        public static bool SaveApiKey(string apiKey)
        {
            try
            {
                // Create directory if it doesn't exist.
                string directory = Path.GetDirectoryName(ApiKeyFilePath);
                if (!Directory.Exists(directory) && directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(ApiKeyFilePath, apiKey.Trim());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving API key: {ex.Message}");
                return false;
            }
        }
    }
}

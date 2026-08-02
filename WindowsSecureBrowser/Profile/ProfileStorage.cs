using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WindowsSecureBrowser.Profile
{
    public class ProfileStorage
    {
        public static void SaveEncrypted<T>(string filePath, T data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            
            // Encrypt using DPAPI CurrentUser scope
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(filePath, encryptedBytes);
        }

        public static T? LoadEncrypted<T>(string filePath)
        {
            if (!File.Exists(filePath)) return default;

            try
            {
                byte[] encryptedBytes = File.ReadAllBytes(filePath);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plainBytes);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load encrypted profile data: {ex.Message}");
                return default;
            }
        }
    }
}

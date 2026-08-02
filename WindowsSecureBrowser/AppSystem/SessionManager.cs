using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WindowsSecureBrowser.AppSystem
{
    public class SessionData
    {
        public List<string> OpenUrls { get; set; } = new List<string>();
        public int ActiveIndex { get; set; }
        public DateTime LastSaved { get; set; } = DateTime.Now;
    }

    public class SessionManager
    {
        private static readonly string SessionFilePath = AppDataPath.SessionFilePath;


        public static void SaveSession(List<string> urls, int activeIndex)
        {
            try
            {
                var data = new SessionData
                {
                    OpenUrls = urls,
                    ActiveIndex = activeIndex,
                    LastSaved = DateTime.Now
                };

                string dir = Path.GetDirectoryName(SessionFilePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SessionFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Session Save Error] {ex.Message}");
            }
        }

        public static SessionData? RestoreSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    string json = File.ReadAllText(SessionFilePath);
                    return JsonSerializer.Deserialize<SessionData>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Session Restore Error] {ex.Message}");
            }
            return null;
        }
    }
}

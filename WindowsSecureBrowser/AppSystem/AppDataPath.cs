using System;
using System.IO;

namespace WindowsSecureBrowser.AppSystem
{
    public static class AppDataPath
    {
        public static string RootDataDir
        {
            get
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string ProfilesDir
        {
            get
            {
                string path = Path.Combine(RootDataDir, "Profiles");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string SessionFilePath => Path.Combine(RootDataDir, "session_restore.json");
        public static string AppConfigFilePath => Path.Combine(RootDataDir, "app_config.json");
    }
}

using System;
using System.IO;
using System.Text.Json;

namespace WindowsSecureBrowser.AppSystem
{
    public class AppConfigModel
    {
        public double WindowOpacity { get; set; } = 1.0;
        public string ThemeMode { get; set; } = "Dark";
    }

    public class AppSettingsManager
    {
        public double WindowOpacity { get; set; } = 1.0;
        public string ThemeMode { get; set; } = "Dark";

        public void SaveConfig()
        {
            try
            {
                var model = new AppConfigModel
                {
                    WindowOpacity = this.WindowOpacity,
                    ThemeMode = this.ThemeMode
                };
                string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AppDataPath.AppConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveConfig error: {ex.Message}");
            }
        }

        public void LoadConfig()
        {
            try
            {
                string path = AppDataPath.AppConfigFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var model = JsonSerializer.Deserialize<AppConfigModel>(json);
                    if (model != null)
                    {
                        if (model.WindowOpacity >= 0.1 && model.WindowOpacity <= 1.0)
                        {
                            this.WindowOpacity = model.WindowOpacity;
                        }
                        if (!string.IsNullOrEmpty(model.ThemeMode))
                        {
                            this.ThemeMode = model.ThemeMode;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadConfig error: {ex.Message}");
            }
        }
    }
}

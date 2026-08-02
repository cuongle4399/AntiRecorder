using System;
using System.IO;
using System.Text.Json;

namespace WindowsSecureBrowser.AppSystem
{
    public class AppConfigModel
    {
        public double WindowOpacity { get; set; } = 1.0;
        public string ThemeMode { get; set; } = "Dark";
        public double StartupWidth { get; set; } = 600;
        public double StartupHeight { get; set; } = 470;
        public double ZoomFactor { get; set; } = 0.75;
        public bool IsAudioMuted { get; set; } = true;
    }

    public class AppSettingsManager
    {
        public double WindowOpacity { get; set; } = 1.0;
        public string ThemeMode { get; set; } = "Dark";
        public double StartupWidth { get; set; } = 600;
        public double StartupHeight { get; set; } = 470;
        public double ZoomFactor { get; set; } = 0.75;
        public bool IsAudioMuted { get; set; } = true;

        public void SaveConfig()
        {
            try
            {
                var model = new AppConfigModel
                {
                    WindowOpacity = this.WindowOpacity,
                    ThemeMode = this.ThemeMode,
                    StartupWidth = this.StartupWidth,
                    StartupHeight = this.StartupHeight,
                    ZoomFactor = this.ZoomFactor,
                    IsAudioMuted = this.IsAudioMuted
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
                        if (model.StartupWidth >= 200)
                        {
                            this.StartupWidth = model.StartupWidth;
                        }
                        if (model.StartupHeight >= 150)
                        {
                            this.StartupHeight = model.StartupHeight;
                        }
                        if (model.ZoomFactor >= 0.01 && model.ZoomFactor <= 3.0)
                        {
                            this.ZoomFactor = model.ZoomFactor;
                        }
                        this.IsAudioMuted = model.IsAudioMuted;
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

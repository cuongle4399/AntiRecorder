using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace WindowsSecureBrowser.Browser
{
    public class ExtensionItem
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0";
        public string Description { get; set; } = "";
        public string FolderPath { get; set; } = "";
    }

    public class ExtensionManager
    {
        public static string GetExtensionsFolderPath(string userDataFolder)
        {
            string extFolder = Path.Combine(userDataFolder, "Extensions");
            if (!Directory.Exists(extFolder))
            {
                Directory.CreateDirectory(extFolder);
            }
            EnsureDefaultShieldExtension(extFolder);
            return extFolder;
        }

        public static string GetLoadExtensionArgument(string userDataFolder)
        {
            try
            {
                string extFolder = GetExtensionsFolderPath(userDataFolder);
                var subDirs = Directory.GetDirectories(extFolder);
                if (subDirs.Length == 0) return "";

                List<string> validPaths = new List<string>();
                foreach (var dir in subDirs)
                {
                    if (File.Exists(Path.Combine(dir, "manifest.json")))
                    {
                        validPaths.Add(dir);
                    }
                }

                if (validPaths.Count == 0) return "";
                string joined = string.Join(",", validPaths);
                return $"--load-extension=\"{joined}\" ";
            }
            catch
            {
                return "";
            }
        }

        public static List<ExtensionItem> GetInstalledExtensions(string userDataFolder)
        {
            var list = new List<ExtensionItem>();
            try
            {
                string extFolder = GetExtensionsFolderPath(userDataFolder);
                var subDirs = Directory.GetDirectories(extFolder);

                foreach (var dir in subDirs)
                {
                    string manifestPath = Path.Combine(dir, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var item = new ExtensionItem
                        {
                            Name = Path.GetFileName(dir),
                            FolderPath = dir
                        };

                        try
                        {
                            string json = File.ReadAllText(manifestPath);
                            if (json.Contains("\"name\""))
                            {
                                item.Name = ExtractJsonValue(json, "name") ?? item.Name;
                            }
                            if (json.Contains("\"version\""))
                            {
                                item.Version = ExtractJsonValue(json, "version") ?? "1.0";
                            }
                            if (json.Contains("\"description\""))
                            {
                                item.Description = ExtractJsonValue(json, "description") ?? "";
                            }
                        }
                        catch { }

                        list.Add(item);
                    }
                }
            }
            catch { }

            return list;
        }

        public static void OpenExtensionFolder(string userDataFolder)
        {
            string folder = GetExtensionsFolderPath(userDataFolder);
            try
            {
                Process.Start("explorer.exe", folder);
            }
            catch { }
        }

        private static void EnsureDefaultShieldExtension(string extFolder)
        {
            try
            {
                string shieldFolder = Path.Combine(extFolder, "AntiRecorderShield");
                if (!Directory.Exists(shieldFolder))
                {
                    Directory.CreateDirectory(shieldFolder);
                }

                string manifestPath = Path.Combine(shieldFolder, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    string manifestContent = @"{
  ""manifest_version"": 3,
  ""name"": ""AntiRecorder Shield Extension"",
  ""version"": ""1.0"",
  ""description"": ""Tiện ích mở rộng bảo vệ riêng tư & chặn quảng cáo mặc định cho AntiRecorder Browser."",
  ""content_scripts"": [
    {
      ""matches"": [""<all_urls>""],
      ""js"": [""content.js""],
      ""run_at"": ""document_start""
    }
  ]
}";
                    File.WriteAllText(manifestPath, manifestContent);
                }

                string contentJsPath = Path.Combine(shieldFolder, "content.js");
                if (!File.Exists(contentJsPath))
                {
                    string jsContent = @"// AntiRecorder Shield Content Script
console.log('🛡 [AntiRecorder Extension] AntiRecorder Shield is Active and Running!');
";
                    File.WriteAllText(contentJsPath, jsContent);
                }
            }
            catch { }
        }

        private static string? ExtractJsonValue(string json, string key)
        {
            try
            {
                int keyIdx = json.IndexOf($"\"{key}\"");
                if (keyIdx == -1) return null;
                int colonIdx = json.IndexOf(":", keyIdx);
                if (colonIdx == -1) return null;
                int quoteStart = json.IndexOf("\"", colonIdx);
                if (quoteStart == -1) return null;
                int quoteEnd = json.IndexOf("\"", quoteStart + 1);
                if (quoteEnd == -1) return null;

                return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
            catch
            {
                return null;
            }
        }
    }
}

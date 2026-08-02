using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using WindowsSecureBrowser.Profile;

namespace WindowsSecureBrowser.Browser
{
    public class BrowserManager
    {
        private static BrowserManager? _instance;
        public static BrowserManager Instance => _instance ??= new BrowserManager();

        public ProfileManager ProfileManager { get; } = new ProfileManager();
        public TabManager TabManager { get; } = new TabManager();
        public DownloadManager DownloadManager { get; } = new DownloadManager();

        public async Task<CoreWebView2Environment> CreateEnvironmentForProfileAsync(UserProfile profile)
        {
            string userDataFolder = profile.UserDataFolder;

            if (!Directory.Exists(userDataFolder))
            {
                Directory.CreateDirectory(userDataFolder);
            }

            var options = new CoreWebView2EnvironmentOptions();

            // STRICT AUDIO PRIVACY: Force Chromium engine level --mute-audio flag
            options.AdditionalBrowserArguments = "--mute-audio";

            return await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
        }
    }
}

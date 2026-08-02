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

        public async Task<CoreWebView2Environment> CreateEnvironmentForProfileAsync(UserProfile profile, string? proxyServer = null)
        {
            string userDataFolder = profile.UserDataFolder;

            if (!Directory.Exists(userDataFolder))
            {
                Directory.CreateDirectory(userDataFolder);
            }

            var options = new CoreWebView2EnvironmentOptions();

            string proxyArg = string.IsNullOrWhiteSpace(proxyServer) ? "" : $"--proxy-server=\"{proxyServer}\" ";

            // HIGH PERFORMANCE & LOW RAM/CPU CHROMIUM ARGUMENTS
            options.AdditionalBrowserArguments = proxyArg +
                "--disable-background-networking " +
                "--disable-background-timer-throttling " +
                "--disable-client-side-phishing-detection " +
                "--disable-default-apps " +
                "--disable-extensions " +
                "--disable-hang-monitor " +
                "--disable-popup-blocking " +
                "--disable-prompt-on-repost " +
                "--disable-sync " +
                "--disable-translate " +
                "--metrics-recording-only " +
                "--no-first-run " +
                "--safebrowsing-disable-auto-update " +
                "--enable-features=MemorySaver " +
                "--js-flags=\"--max-old-space-size=128\"";

            return await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
        }
    }
}

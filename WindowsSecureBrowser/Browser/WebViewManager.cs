using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowsSecureBrowser.Profile;

namespace WindowsSecureBrowser.Browser
{
    public class WebViewManager
    {
        public async Task InitializeWebViewAsync(WebView2 webView, CoreWebView2Environment environment, string initialUrl, UserProfile? profile = null, bool isMuted = true)
        {
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            await webView.EnsureCoreWebView2Async(environment);
            
            // Default configuration
            webView.CoreWebView2.Settings.IsStatusBarEnabled = true;
            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
            webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            
            // AUDIO PRIVACY: Set IsMuted based on app configuration (defaults to true)
            webView.CoreWebView2.IsMuted = isMuted;

            webView.Source = new Uri(initialUrl);
        }

        public void Navigate(WebView2 webView, string targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl)) return;

            if (!targetUrl.StartsWith("http://") && !targetUrl.StartsWith("https://") && !targetUrl.StartsWith("file://"))
            {
                // If it looks like a domain name, prepend https://, otherwise treat as Google search
                if (targetUrl.Contains(".") && !targetUrl.Contains(" "))
                {
                    targetUrl = "https://" + targetUrl;
                }
                else
                {
                    targetUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(targetUrl);
                }
            }

            try
            {
                webView.Source = new Uri(targetUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }
    }
}

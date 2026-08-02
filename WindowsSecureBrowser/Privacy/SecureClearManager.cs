using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using WindowsSecureBrowser.Browser;

namespace WindowsSecureBrowser.Privacy
{
    public class SecureClearManager
    {
        public static async Task ClearCookiesAndSessionDataAsync(BrowserManager browserManager)
        {
            try
            {
                if (browserManager?.TabManager?.Tabs != null)
                {
                    foreach (var tab in browserManager.TabManager.Tabs)
                    {
                        try
                        {
                            if (tab.WebView != null && tab.WebView.CoreWebView2 != null)
                            {
                                // 1. Delete all Cookies via WebView2 CookieManager
                                tab.WebView.CoreWebView2.CookieManager.DeleteAllCookies();

                                // 2. Clear All Browsing Data (Cookies, Cache, DOM Storage, LocalStorage, IndexedDB, WebSQL)
                                if (tab.WebView.CoreWebView2.Profile != null)
                                {
                                    await tab.WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                                        CoreWebView2BrowsingDataKinds.Cookies |
                                        CoreWebView2BrowsingDataKinds.DiskCache |
                                        CoreWebView2BrowsingDataKinds.LocalStorage |
                                        CoreWebView2BrowsingDataKinds.AllDomStorage |
                                        CoreWebView2BrowsingDataKinds.CacheStorage |
                                        CoreWebView2BrowsingDataKinds.IndexedDb |
                                        CoreWebView2BrowsingDataKinds.WebSql
                                    );
                                }

                                // 3. Execute JS script to wipe in-memory SessionStorage and LocalStorage
                                try
                                {
                                    await tab.WebView.CoreWebView2.ExecuteScriptAsync(
                                        "try { window.sessionStorage.clear(); window.localStorage.clear(); } catch(e) {}"
                                    );
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Tab clear error: {ex.Message}");
                        }
                    }

                    // Reload active tab to reflect session invalidation/logout immediately
                    var activeTab = browserManager.TabManager.ActiveTab;
                    if (activeTab?.WebView != null && activeTab.WebView.CoreWebView2 != null)
                    {
                        activeTab.WebView.Reload();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearCookiesAndSessionDataAsync error: {ex.Message}");
            }

            // 4. Wipe Private RAM Bitmaps & OS Clipboard
            ClearSensitiveData();
        }

        public static void ClearSensitiveData()
        {
            // 1. Wipe Private RAM Bitmaps
            PrivateClipboardManager.ClearPrivateRam();

            // 2. Wipe OS Clipboard if private data was set
            PrivateClipboardManager.ClearSystemClipboardIfMatching();

            // 3. Force Garbage Collector memory cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Web.WebView2.Wpf;

namespace WindowsSecureBrowser.Browser
{
    public class BrowserTab
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "https://www.google.com";
        public WebView2 WebView { get; set; } = null!;
        public bool IsActive { get; set; }
        public ProxyModel Proxy { get; set; } = new ProxyModel();
        public long PingMs { get; set; } = -1;

        /// <summary>
        /// True khi WebView2 đã bị dispose để giải phóng RAM (ví dụ khi app ẩn xuống tray).
        /// URL được giữ lại để restore khi cần.
        /// </summary>
        public bool IsDiscarded { get; set; } = false;

        /// <summary>
        /// True khi WebView2 đang trong quá trình tái tạo lại sau khi bị discard.
        /// </summary>
        public bool IsRestoring { get; set; } = false;
    }

    public class TabManager
    {
        public ObservableCollection<BrowserTab> Tabs { get; } = new ObservableCollection<BrowserTab>();
        public BrowserTab? ActiveTab => Tabs.FirstOrDefault(t => t.IsActive);

        public event EventHandler<BrowserTab>? TabAdded;
        public event EventHandler<BrowserTab>? TabClosed;
        public event EventHandler<BrowserTab>? TabSelected;

        public BrowserTab CreateTab(string url = "https://www.google.com")
        {
            var webView = new WebView2();
            var tab = new BrowserTab
            {
                Url = url,
                WebView = webView,
                Title = "Loading..."
            };

            Tabs.Add(tab);
            SelectTab(tab);
            TabAdded?.Invoke(this, tab);
            return tab;
        }

        public void SelectTab(BrowserTab tab)
        {
            if (!Tabs.Contains(tab)) return;

            foreach (var t in Tabs)
            {
                t.IsActive = (t == tab);
            }
            TabSelected?.Invoke(this, tab);
        }

        public void CloseTab(BrowserTab tab)
        {
            if (!Tabs.Contains(tab)) return;

            int index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            
            try
            {
                tab.WebView.Dispose();
            }
            catch { }

            TabClosed?.Invoke(this, tab);

            if (Tabs.Count > 0)
            {
                int nextIndex = Math.Min(index, Tabs.Count - 1);
                SelectTab(Tabs[nextIndex]);
            }
        }

        public void NextTab()
        {
            if (Tabs.Count <= 1) return;
            var current = ActiveTab;
            if (current == null)
            {
                if (Tabs.Count > 0) SelectTab(Tabs[0]);
                return;
            }
            int idx = Tabs.IndexOf(current);
            int nextIdx = (idx + 1) % Tabs.Count;
            SelectTab(Tabs[nextIdx]);
        }
    }
}

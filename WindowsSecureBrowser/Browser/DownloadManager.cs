using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace WindowsSecureBrowser.Browser
{
    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public string Url { get; set; } = "";
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public int ProgressPercent => TotalBytes > 0 ? (int)((BytesReceived * 100) / TotalBytes) : 0;
        public string State { get; set; } = "In Progress";
        public CoreWebView2DownloadOperation? Operation { get; set; }
    }

    public class DownloadManager
    {
        public ObservableCollection<DownloadItem> Downloads { get; } = new ObservableCollection<DownloadItem>();
        public string DefaultDownloadPath { get; set; }

        public DownloadManager()
        {
            DefaultDownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        public void RegisterDownloadEvents(CoreWebView2 coreWebView2)
        {
            coreWebView2.DownloadStarting += (sender, args) =>
            {
                var op = args.DownloadOperation;
                var item = new DownloadItem
                {
                    FileName = Path.GetFileName(args.ResultFilePath),
                    Url = op.Uri,
                    TotalBytes = (long)(op.TotalBytesToReceive ?? 0),
                    Operation = op
                };

                Downloads.Add(item);

                op.BytesReceivedChanged += (s, e) =>
                {
                    item.BytesReceived = (long)op.BytesReceived;
                    item.TotalBytes = (long)(op.TotalBytesToReceive ?? 0);
                };

                op.StateChanged += (s, e) =>
                {
                    item.State = op.State.ToString();
                };
            };
        }
    }
}

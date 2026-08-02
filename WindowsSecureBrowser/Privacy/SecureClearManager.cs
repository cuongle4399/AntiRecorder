using System;

namespace WindowsSecureBrowser.Privacy
{
    public class SecureClearManager
    {
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

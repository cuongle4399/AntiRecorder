using System;
using System.Runtime.InteropServices;

namespace WindowsSecureBrowser.Security
{
    public static class SecurityCoreWrapper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        public static bool SetWindowProtection(IntPtr hwnd, bool enable)
        {
            uint affinity = enable ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
            bool success = SetWindowDisplayAffinity(hwnd, affinity);
            if (!success && enable)
            {
                success = SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
            }

            // Recursively protect all child HWNDs (WebView2, popups, tooltips)
            try
            {
                EnumChildWindows(hwnd, (childHwnd, lParam) =>
                {
                    try
                    {
                        SetWindowDisplayAffinity(childHwnd, affinity);
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            return success;
        }
    }
}

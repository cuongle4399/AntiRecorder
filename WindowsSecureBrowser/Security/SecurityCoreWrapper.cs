using System;
using System.Runtime.InteropServices;

namespace WindowsSecureBrowser.Security
{
    public static class SecurityCoreWrapper
    {
        private const string DllName = "SecurityCore.dll";
        private static bool? s_isNativeDllAvailable;

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, EntryPoint = "SetWindowCaptureProtection")]
        private static extern bool NativeSetWindowCaptureProtection(IntPtr hwnd, bool enable);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static bool SetWindowProtection(IntPtr hwnd, bool enable)
        {
            if (s_isNativeDllAvailable == null)
            {
                try
                {
                    // Check if native C++ DLL is loaded
                    NativeSetWindowCaptureProtection(IntPtr.Zero, false);
                    s_isNativeDllAvailable = true;
                }
                catch
                {
                    s_isNativeDllAvailable = false;
                }
            }

            if (s_isNativeDllAvailable == true)
            {
                try
                {
                    return NativeSetWindowCaptureProtection(hwnd, enable);
                }
                catch
                {
                    s_isNativeDllAvailable = false;
                }
            }

            // Direct Win32 Fallback
            uint affinity = enable ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
            bool success = SetWindowDisplayAffinity(hwnd, affinity);
            if (!success && enable)
            {
                success = SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
            }
            return success;
        }
    }
}

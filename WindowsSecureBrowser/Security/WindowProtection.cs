using System;
using System.Windows;
using System.Windows.Interop;

namespace WindowsSecureBrowser.Security
{
    public enum ProtectionMode
    {
        FullStealth = 0,     // 100% Permanent Black / Hidden on Screen Recorders
        AllowOSCapture = 0,
        Disabled = 0
    }

    public class WindowProtection
    {
        public static ProtectionMode CurrentMode { get; set; } = ProtectionMode.FullStealth;
        private static bool _isProtectionDisabledTemporarily = false;

        public static bool IsProtectionDisabledTemporarily => _isProtectionDisabledTemporarily;

        public static bool ApplyProtection(Window window, ProtectionMode mode = ProtectionMode.FullStealth)
        {
            if (_isProtectionDisabledTemporarily) return false;

            CurrentMode = ProtectionMode.FullStealth;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            // Enforce WDA_EXCLUDEFROMCAPTURE to hide app window 100% PERMANENTLY from screen recorders (OBS, Discord, Zoom, Camtasia...)
            return SecurityCoreWrapper.SetWindowProtection(hwnd, true);
        }

        public static bool EnableCaptureProtection(Window window)
        {
            _isProtectionDisabledTemporarily = false;
            return ApplyProtection(window, ProtectionMode.FullStealth);
        }

        public static bool DisableCaptureProtection(Window window)
        {
            // Only used internally during OS screenshot (PrintScreen / Win+Shift+S) or regional screenshot capture
            _isProtectionDisabledTemporarily = true;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            return SecurityCoreWrapper.SetWindowProtection(hwnd, false);
        }

        public static void RegisterContinuousProtectionHook(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProcProtectionHook);
                ApplyProtection(window, ProtectionMode.FullStealth);
            }
        }

        private static IntPtr WndProcProtectionHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SHOWWINDOW = 0x0018;
            const int WM_ACTIVATE = 0x0006;
            const int WM_WINDOWPOSCHANGED = 0x0047;
            const int WM_STYLECHANGED = 0x007C;
            const int WM_ENTERSIZEMOVE = 0x0231;
            const int WM_EXITSIZEMOVE = 0x0232;
            const int WM_NCACTIVATE = 0x0086;

            if (msg == WM_SHOWWINDOW || msg == WM_ACTIVATE || msg == WM_WINDOWPOSCHANGED ||
                msg == WM_STYLECHANGED || msg == WM_ENTERSIZEMOVE || msg == WM_EXITSIZEMOVE || msg == WM_NCACTIVATE)
            {
                if (!_isProtectionDisabledTemporarily)
                {
                    // Re-enforce Anti-Recording Protection when not temporarily suspended for OS screenshot
                    SecurityCoreWrapper.SetWindowProtection(hwnd, true);
                }
            }

            return IntPtr.Zero;
        }
    }
}

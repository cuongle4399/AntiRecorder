using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace WindowsSecureBrowser.Security
{
    public enum ProtectionMode
    {
        FullStealth = 0,     // 100% Permanent Black / Hidden on Screen Recorders & Snipping Tools
        AllowOSCapture = 1,
        Disabled = 2
    }

    public class WindowProtection
    {
        public static ProtectionMode CurrentMode { get; set; } = ProtectionMode.FullStealth;
        private static bool _isProtectionDisabledTemporarily = false;
        private static bool _isBackgroundScannerStarted = false;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private static WinEventDelegate? _winEventDelegate;
        private static IntPtr _winEventHook = IntPtr.Zero;

        public static bool IsProtectionDisabledTemporarily => _isProtectionDisabledTemporarily;

        public static bool ApplyProtection(Window window, ProtectionMode mode = ProtectionMode.FullStealth)
        {
            if (_isProtectionDisabledTemporarily) return false;

            CurrentMode = mode;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            return SecurityCoreWrapper.SetWindowProtection(hwnd, true);
        }

        public static bool EnableCaptureProtection(Window window)
        {
            _isProtectionDisabledTemporarily = false;
            return ApplyProtection(window, ProtectionMode.FullStealth);
        }

        public static bool DisableCaptureProtection(Window window)
        {
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

            RegisterWinEventHook();
            StartProcessWideStealthScanner();
        }

        private static void RegisterWinEventHook()
        {
            if (_winEventHook != IntPtr.Zero) return;
            try
            {
                uint pid = (uint)Process.GetCurrentProcess().Id;
                _winEventDelegate = new WinEventDelegate(WinEventProc);
                _winEventHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _winEventDelegate, pid, 0, WINEVENT_OUTOFCONTEXT);
            }
            catch { }
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd != IntPtr.Zero && !_isProtectionDisabledTemporarily)
            {
                try
                {
                    SecurityCoreWrapper.SetWindowProtection(hwnd, true);
                }
                catch { }
            }
        }

        private static void StartProcessWideStealthScanner()
        {
            if (_isBackgroundScannerStarted) return;
            _isBackgroundScannerStarted = true;

            Task.Run(async () =>
            {
                int currentPid = Process.GetCurrentProcess().Id;
                while (true)
                {
                    try
                    {
                        // 2000ms: an toàn vì giờ đã có immediate protection khi tạo WebView2 mới.
                        // Scanner chỉ còn là safety net — không cần tick 1000ms, tiết kiệm ~50% CPU overhead.
                        await Task.Delay(2000);
                        if (!_isProtectionDisabledTemporarily)
                        {
                            ProtectAllProcessWindows(currentPid);
                        }
                    }
                    catch { }
                }
            });
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static void ProtectAllProcessWindows(int targetPid)
        {
            try
            {
                EnumWindows((hwnd, lParam) =>
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == targetPid)
                    {
                        SecurityCoreWrapper.SetWindowProtection(hwnd, true);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
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
            const int WM_MOVE = 0x0003;
            const int WM_SIZE = 0x0005;
            const int WM_MOVING = 0x0216;
            const int WM_SIZING = 0x0214;
            const int WM_PAINT = 0x000F;
            const int WM_DISPLAYCHANGE = 0x007E;
            const int WM_DPICHANGED = 0x02E0;

            if (msg == WM_SHOWWINDOW || msg == WM_ACTIVATE || msg == WM_WINDOWPOSCHANGED ||
                msg == WM_STYLECHANGED || msg == WM_ENTERSIZEMOVE || msg == WM_EXITSIZEMOVE ||
                msg == WM_NCACTIVATE || msg == WM_MOVE || msg == WM_SIZE || msg == WM_MOVING ||
                msg == WM_SIZING || msg == WM_PAINT || msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED)
            {
                if (!_isProtectionDisabledTemporarily)
                {
                    SecurityCoreWrapper.SetWindowProtection(hwnd, true);
                }
            }

            return IntPtr.Zero;
        }
    }
}

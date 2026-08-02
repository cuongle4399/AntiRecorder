using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsSecureBrowser.Security
{
    public class OSScreenshotDetector
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_SNAPSHOT = 0x2C; // PrintScreen key
        private const int VK_S = 0x53;        // 'S' key
        private const int VK_LWIN = 0x5B;     // Left Win key
        private const int VK_RWIN = 0x5C;     // Right Win key
        private const int VK_SHIFT = 0x10;    // Shift key

        private static LowLevelKeyboardProc? _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static Window? _targetWindow;
        private static CancellationTokenSource? _resetTimerCts;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public static void Initialize(Window window)
        {
            _targetWindow = window;
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // When user clicks back into the app, restore protection
            window.Activated += (s, e) =>
            {
                if (_resetTimerCts != null)
                {
                    _resetTimerCts.Cancel();
                    _resetTimerCts = null;
                    WindowProtection.EnableCaptureProtection(window);
                }
            };
        }

        public static void Shutdown()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);

                bool isPrintScreen = (vkCode == VK_SNAPSHOT);
                bool isWinShiftS = false;

                if (vkCode == VK_S)
                {
                    bool winDown = (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0;
                    bool shiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                    if (winDown && shiftDown)
                    {
                        isWinShiftS = true;
                    }
                }

                if ((isPrintScreen || isWinShiftS) && _targetWindow != null)
                {
                    TemporarilyDisableProtectionForOSCapture();
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public static void TemporarilyDisableProtectionForOSCapture()
        {
            if (_targetWindow == null) return;

            // Cancel any existing pending timer
            _resetTimerCts?.Cancel();
            _resetTimerCts = new CancellationTokenSource();
            var token = _resetTimerCts.Token;

            _targetWindow.Dispatcher.Invoke(() =>
            {
                // Ensure window remains visible and NOT hidden or minimized!
                if (_targetWindow.WindowState == WindowState.Minimized)
                {
                    _targetWindow.WindowState = WindowState.Normal;
                }
                _targetWindow.Show();

                // Temporarily disable WDA_EXCLUDEFROMCAPTURE so OS Screenshot (PrintScreen / Win+Shift+S / Snipping Tool / Lightshot) captures window content clearly
                WindowProtection.DisableCaptureProtection(_targetWindow);
            });

            // Re-enable protection after 5000ms (5s) or when user clicks back into app
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5000, token);
                    if (!token.IsCancellationRequested && _targetWindow != null)
                    {
                        _targetWindow.Dispatcher.Invoke(() =>
                        {
                            WindowProtection.EnableCaptureProtection(_targetWindow);
                        });
                    }
                }
                catch (TaskCanceledException) {}
            });
        }

        #region Win32 Imports
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern short GetKeyState(int nVirtKey);
        #endregion
    }
}

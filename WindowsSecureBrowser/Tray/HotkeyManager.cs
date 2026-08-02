using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsSecureBrowser.Tray
{
    public class HotkeyManager
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const uint MOD_NONE = 0x0000;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;

        public const uint VK_F4 = 0x73;
        public const uint VK_SPACE = 0x20;
        public const uint VK_S = 0x53;

        public const int HOTKEY_F4 = 9001;
        public const int HOTKEY_CTRL_SHIFT_SPACE = 9002;
        public const int HOTKEY_CTRL_SHIFT_S = 9003;

        private HwndSource? _hwndSource;
        private IntPtr _windowHandle;

        public event Action? OnF4Pressed;
        public event Action? OnCtrlShiftSpacePressed;
        public event Action? OnCtrlShiftSPressed;

        public void RegisterGlobalHotkeys(Window window)
        {
            _windowHandle = new WindowInteropHelper(window).Handle;
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource.AddHook(HwndHook);

            // F4 (Hide/Show Toggle)
            RegisterHotKey(_windowHandle, HOTKEY_F4, MOD_NONE, VK_F4);

            // Ctrl + Shift + Space (Show Window)
            RegisterHotKey(_windowHandle, HOTKEY_CTRL_SHIFT_SPACE, MOD_CONTROL | MOD_SHIFT, VK_SPACE);

            // Ctrl + Shift + S (Private Screenshot)
            RegisterHotKey(_windowHandle, HOTKEY_CTRL_SHIFT_S, MOD_CONTROL | MOD_SHIFT, VK_S);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_F4:
                        OnF4Pressed?.Invoke();
                        handled = true;
                        break;
                    case HOTKEY_CTRL_SHIFT_SPACE:
                        OnCtrlShiftSpacePressed?.Invoke();
                        handled = true;
                        break;
                    case HOTKEY_CTRL_SHIFT_S:
                        OnCtrlShiftSPressed?.Invoke();
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }

        public void UnregisterHotkeys()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_F4);
                UnregisterHotKey(_windowHandle, HOTKEY_CTRL_SHIFT_SPACE);
                UnregisterHotKey(_windowHandle, HOTKEY_CTRL_SHIFT_S);
            }
        }
    }
}

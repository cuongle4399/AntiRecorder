using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using WindowsSecureBrowser.Security;

namespace WindowsSecureBrowser.Tray
{
    public class TrayManager
    {
        private NotifyIcon? _notifyIcon;
        private Window? _mainWindow;

        public void Initialize(Window mainWindow)

        {
            _mainWindow = mainWindow;

            Icon appIcon = SystemIcons.Shield;
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (File.Exists(iconPath))
                {
                    appIcon = new Icon(iconPath);
                }
            }
            catch
            {
                appIcon = SystemIcons.Shield;
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = appIcon,
                Text = "Trình Duyệt Bảo Mật Riêng Tư (Bấm F4 để Ẩn/Hiện)",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Mở Trình Duyệt", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Ẩn Trình Duyệt (F4)", null, (s, e) => HideWindow());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Thoát Ứng Dụng", null, (s, e) => ExitApp());


            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ToggleWindow();
        }

        public void ToggleWindow()
        {
            if (_mainWindow == null) return;

            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                HideWindow();
            }
            else
            {
                ShowWindow();
            }
        }

        public void ShowWindow()
        {
            if (_mainWindow == null) return;

            // STRICT STEALTH: Enforce protection FIRST before restoring window surface visibility!
            WindowProtection.EnableCaptureProtection(_mainWindow);
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        }


        public void HideWindow()
        {
            if (_mainWindow == null) return;

            _mainWindow.Hide();
        }



        public void ShowNotification(string title, string text)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
        }

        private void ExitApp()
        {
            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowsSecureBrowser.AppSystem;
using WindowsSecureBrowser.Browser;
using WindowsSecureBrowser.Privacy;
using WindowsSecureBrowser.Profile;
using WindowsSecureBrowser.Security;
using WindowsSecureBrowser.Tray;
using WindowsSecureBrowser.UI;

namespace WindowsSecureBrowser
{
    public partial class MainWindow : Window
    {
        private readonly BrowserManager _browserManager = BrowserManager.Instance;
        private readonly WebViewManager _webViewManager = new WebViewManager();
        private readonly HotkeyManager _hotkeyManager = new HotkeyManager();
        private readonly TrayManager _trayManager = new TrayManager();
        private readonly ScreenshotManager _screenshotManager = new ScreenshotManager();
        private readonly OCRManager _ocrManager = new OCRManager();
        private readonly AppSettingsManager _appSettings = new AppSettingsManager();
        private readonly ThemeManager _themeManager;

        private CoreWebView2Environment? _activeEnvironment;

        // Guard flags cho Tray Discard — tránh race condition và screenshot mode trigger sai
        private bool _isDiscarding = false;      // Đang trong quá trình dispose WebView2s
        private bool _isTemporaryHide = false;   // App ẩn tạm để chụp screenshot (KHÔNG discard)

        public MainWindow()
        {
            _isInitializingTheme = true;
            InitializeComponent();
            _themeManager = new ThemeManager(this);
            _isInitializingTheme = false;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 0. Load saved App Settings (Opacity, Theme, Startup Size, Zoom)
            _appSettings.LoadConfig();
            this.Width = (_appSettings.StartupWidth >= 200) ? _appSettings.StartupWidth : 600;
            this.Height = (_appSettings.StartupHeight >= 150) ? _appSettings.StartupHeight : 470;

            if (_appSettings.WindowOpacity >= 0.1 && _appSettings.WindowOpacity <= 1.0)
            {
                ApplyWindowOpacity(_appSettings.WindowOpacity);
            }
            ApplyTheme(_appSettings.ThemeMode);
            this.Topmost = _appSettings.IsAlwaysOnTop;

            // 1. Initialize Tray Icon & Global Hotkeys
            _trayManager.Initialize(this);

            _hotkeyManager.RegisterGlobalHotkeys(this);

            // 2. Enforce Continuous Protection Hook & Stealth Cursor
            this.ShowInTaskbar = false;
            WindowProtection.CurrentMode = ProtectionMode.FullStealth;
            WindowProtection.RegisterContinuousProtectionHook(this);
            OSScreenshotDetector.Initialize(this);
            Activated += (s, e) => { this.ShowInTaskbar = false; HideFromAltTab(); if (!WindowProtection.IsProtectionDisabledTemporarily) WindowProtection.EnableCaptureProtection(this); };
            StateChanged += (s, e) => { this.ShowInTaskbar = false; HideFromAltTab(); if (!WindowProtection.IsProtectionDisabledTemporarily) WindowProtection.EnableCaptureProtection(this); };
            IsVisibleChanged += async (s, e) =>
            {
                this.ShowInTaskbar = false;
                HideFromAltTab();
                if (IsVisible)
                {
                    if (!WindowProtection.IsProtectionDisabledTemporarily)
                        WindowProtection.EnableCaptureProtection(this);

                    // App hiện lại → restore tab đang active nếu đã bị discard
                    var activeTab = _browserManager.TabManager.ActiveTab;
                    if (activeTab?.IsDiscarded == true)
                        await RestoreDiscardedTabAsync(activeTab);
                }
                else if (!IsVisible && !_isTemporaryHide && !_isDiscarding)
                {
                    // App ẩn xuống tray thật sự (F4) → dispose toàn bộ WebView2 giải phóng RAM
                    // Guard: bỏ qua nếu đang ẩn tạm để chụp screenshot (_isTemporaryHide)
                    //        hoặc đã đang trong quá trình discard (_isDiscarding)
                    await DiscardAllWebViewsAsync();
                }
            };

            MouseEnter += (s, e) => { System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow; };
            MouseLeave += (s, e) => { System.Windows.Input.Mouse.OverrideCursor = null; };

            // Ensure window pops up front-and-center when app launches
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();

            _hotkeyManager.OnF4Pressed += () => Dispatcher.Invoke(() => _trayManager.ToggleWindow());
            _hotkeyManager.OnCtrlShiftSpacePressed += () => Dispatcher.Invoke(() => _trayManager.ShowWindow());
            _hotkeyManager.OnCtrlShiftSPressed += () => Dispatcher.Invoke(HandleCtrlShiftSHotkey);

            // 3. Register Screenshot Handler
            _screenshotManager.ScreenshotCaptured += OnScreenshotCaptured;

            // 4. Setup Tab Manager Listeners
            _browserManager.TabManager.TabAdded += TabManager_TabAdded;
            _browserManager.TabManager.TabSelected += TabManager_TabSelected;
            _browserManager.TabManager.TabClosed += TabManager_TabClosed;

            this.SizeChanged += (s, e) => UpdateTabHeaderWidths();

            // 5. Initialize Core WebView2 Environment for current profile
            await ReinitializeProfileEnvironmentAsync();

            // 6. Restore session or create initial tab
            var session = SessionManager.RestoreSession();
            if (session != null && session.OpenUrls.Count > 0)
            {
                foreach (var url in session.OpenUrls)
                {
                    AddNewTab(url);
                }
            }
            else
            {
                AddNewTab("https://www.google.com");
            }

            // Startup: chỉ hiện số RAM, không chạy GC — tránh làm chậm khởi động
            txtMemoryUsage.Text = $"RAM: {ResourceManager.GetWorkingSetMemoryMB()} MB";

            // 7. Periodic Background RAM Trim — chạy mỗi 5 phút, GC nhẹ (không Aggressive)
            var memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            memoryTimer.Tick += async (s, e) => await ResourceManager.OptimizeMemoryAsync(aggressive: false);
            memoryTimer.Start();
        }

        #region Window Event Handlers & Resizing (WM_NCHITTEST & WM_GETMINMAXINFO)
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    RECT rcWorkArea = monitorInfo.rcWork;
                    RECT rcMonitorArea = monitorInfo.rcMonitor;

                    mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                    mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                    mmi.ptMaxSize.x = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                    mmi.ptMaxSize.y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);

                    mmi.ptMinTrackSize.x = 320;
                    mmi.ptMinTrackSize.y = 220;
                }
            }

            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
        }

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WM_SETCURSOR = 0x0020;
        private const int IDC_ARROW = 32512;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        private void HideFromAltTab()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW;  // Hide from Alt+Tab switcher & Win+Tab Task View!
                    exStyle &= ~WS_EX_APPWINDOW; // Exclude from AppWindow list
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HideFromAltTab error: {ex.Message}");
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
            HideFromAltTab();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETCURSOR)
            {
                IntPtr hArrow = LoadCursor(IntPtr.Zero, IDC_ARROW);
                if (hArrow != IntPtr.Zero)
                {
                    SetCursor(hArrow);
                    handled = true;
                    return new IntPtr(1);
                }
            }

            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
            {
                int cornerSize = 10;
                int borderSize = 6;

                int x = (short)(lParam.ToInt32() & 0xFFFF);
                int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

                System.Windows.Point screenPoint = new System.Windows.Point(x, y);
                System.Windows.Point windowPoint = PointFromScreen(screenPoint);

                double width = ActualWidth;
                double height = ActualHeight;

                if (windowPoint.X <= cornerSize && windowPoint.Y <= cornerSize) { handled = true; return new IntPtr(HTTOPLEFT); }
                if (windowPoint.X >= width - cornerSize && windowPoint.Y <= cornerSize) { handled = true; return new IntPtr(HTTOPRIGHT); }
                if (windowPoint.X <= cornerSize && windowPoint.Y >= height - cornerSize) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                if (windowPoint.X >= width - cornerSize && windowPoint.Y >= height - cornerSize) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }

                if (windowPoint.X <= borderSize) { handled = true; return new IntPtr(HTLEFT); }
                if (windowPoint.X >= width - borderSize) { handled = true; return new IntPtr(HTRIGHT); }
                if (windowPoint.Y <= borderSize) { handled = true; return new IntPtr(HTTOP); }
                if (windowPoint.Y >= height - borderSize) { handled = true; return new IntPtr(HTBOTTOM); }
            }
            return IntPtr.Zero;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    BtnMaximize_Click(sender, e);
                }
                else
                {
                    this.DragMove();
                }
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (OuterWindowBorder != null)
            {
                if (WindowState == WindowState.Maximized)
                {
                    OuterWindowBorder.CornerRadius = new CornerRadius(0);
                    OuterWindowBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    OuterWindowBorder.CornerRadius = new CornerRadius(8);
                    OuterWindowBorder.BorderThickness = new Thickness(1.5);
                }
            }

            double w = e.NewSize.Width;

            // 3. Responsive Nav Buttons in Address Toolbar
            if (btnForward != null && btnHome != null)
            {
                if (w < 480)
                {
                    btnForward.Visibility = Visibility.Collapsed;
                    btnHome.Visibility = Visibility.Collapsed;
                }
                else
                {
                    btnForward.Visibility = Visibility.Visible;
                    btnHome.Visibility = Visibility.Visible;
                }
            }
        }

        private void BtnDragWindow_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            _trayManager?.HideWindow();
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnCloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        #endregion



        private async System.Threading.Tasks.Task ReinitializeProfileEnvironmentAsync()
        {
            try
            {
                var profile = _browserManager.ProfileManager.CurrentProfile;
                _activeEnvironment = await _browserManager.CreateEnvironmentForProfileAsync(profile);

                var tabs = _browserManager.TabManager.Tabs.ToArray();
                foreach (var tab in tabs)
                {
                    await RecreateTabWebViewAsync(tab, tab.Url);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReinitializeProfileEnvironmentAsync error: {ex.Message}");
            }
        }


        private async System.Threading.Tasks.Task RecreateTabWebViewAsync(BrowserTab tab, string targetUrl)
        {
            if (tab == null || _activeEnvironment == null) return;

            string url = !string.IsNullOrWhiteSpace(targetUrl) ? targetUrl : (string.IsNullOrWhiteSpace(tab.Url) ? "https://www.google.com" : tab.Url);

            try
            {
                if (tab.WebView != null)
                {
                    WebViewHostGrid.Children.Remove(tab.WebView);
                    try { tab.WebView.Dispose(); } catch {}
                }

                var newWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
                tab.WebView = newWebView;

                WebViewHostGrid.Children.Add(newWebView);

                await _webViewManager.InitializeWebViewAsync(newWebView, _activeEnvironment, url, _browserManager.ProfileManager.CurrentProfile, _appSettings?.IsAudioMuted ?? true);

                // STEALTH: Áp dụng WDA_EXCLUDEFROMCAPTURE ngay lập tức cho HWND mới
                // Không đợi Background Scanner (độ trễ tối đa 1 giây)
                WindowProtection.EnableCaptureProtection(this);

                newWebView.CoreWebView2.SourceChanged += (s, e) =>
                {
                    tab.Url = newWebView.Source.ToString();
                    if (tab == _browserManager.TabManager.ActiveTab)
                    {
                        txtAddressBar.Text = tab.Url;
                    }
                    _browserManager.ProfileManager.AddHistory(tab.Url);
                };

                newWebView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    tab.Title = newWebView.CoreWebView2.DocumentTitle;
                    UpdateTabHeaderUI(tab);
                };

                newWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    ApplyThemeToTab(tab);
                    try { if (_appSettings != null) newWebView.ZoomFactor = Math.Clamp(_appSettings.ZoomFactor, 0.01, 3.0); } catch { }
                };

                _browserManager.DownloadManager.RegisterDownloadEvents(newWebView.CoreWebView2);
                ApplyThemeToTab(tab);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecreateTabWebViewAsync error: {ex.Message}");
            }
        }



        private async void AddNewTab(string url = "https://www.google.com")
        {
            if (_activeEnvironment == null) return;

            var tab = _browserManager.TabManager.CreateTab(url);
            await _webViewManager.InitializeWebViewAsync(tab.WebView, _activeEnvironment, url, _browserManager.ProfileManager.CurrentProfile, _appSettings?.IsAudioMuted ?? true);

            // STEALTH: Áp dụng WDA_EXCLUDEFROMCAPTURE ngay sau khi WebView2 khởi tạo xong
            // Tab mới có HWND mới chưa được protect — gọi ngay, không đợi Scanner tick 1 giây
            WindowProtection.EnableCaptureProtection(this);

            tab.WebView.CoreWebView2.SourceChanged += (s, e) =>
            {
                tab.Url = tab.WebView.Source.ToString();
                if (tab == _browserManager.TabManager.ActiveTab)
                {
                    txtAddressBar.Text = tab.Url;
                }
                _browserManager.ProfileManager.AddHistory(tab.Url);
            };

            tab.WebView.CoreWebView2.DocumentTitleChanged += (s, e) =>
            {
                tab.Title = tab.WebView.CoreWebView2.DocumentTitle;
                UpdateTabHeaderUI(tab);
            };

            tab.WebView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                ApplyThemeToTab(tab);
                try { if (_appSettings != null) tab.WebView.ZoomFactor = Math.Clamp(_appSettings.ZoomFactor, 0.01, 3.0); } catch { }
            };

            _browserManager.DownloadManager.RegisterDownloadEvents(tab.WebView.CoreWebView2);
            ApplyThemeToTab(tab);
        }

        private void BtnNewTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab("https://www.google.com");
        }

        #region Tab UI Handling
        private void TabManager_TabAdded(object? sender, BrowserTab tab)
        {
            var btnTab = new System.Windows.Controls.Button
            {
                Tag = tab,
                Content = CreateTabHeaderContent(tab),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59)),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 3, 0),
                Padding = new Thickness(6, 0, 4, 0),
                Height = 28,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105)),
                BorderThickness = new Thickness(1)
            };

            btnTab.Click += (s, e) => _browserManager.TabManager.SelectTab(tab);
            btnTab.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
                {
                    _browserManager.TabManager.CloseTab(tab);
                }
            };

            btnTab.MouseEnter += (s, e) =>
            {
                bool isLight = string.Equals(_appSettings?.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);
                var activeTab = _browserManager.TabManager.ActiveTab;
                bool isActive = (btnTab.Tag == activeTab);

                if (!isActive)
                {
                    if (isLight)
                    {
                        btnTab.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225));
                        btnTab.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
                        btnTab.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
                    }
                    else
                    {
                        btnTab.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59));
                        btnTab.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252));
                        btnTab.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
                    }

                    if (btnTab.Content is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is TextBlock txtTitle)
                            {
                                txtTitle.Foreground = btnTab.Foreground;
                            }
                        }
                    }
                }
            };

            btnTab.MouseLeave += (s, e) =>
            {
                UpdateAllTabStyles();
            };

            TabContainer.Children.Add(btnTab);
            UpdateAllTabStyles();
        }

        #region Dynamic DPI & Responsive Font Scaling
        private double GetDynamicScaleFactor()
        {
            double w = this.ActualWidth > 0 ? this.ActualWidth : 600;
            // Base window width is 600px. Scale factor smoothly ranges from 0.95 (narrow) up to 1.35 (maximized 1920px+)
            return Math.Clamp(0.95 + (w - 400.0) / 1400.0 * 0.4, 0.95, 1.35);
        }

        private double ScaledFont(double baseFontSize)
        {
            return Math.Round(baseFontSize * GetDynamicScaleFactor(), 1);
        }

        private void UpdateDynamicFontSize()
        {
            double scale = GetDynamicScaleFactor();
            double titleFontSize = Math.Round(11 * scale, 1);
            double addressFontSize = Math.Round(12 * scale, 1);
            double statusFontSize = Math.Round(11 * scale, 1);

            if (txtAddressBar != null) txtAddressBar.FontSize = addressFontSize;
            if (txtStatus != null) txtStatus.FontSize = statusFontSize;
            if (txtProxyBadge != null) txtProxyBadge.FontSize = statusFontSize;
            if (btnNewTab != null) btnNewTab.FontSize = Math.Round(15 * scale, 1);

            // Update Tab Titles FontSize dynamically
            if (TabContainer != null)
            {
                foreach (UIElement elem in TabContainer.Children)
                {
                    if (elem is System.Windows.Controls.Button btn && btn.Content is Grid grid)
                    {
                        btn.Height = Math.Round(28 * scale);
                        if (grid.Children.Count > 0 && grid.Children[0] is TextBlock txtTitle)
                        {
                            txtTitle.FontSize = titleFontSize;
                        }
                    }
                }
            }
        }
        #endregion

        private double GetTargetTabWidth()
        {
            double windowWidth = this.ActualWidth > 0 ? this.ActualWidth : 600;
            int tabCount = TabContainer?.Children?.Count ?? 1;
            if (tabCount <= 0) tabCount = 1;

            // Available width for tab buttons (window width minus ~150px for window controls and + button)
            double availableSpace = Math.Max(100, windowWidth - 150);
            double calculatedWidth = (availableSpace - (tabCount * 3)) / tabCount;
            return Math.Clamp(calculatedWidth, 55, 180);
        }

        private void UpdateTabHeaderWidths()
        {
            if (TabContainer == null) return;
            double targetTabWidth = GetTargetTabWidth();

            foreach (UIElement elem in TabContainer.Children)
            {
                if (elem is System.Windows.Controls.Button btn)
                {
                    btn.Width = targetTabWidth;
                }
            }
        }

        private UIElement CreateTabHeaderContent(BrowserTab tab)
        {
            var grid = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txtTitle = new TextBlock
            {
                Text = tab.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = ScaledFont(11),
                Margin = new Thickness(0, 0, 2, 0)
            };

            var btnClose = new System.Windows.Controls.Button
            {
                Content = "✕",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(0),
                Width = 16,
                Height = 16,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                ToolTip = "Đóng Tab (hoặc nhấp chuột giữa)",
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnClose.Click += (s, e) =>
            {
                e.Handled = true;
                _browserManager.TabManager.CloseTab(tab);
            };

            btnClose.MouseEnter += (s, e) =>
            {
                btnClose.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                btnClose.Foreground = System.Windows.Media.Brushes.White;
            };

            btnClose.MouseLeave += (s, e) =>
            {
                bool isLight = string.Equals(_appSettings?.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);
                btnClose.Background = System.Windows.Media.Brushes.Transparent;
                btnClose.Foreground = isLight ?
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            };

            Grid.SetColumn(txtTitle, 0);
            Grid.SetColumn(btnClose, 1);
            grid.Children.Add(txtTitle);
            grid.Children.Add(btnClose);

            return grid;
        }



        private void UpdateTabHeaderUI(BrowserTab tab)
        {
            foreach (UIElement elem in TabContainer.Children)
            {
                if (elem is System.Windows.Controls.Button btn && btn.Tag == tab)
                {
                    if (btn.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock txtTitle)
                    {
                        txtTitle.Text = tab.Title;
                    }
                    else
                    {
                        btn.Content = CreateTabHeaderContent(tab);
                    }
                    break;
                }
            }
        }

        private void UpdateAllTabStyles()
        {
            // 4. Update Tab Header Widths & Dynamic Font Scaling
            UpdateTabHeaderWidths();
            UpdateDynamicFontSize();
            string mode = _appSettings?.ThemeMode ?? "Dark";
            _themeManager?.UpdateAllTabStyles(TabContainer, _browserManager?.TabManager?.ActiveTab, mode, _currentWindowOpacity);
        }

        private async void TabManager_TabSelected(object? sender, BrowserTab tab)
        {
            txtAddressBar.Text = tab.Url;
            UpdateAllTabStyles();

            // Lazy restore: nếu tab bị discard (WebView đã dispose), recreate trước khi hiển thị
            if (tab.IsDiscarded || tab.IsRestoring)
            {
                await RestoreDiscardedTabAsync(tab);
                return; // RestoreDiscardedTabAsync tự cập nhật visual tree
            }

            if (tab.WebView != null)
            {
                WebViewHostGrid.Children.Clear();
                WebViewHostGrid.Children.Add(tab.WebView);
            }

            // Background Tab RAM & CPU Optimization: Suspend inactive tabs and set memory level low
            if (_browserManager?.TabManager?.Tabs != null)
            {
                foreach (var t in _browserManager.TabManager.Tabs)
                {
                    try
                    {
                        if (t.WebView != null && !t.IsDiscarded && !t.IsRestoring && t.WebView.CoreWebView2 != null)
                        {
                            if (t == tab)
                            {
                                t.WebView.CoreWebView2.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Normal;
                                t.WebView.CoreWebView2.Resume();
                            }
                            else
                            {
                                t.WebView.CoreWebView2.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low;
                                _ = t.WebView.CoreWebView2.TrySuspendAsync();
                            }
                        }
                    }
                    catch { }
                }
            }

            _ = UpdateProxyStatusBadgeAsync(tab);
        }

        /// <summary>
        /// Dispose toàn bộ WebView2 của tất cả tab để giải phóng RAM tối đa.
        /// URL được giữ lại trong tab.Url để restore sau.
        /// Gọi khi app ẩn xuống tray (F4).
        /// </summary>
        private async Task DiscardAllWebViewsAsync()
        {
            // Guard: tránh race condition nếu F4 được nhấn nhanh liên tiếp
            if (_isDiscarding) return;
            _isDiscarding = true;

            try
            {
                // Xóa khỏi visual tree trước
                WebViewHostGrid.Children.Clear();

                foreach (var tab in _browserManager.TabManager.Tabs)
                {
                    try
                    {
                        if (tab.WebView != null)
                        {
                            tab.WebView.Dispose();
                            tab.WebView = null!;
                        }
                        tab.IsDiscarded = true;
                        tab.IsRestoring = false;
                    }
                    catch { }
                }

                // GC Aggressive: app đã ẩn, user không thấy gì — dùng chế độ mạnh nhất để giải phóng tối đa
                await ResourceManager.OptimizeMemoryAsync(aggressive: true);
            }
            finally
            {
                _isDiscarding = false;
            }
        }

        /// <summary>
        /// Tạo lại WebView2 cho tab bị discard và navigate về URL cũ.
        /// Gọi khi user hiện app lại (F4) hoặc click tab bị discard.
        /// </summary>
        private async Task RestoreDiscardedTabAsync(BrowserTab tab)
        {
            if (tab == null || _activeEnvironment == null) return;
            if (!tab.IsDiscarded || tab.IsRestoring) return;

            tab.IsRestoring = true;
            string url = string.IsNullOrWhiteSpace(tab.Url) ? "https://www.google.com" : tab.Url;
            string savedTitle = tab.Title; // Lưu title cũ để restore nếu lỗi

            try
            {
                // Hiện trạng thái loading trên tab header người dùng biết tab đang khởi động lại
                tab.Title = "🔄 Đang tải...";
                UpdateTabHeaderUI(tab);

                // Tạo WebView2 mới
                var newWebView = new Microsoft.Web.WebView2.Wpf.WebView2();

                // CỰC KỲ QUAN TRỌNG: Đưa vào visual tree TRƯỚC KHI Initialize
                // để WPF HwndHost tạo HWND native handle đúng chuẩn
                if (tab == _browserManager.TabManager.ActiveTab)
                {
                    WebViewHostGrid.Children.Clear();
                    WebViewHostGrid.Children.Add(newWebView);
                }

                // Initialize với environment hiện tại
                await _webViewManager.InitializeWebViewAsync(newWebView, _activeEnvironment, url,
                    _browserManager.ProfileManager.CurrentProfile, _appSettings?.IsAudioMuted ?? true);

                tab.WebView = newWebView;
                tab.IsDiscarded = false;

                // STEALTH: Áp dụng WDA_EXCLUDEFROMCAPTURE ngay sau khi WebView2 mới khởi tạo
                WindowProtection.EnableCaptureProtection(this);

                // Đăng ký lại events (chỉ khi CoreWebView2 đã khởi tạo xong)
                if (newWebView.CoreWebView2 != null)
                {
                    newWebView.CoreWebView2.SourceChanged += (s, e) =>
                    {
                        if (newWebView.Source != null)
                        {
                            tab.Url = newWebView.Source.ToString();
                            if (tab == _browserManager.TabManager.ActiveTab)
                                txtAddressBar.Text = tab.Url;
                            _browserManager.ProfileManager.AddHistory(tab.Url);
                        }
                    };

                    newWebView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                    {
                        if (newWebView.CoreWebView2 != null)
                        {
                            tab.Title = newWebView.CoreWebView2.DocumentTitle;
                            UpdateTabHeaderUI(tab);
                        }
                    };

                    newWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                    {
                        ApplyThemeToTab(tab);
                        try { if (_appSettings != null && newWebView.CoreWebView2 != null) newWebView.ZoomFactor = Math.Clamp(_appSettings.ZoomFactor, 0.01, 3.0); } catch { }
                    };

                    _browserManager.DownloadManager.RegisterDownloadEvents(newWebView.CoreWebView2);
                }

                ApplyThemeToTab(tab);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RestoreDiscardedTab] Error: {ex.Message}");
                tab.Title = savedTitle; // Restore title cũ nếu lỗi
                tab.IsDiscarded = true;
                UpdateTabHeaderUI(tab);
            }
            finally
            {
                tab.IsRestoring = false;
            }
        }

        private void TabManager_TabClosed(object? sender, BrowserTab tab)
        {
            if (tab.WebView != null && WebViewHostGrid.Children.Contains(tab.WebView))
            {
                WebViewHostGrid.Children.Remove(tab.WebView);
            }

            System.Windows.Controls.Button? foundBtn = null;
            foreach (UIElement elem in TabContainer.Children)
            {
                if (elem is System.Windows.Controls.Button btn && btn.Tag == tab)
                {
                    foundBtn = btn;
                    break;
                }
            }
            if (foundBtn != null)
            {
                TabContainer.Children.Remove(foundBtn);
            }

            if (_browserManager.TabManager.Tabs.Count == 0)
            {
                AddNewTab("https://www.google.com");
            }
        }
        #endregion

        #region Navigation & Key Shortcuts
        private async Task SafeNavigateActiveTabAsync(string targetUrl, string statusMessage = "")
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active == null) return;

            if (!string.IsNullOrEmpty(statusMessage))
                txtStatus.Text = statusMessage;

            active.Url = targetUrl;

            if (active.IsDiscarded || active.IsRestoring || active.WebView == null || active.WebView.CoreWebView2 == null)
            {
                await RestoreDiscardedTabAsync(active);
            }
            else
            {
                _webViewManager.Navigate(active.WebView, targetUrl);
            }
        }

        private async void TxtAddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SafeNavigateActiveTabAsync(txtAddressBar.Text, "Đang chuyển trang...");
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
            {
                AddNewTab();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                var active = _browserManager.TabManager.ActiveTab;
                if (active != null)
                {
                    _browserManager.TabManager.CloseTab(active);
                }
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Tab)
            {
                _browserManager.TabManager.NextTab();
                e.Handled = true;
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null && !active.IsDiscarded && !active.IsRestoring && active.WebView?.CoreWebView2 != null && active.WebView.CanGoBack)
            {
                txtStatus.Text = "Đang quay lại trang trước...";
                active.WebView.GoBack();
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null && !active.IsDiscarded && !active.IsRestoring && active.WebView?.CoreWebView2 != null && active.WebView.CanGoForward)
            {
                txtStatus.Text = "Đang tiến tới trang tiếp...";
                active.WebView.GoForward();
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                if (active.IsDiscarded || active.IsRestoring || active.WebView == null || active.WebView.CoreWebView2 == null)
                {
                    await RestoreDiscardedTabAsync(active);
                }
                else
                {
                    txtStatus.Text = "Đang tải lại trang...";
                    try
                    {
                        active.WebView.Reload();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BtnRefresh] Error: {ex.Message}");
                    }
                }
            }
        }

        private async void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            await SafeNavigateActiveTabAsync("https://www.google.com", "Đang về Trang chủ Google...");
        }
        #endregion

        #region Private Screenshot & OCR Handler (Normal Visible Screenshot)
        private void HandleCtrlShiftSHotkey()
        {
            if (!this.IsVisible || this.WindowState == WindowState.Minimized)
            {
                // App is currently hidden in system tray or minimized:
                // Directly trigger stealth regional crop selection overlay over desktop!
                _screenshotManager.TriggerRegionalCapture();
            }
            else
            {
                // App is visible: Toggle screenshot options modal
                ToggleScreenshotModal();
            }
        }

        private void ToggleScreenshotModal()
        {
            bool wasOpen = ScreenshotModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : ScreenshotModal);
            ScreenshotModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            UpdateModalVisibilities();
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            ToggleScreenshotModal();
        }

        private void BtnCloseScreenshotModal_Click(object sender, RoutedEventArgs e)
        {
            ToggleScreenshotModal();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;

        private async void BtnCropScreenshot_Click(object sender, RoutedEventArgs e)
        {
            ScreenshotModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();

            txtStatus.Text = "Ứng dụng tạm tàng hình... Kéo thả chuột để chọn vùng màn hình phía sau cần chụp";

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            try
            {
                // 1. Đánh dấu tạm ẩn để chụp ảnh — ngăn IsVisibleChanged trigger DiscardAllWebViewsAsync
                _isTemporaryHide = true;

                // 2. Hide native WebView2 HWND & minimize/hide main WPF window completely via Win32
                WebViewHostGrid.Visibility = Visibility.Hidden;
                this.WindowState = WindowState.Minimized;
                this.Hide();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_HIDE);

                // 3. Allow DWM compositor 300ms to completely unmap window surface from desktop composite
                await Task.Delay(300);

                // 4. Trigger regional crop capture overlay (Overlay is protected from screen recorders)
                _screenshotManager.TriggerRegionalCapture();
            }
            finally
            {
                // 5. Reset flag trước khi Show() để tránh bất kỳ trigger nào sau này
                _isTemporaryHide = false;

                // 6. STRICT STEALTH: Re-enforce 100% protection FIRST before restoring window visibility!
                WindowProtection.EnableCaptureProtection(this);

                // 7. Unhide main WPF window & restore window state
                WebViewHostGrid.Visibility = Visibility.Visible;
                this.Show();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_RESTORE);
                this.WindowState = WindowState.Normal;
                this.Activate();
                UpdateModalVisibilities();
            }
        }

        private async void BtnFullScreenScreenshot_Click(object sender, RoutedEventArgs e)
        {
            ScreenshotModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();

            txtStatus.Text = "Ứng dụng tạm tàng hình... Đang chụp toàn bộ màn hình phía sau app";

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            try
            {
                // 1. Đánh dấu tạm ẩn để chụp ảnh — ngăn IsVisibleChanged trigger DiscardAllWebViewsAsync
                _isTemporaryHide = true;

                // 2. Hide native WebView2 HWND & minimize/hide main WPF window completely via Win32
                WebViewHostGrid.Visibility = Visibility.Hidden;
                this.WindowState = WindowState.Minimized;
                this.Hide();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_HIDE);

                // 3. Allow DWM compositor 300ms to completely unmap window surface from desktop composite
                await Task.Delay(300);

                // 4. Trigger full screen capture
                _screenshotManager.TriggerFullScreenCapture();
            }
            finally
            {
                // 5. Reset flag trước khi Show()
                _isTemporaryHide = false;

                // 6. STRICT STEALTH: Re-enforce 100% protection FIRST before restoring window visibility!
                WindowProtection.EnableCaptureProtection(this);

                // 7. Unhide main WPF window & restore window state
                WebViewHostGrid.Visibility = Visibility.Visible;
                this.Show();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_RESTORE);
                this.WindowState = WindowState.Normal;
                this.Activate();
                UpdateModalVisibilities();
            }
        }



        private void OnScreenshotCaptured(object? sender, Bitmap ramBitmap)
        {
            // Thread-Safe Dispatcher call for UI updates from WinForms dialog thread
            Dispatcher.Invoke(async () =>
            {
                // STRICT PRIVACY: Retain ONLY in RAM, and synchronize to System Clipboard so Ctrl+V works!
                PrivateClipboardManager.SetPrivateBitmap(ramBitmap);

                // Extract text with Windows OCR
                string ocrText = await _ocrManager.ExtractTextFromBitmapAsync(ramBitmap);

                txtStatus.Text = "Đã chụp ảnh màn hình thành công (Đã copy vào Clipboard - Có thể Ctrl+V dán ngay)";

                if (!this.IsVisible || this.WindowState == WindowState.Minimized)
                {
                    _trayManager.ShowNotification("📷 Đã Chụp Màn Hình (RAM & Clipboard)", "Ảnh đã lưu RAM & copy vào Clipboard. Nhấn Ctrl+V để DÁN ngay!");
                }
                else
                {
                    ShowAppNotification($"Đã chụp ảnh màn hình thành công!\n(Ảnh được lưu RAM & Đã copy vào Clipboard, nhấn Ctrl+V để DÁN ngay!)\n\nChữ trích xuất từ OCR:\n{(string.IsNullOrWhiteSpace(ocrText) ? "(Không phát hiện chữ)" : ocrText)}", "Đã Chụp Màn Hình (Sẵn sàng Ctrl+V)");
                }
            });
        }
        #endregion

        #region User Guide & Help Modal Handlers
        private void ToggleHelpModal()
        {
            bool wasOpen = HelpModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : HelpModal);
            HelpModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            UpdateModalVisibilities();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            ToggleHelpModal();
        }

        private void BtnCloseHelpModal_Click(object sender, RoutedEventArgs e)
        {
            ToggleHelpModal();
        }
        #endregion



        #region Google Account Manager Modal & Profile Badges
        private void ToggleGoogleAccountModal()
        {
            bool wasOpen = GoogleAccountModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : GoogleAccountModal);
            GoogleAccountModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            UpdateModalVisibilities();
        }

        private void GoogleAccountBadge_MouseDown(object sender, MouseButtonEventArgs e) => ToggleGoogleAccountModal();
        private void BtnCloseGoogleAccountModal_Click(object sender, RoutedEventArgs e) => ToggleGoogleAccountModal();

        private async void BtnGoogleSignIn_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            await SafeNavigateActiveTabAsync("https://accounts.google.com/", "Đang chuyển tới trang Đăng nhập Google...");
        }

        private async void BtnOpenChatGPT_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            await SafeNavigateActiveTabAsync("https://chatgpt.com/", "Đang mở ChatGPT (https://chatgpt.com/)...");
        }

        private async void BtnOpenGemini_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            await SafeNavigateActiveTabAsync("https://gemini.google.com/app", "Đang mở Google Gemini (https://gemini.google.com/app)...");
        }


        private async void BtnPersonalProfile_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            var personal = _browserManager.ProfileManager.Profiles.FirstOrDefault(p => !p.IsGuest);
            if (personal != null)
            {
                _browserManager.ProfileManager.SwitchProfile(personal);
                await ReinitializeProfileEnvironmentAsync();
                AddNewTab("https://www.google.com");
                ShowAppNotification("Đã chuyển sang Profile Cá nhân.", "Đã Chuyển Profile");
            }
        }

        private async void BtnGuestProfile_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            var guest = _browserManager.ProfileManager.CreateGuestProfile();
            _browserManager.ProfileManager.SwitchProfile(guest);
            await ReinitializeProfileEnvironmentAsync();
            AddNewTab("https://www.google.com");
            ShowAppNotification("Đã chuyển sang Profile Khách (Không lưu lịch sử/cookie).", "Profile Khách Hoạt Động");
        }

        private async void BtnClearGoogleCache_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            await SecureClearManager.ClearCookiesAndSessionDataAsync(_browserManager);
            ShowAppNotification("Đã xóa sạch Cookie, Session Storage, LocalStorage & Cache thành công!", "Đã Dọn Dẹp Sạch Sẽ");
        }
        #endregion






        #region Action Buttons & Settings
        private void BtnBookmark_Click(object sender, RoutedEventArgs e)
        {
            bool wasOpen = BookmarksModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : BookmarksModal);
            BookmarksModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (BookmarksModal.Visibility == Visibility.Visible)
            {
                RefreshBookmarksUI();
            }
            UpdateModalVisibilities();
        }

        private void BtnCloseBookmarksModal_Click(object sender, RoutedEventArgs e)
        {
            BookmarksModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        private void BtnAddCurrentBookmark_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null && !string.IsNullOrWhiteSpace(active.Url))
            {
                _browserManager.ProfileManager.AddBookmark(active.Url);
                RefreshBookmarksUI();
                ShowAppNotification($"Đã thêm trang vào Danh sách Yêu thích:\n{active.Url}", "★ Thêm Thành Công");
            }
        }

        private void RefreshBookmarksUI()
        {
            if (BookmarksListContainer == null) return;
            BookmarksListContainer.Children.Clear();

            var bookmarks = _browserManager.ProfileManager.CurrentProfile.Bookmarks;
            if (bookmarks.Count == 0)
            {
                BookmarksListContainer.Children.Add(new TextBlock
                {
                    Text = "Chưa có trang yêu thích nào.",
                    FontSize = 11,
                    Opacity = 0.7,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 10)
                });
                return;
            }

            bool isLight = string.Equals(_appSettings?.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);

            foreach (var url in bookmarks)
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 0, 4),
                    BorderThickness = new Thickness(1),
                    Background = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(241, 245, 249) : System.Windows.Media.Color.FromRgb(30, 41, 59)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(203, 213, 225) : System.Windows.Media.Color.FromRgb(51, 65, 85))
                };

                var dock = new DockPanel();

                var btnDelete = new System.Windows.Controls.Button
                {
                    Content = "🗑",
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    FontSize = 10,
                    ToolTip = "Xóa khỏi danh sách"
                };
                DockPanel.SetDock(btnDelete, Dock.Right);

                string currentUrl = url;
                btnDelete.Click += (s, e) =>
                {
                    _browserManager.ProfileManager.RemoveBookmark(currentUrl);
                    RefreshBookmarksUI();
                };

                var btnNav = new System.Windows.Controls.Button
                {
                    Content = currentUrl,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                btnNav.Click += async (s, e) =>
                {
                    BookmarksModal.Visibility = Visibility.Collapsed;
                    UpdateModalVisibilities();
                    await SafeNavigateActiveTabAsync(currentUrl);
                };

                dock.Children.Add(btnDelete);
                dock.Children.Add(btnNav);
                card.Child = dock;

                BookmarksListContainer.Children.Add(card);
            }
        }

        private void BtnDownloads_Click(object sender, RoutedEventArgs e)
        {
            bool wasOpen = DownloadsModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : DownloadsModal);
            DownloadsModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (DownloadsModal.Visibility == Visibility.Visible)
            {
                RefreshDownloadsUI();
            }
            UpdateModalVisibilities();
        }

        private void BtnCloseDownloadsModal_Click(object sender, RoutedEventArgs e)
        {
            DownloadsModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        private void BtnOpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        {
            _browserManager.DownloadManager.OpenDownloadFolder();
        }

        private void RefreshDownloadsUI()
        {
            if (DownloadsListContainer == null) return;
            DownloadsListContainer.Children.Clear();

            var downloads = _browserManager.DownloadManager.Downloads;
            if (downloads.Count == 0)
            {
                DownloadsListContainer.Children.Add(new TextBlock
                {
                    Text = "Chưa có file nào được tải xuống.",
                    FontSize = 11,
                    Opacity = 0.7,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 10)
                });
                return;
            }

            bool isLight = string.Equals(_appSettings?.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);

            foreach (var item in downloads.Reverse())
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 6),
                    BorderThickness = new Thickness(1),
                    Background = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(241, 245, 249) : System.Windows.Media.Color.FromRgb(30, 41, 59)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(203, 213, 225) : System.Windows.Media.Color.FromRgb(51, 65, 85))
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var txtName = new TextBlock
                {
                    Text = $"📄 {item.FileName}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = item.ResultFilePath
                };
                Grid.SetRow(txtName, 0);

                var buttonsPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                Grid.SetRow(buttonsPanel, 1);

                var btnOpenFile = new System.Windows.Controls.Button
                {
                    Content = "📄 Mở File",
                    FontSize = 10,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };
                btnOpenFile.Click += (s, e) => _browserManager.DownloadManager.OpenFile(item.ResultFilePath);

                var btnShowFolder = new System.Windows.Controls.Button
                {
                    Content = "📂 Xem Thư Mục",
                    FontSize = 10,
                    Padding = new Thickness(6, 2, 6, 2)
                };
                btnShowFolder.Click += (s, e) => _browserManager.DownloadManager.ShowInFolder(item.ResultFilePath);

                buttonsPanel.Children.Add(btnOpenFile);
                buttonsPanel.Children.Add(btnShowFolder);

                grid.Children.Add(txtName);
                grid.Children.Add(buttonsPanel);
                card.Child = grid;

                DownloadsListContainer.Children.Add(card);
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            bool wasOpen = SettingsModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : SettingsModal);
            SettingsModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsModal.Visibility == Visibility.Visible)
            {
                txtCleanupStatus.Text = "";
                if (txtSaveSettingsStatus != null) txtSaveSettingsStatus.Text = "";
                sldOpacity.Value = _currentWindowOpacity;
                txtOpacityPercent.Text = $"{(int)(_currentWindowOpacity * 100)}% ({((_currentWindowOpacity >= 0.98) ? "Không Trong Suốt" : "Glass Mode")})";

                if (txtStartupWidth != null) txtStartupWidth.Text = _appSettings.StartupWidth.ToString();
                if (txtStartupHeight != null) txtStartupHeight.Text = _appSettings.StartupHeight.ToString();
                if (sldZoom != null) sldZoom.Value = _appSettings.ZoomFactor;
                if (txtZoomPercent != null) txtZoomPercent.Text = $"{(int)(_appSettings.ZoomFactor * 100)}%";
                if (chkMuteAudio != null) chkMuteAudio.IsChecked = _appSettings.IsAudioMuted;
            }
            UpdateModalVisibilities();
        }

        private void BtnCloseSettingsModal_Click(object sender, RoutedEventArgs e)
        {
            SettingsModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        #region Proxy Modal & Tab Proxy Management
        private void BtnProxyFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            ToggleProxyModal();
        }

        private void ToggleProxyModal()
        {
            bool wasOpen = ProxyModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : ProxyModal);
            ProxyModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (ProxyModal.Visibility == Visibility.Visible)
            {
                var active = _browserManager.TabManager.ActiveTab;
                if (active != null)
                {
                    txtProxyInput.Text = active.Proxy?.RawInput ?? "";
                    if (active.Proxy != null && active.Proxy.IsEnabled)
                    {
                        txtProxyStatus.Text = $"🌐 Proxy: {active.Proxy}";
                        txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
                        BtnPingProxy_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        txtProxyStatus.Text = "Chưa đặt Proxy cho Tab này";
                        txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
                    }
                }
            }
            UpdateModalVisibilities();
        }

        private void BtnCloseProxyModal_Click(object sender, RoutedEventArgs e)
        {
            ProxyModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        private async void BtnApplyProxy_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active == null) return;

            string input = txtProxyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                txtProxyStatus.Text = "⚠️ Vui lòng nhập Proxy (ip:port hoặc ip:port:user:pass)";
                txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                return;
            }

            active.Proxy = ProxyModel.Parse(input);
            await ApplyTabProxyAsync(active);
            _ = UpdateProxyStatusBadgeAsync(active);

            txtProxyStatus.Text = $"✅ Đã áp dụng Proxy: {active.Proxy}";
            txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
            ShowAppNotification($"Đã áp dụng Proxy thành công cho Tab hiện tại:\n{active.Proxy}", "🌐 Áp Dụng Proxy");
        }

        private async void BtnPingProxy_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            string input = txtProxyInput.Text.Trim();
            var proxy = string.IsNullOrWhiteSpace(input) ? active?.Proxy : ProxyModel.Parse(input);

            if (proxy == null || !proxy.IsEnabled)
            {
                txtProxyStatus.Text = "⚠️ Chưa nhập Proxy để test ping.";
                txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                return;
            }

            txtProxyStatus.Text = $"⚡ Đang kết nối tới {proxy.Server}...";
            txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));

            var (host, port) = proxy.GetHostAndPort();
            long ping = await ProxyPingHelper.PingProxyAsync(host, port);

            if (ping > 0)
            {
                txtProxyStatus.Text = $"🟢 Proxy Hoạt Động tốt (Độ trễ Ping: {ping} ms)";
                txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
            }
            else
            {
                txtProxyStatus.Text = $"🔴 Proxy Không Phản Hồi (Timeout / Sai IP/Port)";
                txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
            }
        }

        private async void BtnClearProxy_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active == null) return;

            active.Proxy = new ProxyModel();
            txtProxyInput.Text = "";
            await ApplyTabProxyAsync(active);
            _ = UpdateProxyStatusBadgeAsync(active);

            txtProxyStatus.Text = "❌ Đã tắt Proxy cho Tab hiện tại.";
            txtProxyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
            ShowAppNotification("Đã tắt Proxy cho Tab hiện tại.", "Tắt Proxy");
        }

        private async System.Threading.Tasks.Task UpdateProxyStatusBadgeAsync(BrowserTab? tab)
        {
            if (txtProxyBadge == null) return;

            if (tab == null || tab.Proxy == null || !tab.Proxy.IsEnabled)
            {
                txtProxyBadge.Visibility = Visibility.Collapsed;
                txtProxyBadge.Text = "";
                return;
            }

            txtProxyBadge.Visibility = Visibility.Visible;
            txtProxyBadge.Text = $"🌐 Proxy: {tab.Proxy.Server} (⚡...)";
            txtProxyBadge.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));

            var (host, port) = tab.Proxy.GetHostAndPort();
            long ping = await ProxyPingHelper.PingProxyAsync(host, port);
            tab.PingMs = ping;

            if (tab == _browserManager.TabManager.ActiveTab)
            {
                if (ping > 0)
                {
                    txtProxyBadge.Text = $"🌐 Proxy: {tab.Proxy.Server} (🟢 {ping}ms)";
                    txtProxyBadge.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
                }
                else
                {
                    txtProxyBadge.Text = $"🌐 Proxy: {tab.Proxy.Server} (🔴 Offline)";
                    txtProxyBadge.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
                }
            }
        }

        private async System.Threading.Tasks.Task ApplyTabProxyAsync(BrowserTab tab)
        {
            if (tab == null) return;

            try
            {
                var profile = _browserManager.ProfileManager.CurrentProfile;
                string? proxyServer = tab.Proxy != null && tab.Proxy.IsEnabled ? tab.Proxy.Server : null;
                var env = await _browserManager.CreateEnvironmentForProfileAsync(profile, proxyServer);

                await RecreateTabWebViewWithEnvironmentAsync(tab, env);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyTabProxyAsync error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task RecreateTabWebViewWithEnvironmentAsync(BrowserTab tab, Microsoft.Web.WebView2.Core.CoreWebView2Environment env)
        {
            if (tab == null || env == null) return;
            string url = string.IsNullOrWhiteSpace(tab.Url) ? "https://www.google.com" : tab.Url;

            try
            {
                if (tab.WebView != null)
                {
                    WebViewHostGrid.Children.Remove(tab.WebView);
                    try { tab.WebView.Dispose(); } catch { }
                }

                var newWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
                tab.WebView = newWebView;

                WebViewHostGrid.Children.Add(newWebView);

                await _webViewManager.InitializeWebViewAsync(newWebView, env, url, _browserManager.ProfileManager.CurrentProfile, _appSettings?.IsAudioMuted ?? true);

                // STEALTH: Áp dụng WDA_EXCLUDEFROMCAPTURE ngay sau khi WebView2 mới khởi tạo
                // Proxy change tạo WebView2 mới với HWND mới — protect ngay, không đợi Scanner
                WindowProtection.EnableCaptureProtection(this);

                // Subscribe to BasicAuthenticationRequested for proxy user:pass
                newWebView.CoreWebView2.BasicAuthenticationRequested += (sender, args) =>
                {
                    if (tab.Proxy != null && !string.IsNullOrEmpty(tab.Proxy.Username))
                    {
                        args.Response.UserName = tab.Proxy.Username;
                        args.Response.Password = tab.Proxy.Password;
                    }
                };

                newWebView.CoreWebView2.SourceChanged += (s, e) =>
                {
                    tab.Url = newWebView.Source.ToString();
                    if (tab == _browserManager.TabManager.ActiveTab)
                    {
                        txtAddressBar.Text = tab.Url;
                    }
                    _browserManager.ProfileManager.AddHistory(tab.Url);
                };

                newWebView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    tab.Title = newWebView.CoreWebView2.DocumentTitle;
                    UpdateTabHeaderUI(tab);
                };

                newWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    ApplyThemeToTab(tab);
                    try { if (_appSettings != null) newWebView.ZoomFactor = Math.Clamp(_appSettings.ZoomFactor, 0.01, 3.0); } catch { }
                };

                _browserManager.DownloadManager.RegisterDownloadEvents(newWebView.CoreWebView2);
                ApplyThemeToTab(tab);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecreateTabWebViewWithEnvironmentAsync error: {ex.Message}");
            }
        }
        #endregion

        #region Overflow Menu Handlers
        private void BtnOverflowMenu_Click(object sender, RoutedEventArgs e)
        {
            bool wasOpen = OverflowMenuModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : OverflowMenuModal);
            OverflowMenuModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            UpdateModalVisibilities();
        }

        private void BtnBookmarkFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            BtnBookmark_Click(sender, e);
        }

        private void BtnExtensionStoreFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            ToggleExtensionsModal();
        }

        private void ToggleExtensionsModal()
        {
            bool wasOpen = ExtensionsModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : ExtensionsModal);
            ExtensionsModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (ExtensionsModal.Visibility == Visibility.Visible)
            {
                RefreshExtensionsUI();
            }
            UpdateModalVisibilities();
        }

        private void BtnCloseExtensionsModal_Click(object sender, RoutedEventArgs e)
        {
            ExtensionsModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        private void BtnOpenExtensionsFolder_Click(object sender, RoutedEventArgs e)
        {
            var profile = _browserManager.ProfileManager.CurrentProfile;
            ExtensionManager.OpenExtensionFolder(profile.UserDataFolder);
        }

        private void BtnOpenChromeStore_Click(object sender, RoutedEventArgs e)
        {
            ExtensionsModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                _webViewManager.Navigate(active.WebView, "https://chromewebstore.google.com");
            }
        }

        private void RefreshExtensionsUI()
        {
            if (ExtensionsListContainer == null) return;
            ExtensionsListContainer.Children.Clear();

            var profile = _browserManager.ProfileManager.CurrentProfile;
            var list = ExtensionManager.GetInstalledExtensions(profile.UserDataFolder);

            if (list.Count == 0)
            {
                ExtensionsListContainer.Children.Add(new TextBlock
                {
                    Text = "Chưa có Extension nào. Bấm '📂 Thư Mục Extensions' để thả thư mục extension vào.",
                    FontSize = 11,
                    Opacity = 0.7,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            bool isLight = string.Equals(_appSettings?.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);

            foreach (var item in list)
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 6),
                    BorderThickness = new Thickness(1),
                    Background = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(241, 245, 249) : System.Windows.Media.Color.FromRgb(30, 41, 59)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(203, 213, 225) : System.Windows.Media.Color.FromRgb(51, 65, 85))
                };

                var stack = new StackPanel();

                var txtName = new TextBlock
                {
                    Text = $"🧩 {item.Name} (v{item.Version})",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248))
                };

                var txtDesc = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(item.Description) ? "Extension chạy trực tiếp trên Chromium." : item.Description,
                    FontSize = 10,
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                stack.Children.Add(txtName);
                stack.Children.Add(txtDesc);
                card.Child = stack;

                ExtensionsListContainer.Children.Add(card);
            }
        }

        #region Overflow Menu Handlers

        private void BtnScreenshotFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            BtnScreenshot_Click(sender, e);
        }

        private void BtnDownloadsFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            BtnDownloads_Click(sender, e);
        }

        private void BtnSettingsFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            BtnSettings_Click(sender, e);
        }

        private void BtnHelpFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            BtnHelp_Click(sender, e);
        }

        private void BtnGoogleAccountFromOverflow_Click(object sender, RoutedEventArgs e)
        {
            OverflowMenuModal.Visibility = Visibility.Collapsed;
            GoogleAccountModal.Visibility = Visibility.Visible;
            UpdateModalVisibilities();
        }
        #endregion

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x00000002;

        private double _currentWindowOpacity = 1.0;

        public void ApplyWindowOpacity(double opacity)
        {
            _currentWindowOpacity = Math.Clamp(opacity, 0.15, 1.0);
            this.Opacity = 1.0; // Keep WPF window at 1.0 so buttons and modals render 100% solid

            if (!_isInitializingTheme)
            {
                ApplyTheme(_appSettings?.ThemeMode ?? "Dark");
            }
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtOpacityPercent != null)
            {
                ApplyWindowOpacity(e.NewValue);
                txtOpacityPercent.Text = $"{(int)(e.NewValue * 100)}% ({((e.NewValue >= 0.98) ? "Không Trong Suốt" : "Glass Mode")})";
                if (txtSaveSettingsStatus != null)
                {
                    txtSaveSettingsStatus.Text = "⏳ Chưa lưu";
                    txtSaveSettingsStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                }
            }
        }

        private void SldZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtZoomPercent != null)
            {
                txtZoomPercent.Text = $"{(int)(e.NewValue * 100)}%";
                ApplyZoomFactor(e.NewValue);
                if (txtSaveSettingsStatus != null)
                {
                    txtSaveSettingsStatus.Text = "⏳ Chưa lưu";
                    txtSaveSettingsStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                }
            }
        }

        public void ApplyZoomFactor(double zoom)
        {
            if (_browserManager?.TabManager?.Tabs != null)
            {
                foreach (var tab in _browserManager.TabManager.Tabs)
                {
                    try
                    {
                        if (tab.WebView != null && !tab.IsDiscarded && !tab.IsRestoring && tab.WebView.CoreWebView2 != null)
                        {
                            tab.WebView.ZoomFactor = Math.Clamp(zoom, 0.01, 3.0);
                        }
                    }
                    catch { }
                }
            }
        }

        private void ChkMuteAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingTheme || chkMuteAudio == null || _appSettings == null) return;
            bool isMuted = chkMuteAudio.IsChecked == true;
            ApplyAudioMuteSetting(isMuted);
            _appSettings.IsAudioMuted = isMuted;
            if (txtSaveSettingsStatus != null)
            {
                txtSaveSettingsStatus.Text = "⏳ Chưa lưu";
                txtSaveSettingsStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
            }
        }

        private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingTheme || chkAlwaysOnTop == null || _appSettings == null) return;
            bool isAlwaysOnTop = chkAlwaysOnTop.IsChecked == true;
            this.Topmost = isAlwaysOnTop;
            _appSettings.IsAlwaysOnTop = isAlwaysOnTop;
            if (txtSaveSettingsStatus != null)
            {
                txtSaveSettingsStatus.Text = "⏳ Chưa lưu";
                txtSaveSettingsStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
            }
        }

        public void ApplyAudioMuteSetting(bool isMuted)
        {
            if (_browserManager?.TabManager?.Tabs != null)
            {
                foreach (var tab in _browserManager.TabManager.Tabs)
                {
                    try
                    {
                        if (tab.WebView != null && !tab.IsDiscarded && !tab.IsRestoring && tab.WebView.CoreWebView2 != null)
                        {
                            tab.WebView.CoreWebView2.IsMuted = isMuted;
                        }
                    }
                    catch { }
                }
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _appSettings.WindowOpacity = sldOpacity.Value;
            _appSettings.ThemeMode = (rbLightTheme?.IsChecked == true) ? "Light" : "Dark";

            if (txtStartupWidth != null && double.TryParse(txtStartupWidth.Text, out double w) && w >= 200)
            {
                _appSettings.StartupWidth = w;
                this.Width = w;
            }
            if (txtStartupHeight != null && double.TryParse(txtStartupHeight.Text, out double h) && h >= 150)
            {
                _appSettings.StartupHeight = h;
                this.Height = h;
            }

            if (sldZoom != null)
            {
                _appSettings.ZoomFactor = sldZoom.Value;
                ApplyZoomFactor(sldZoom.Value);
            }

            if (chkMuteAudio != null)
            {
                _appSettings.IsAudioMuted = chkMuteAudio.IsChecked == true;
                ApplyAudioMuteSetting(_appSettings.IsAudioMuted);
            }

            if (chkAlwaysOnTop != null)
            {
                _appSettings.IsAlwaysOnTop = chkAlwaysOnTop.IsChecked == true;
                this.Topmost = _appSettings.IsAlwaysOnTop;
            }

            _appSettings.SaveConfig();

            if (txtSaveSettingsStatus != null)
            {
                txtSaveSettingsStatus.Text = "✅ Đã lưu!";
                txtSaveSettingsStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
            }
            ShowAppNotification("Đã lưu cấu hình thành công!", "Đã Lưu Cài Đặt");
        }

        private void BtnPresetSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tagStr)
            {
                var parts = tagStr.Split(',');
                if (parts.Length == 2 && txtStartupWidth != null && txtStartupHeight != null)
                {
                    txtStartupWidth.Text = parts[0];
                    txtStartupHeight.Text = parts[1];
                }
            }
        }

        private void BtnPresetCurrentSize_Click(object sender, RoutedEventArgs e)
        {
            if (txtStartupWidth != null && txtStartupHeight != null)
            {
                txtStartupWidth.Text = Math.Round(this.ActualWidth).ToString();
                txtStartupHeight.Text = Math.Round(this.ActualHeight).ToString();
            }
        }

        private bool _isInitializingTheme = false;

        private void RbTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializingTheme || rbDarkTheme == null || rbLightTheme == null || _appSettings == null) return;
            string selectedTheme = (rbLightTheme.IsChecked == true) ? "Light" : "Dark";
            ApplyTheme(selectedTheme);
            _appSettings.ThemeMode = selectedTheme;
            _appSettings.SaveConfig();
        }

        private void ApplyTheme(string themeMode)
        {
            if (_isInitializingTheme || _themeManager == null) return;
            _isInitializingTheme = true;
            try
            {
                if (_appSettings != null)
                {
                    _appSettings.ThemeMode = themeMode;
                    Border[] modals = { SettingsModal, BookmarksModal, DownloadsModal, ProxyModal, ExtensionsModal, GoogleAccountModal, HelpModal, ScreenshotModal, NotificationModal, OverflowMenuModal };
                    _themeManager.ApplyTheme(themeMode, _currentWindowOpacity, _browserManager, _appSettings, modals);
                    if (chkAlwaysOnTop != null) chkAlwaysOnTop.IsChecked = _appSettings.IsAlwaysOnTop;
                }
            }
            finally
            {
                _isInitializingTheme = false;
            }
        }

        private void ApplyThemeToTab(BrowserTab tab)
        {
            _themeManager.ApplyThemeToTab(tab, _appSettings?.ThemeMode ?? "Dark", _currentWindowOpacity);
        }

        private async void BtnDoCleanup_Click(object sender, RoutedEventArgs e)
        {
            await SecureClearManager.ClearCookiesAndSessionDataAsync(_browserManager);
            UpdateMemoryDisplay();
            txtCleanupStatus.Text = "✅ Đã xóa sạch Cookie, Session, RAM & Cache bảo mật!";
            ShowAppNotification("Đã xóa an toàn toàn bộ Cookie, Session Storage, RAM Bitmaps & Cache nhạy cảm!", "Đã Dọn Dẹp Bảo Mật");
        }

        private async void UpdateMemoryDisplay()
        {
            // GC chạy trên background thread; sau await tự quay về UI thread — không cần Dispatcher.Invoke
            await ResourceManager.OptimizeMemoryAsync();
            txtMemoryUsage.Text = $"RAM: {ResourceManager.GetWorkingSetMemoryMB()} MB";
        }
        #endregion

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Shutdown low-level OS screenshot hook
            OSScreenshotDetector.Shutdown();

            // Save tab session for crash/restart recovery
            var urls = _browserManager.TabManager.Tabs.Select(t => t.Url).ToList();
            int activeIndex = _browserManager.TabManager.Tabs.IndexOf(_browserManager.TabManager.ActiveTab ?? _browserManager.TabManager.Tabs.FirstOrDefault()!);
            SessionManager.SaveSession(urls, activeIndex);

            // Secure memory wipe
            SecureClearManager.ClearSensitiveData();

            _hotkeyManager.UnregisterHotkeys();
            _trayManager.Dispose();
        }
        #endregion
    }
}

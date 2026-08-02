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

        private CoreWebView2Environment? _activeEnvironment;
        private TaskCompletionSource<bool>? _notificationDialogTask;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 0. Load saved App Settings (Opacity, Theme)
            _appSettings.LoadConfig();
            if (_appSettings.WindowOpacity >= 0.1 && _appSettings.WindowOpacity <= 1.0)
            {
                ApplyWindowOpacity(_appSettings.WindowOpacity);
            }
            ApplyTheme(_appSettings.ThemeMode);

            // 1. Initialize Tray Icon & Global Hotkeys
            _trayManager.Initialize(this);

            _hotkeyManager.RegisterGlobalHotkeys(this);

            // 2. Enforce Continuous Protection Hook
            this.ShowInTaskbar = true;
            WindowProtection.CurrentMode = ProtectionMode.FullStealth;
            WindowProtection.RegisterContinuousProtectionHook(this);
            OSScreenshotDetector.Initialize(this);
            Activated += (s, e) => { this.ShowInTaskbar = true; if (!WindowProtection.IsProtectionDisabledTemporarily) WindowProtection.EnableCaptureProtection(this); };
            StateChanged += (s, e) => { this.ShowInTaskbar = true; if (!WindowProtection.IsProtectionDisabledTemporarily) WindowProtection.EnableCaptureProtection(this); };
            IsVisibleChanged += (s, e) => { this.ShowInTaskbar = true; if (IsVisible && !WindowProtection.IsProtectionDisabledTemporarily) WindowProtection.EnableCaptureProtection(this); };

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

            UpdateMemoryDisplay();
        }

        #region Window Event Handlers
        private void Window_StateChanged(object sender, EventArgs e)
        {
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }
        #endregion

        private void CloseAllModalsExcept(UIElement? activeModal = null)
        {
            if (GoogleAccountModal != null && GoogleAccountModal != activeModal) GoogleAccountModal.Visibility = Visibility.Collapsed;
            if (BookmarksModal != null && BookmarksModal != activeModal) BookmarksModal.Visibility = Visibility.Collapsed;
            if (ScreenshotModal != null && ScreenshotModal != activeModal) ScreenshotModal.Visibility = Visibility.Collapsed;
            if (SettingsModal != null && SettingsModal != activeModal) SettingsModal.Visibility = Visibility.Collapsed;
            if (HelpModal != null && HelpModal != activeModal) HelpModal.Visibility = Visibility.Collapsed;
            if (NotificationModal != null && NotificationModal != activeModal) NotificationModal.Visibility = Visibility.Collapsed;
        }

        private void UpdateModalVisibilities()
        {
            // WPF Airspace fix: Hide native WebView2 HWND when WPF overlay modals are active
            bool isAnyModalOpen = (GoogleAccountModal.Visibility == Visibility.Visible) ||
                                  (BookmarksModal.Visibility == Visibility.Visible) ||
                                  (ScreenshotModal.Visibility == Visibility.Visible) ||
                                  (SettingsModal.Visibility == Visibility.Visible) ||
                                  (HelpModal.Visibility == Visibility.Visible) ||
                                  (NotificationModal.Visibility == Visibility.Visible);

            WebViewHostGrid.Visibility = isAnyModalOpen ? Visibility.Hidden : Visibility.Visible;
        }

        private async System.Threading.Tasks.Task ReinitializeProfileEnvironmentAsync()
        {
            try
            {
                var profile = _browserManager.ProfileManager.CurrentProfile;
                txtProfileName.Text = profile.IsGuest ? "Profile Khách" : "Tài khoản Google";
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

                await _webViewManager.InitializeWebViewAsync(newWebView, _activeEnvironment, url, _browserManager.ProfileManager.CurrentProfile);

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

                _browserManager.DownloadManager.RegisterDownloadEvents(newWebView.CoreWebView2);
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
            await _webViewManager.InitializeWebViewAsync(tab.WebView, _activeEnvironment, url, _browserManager.ProfileManager.CurrentProfile);
            
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

            _browserManager.DownloadManager.RegisterDownloadEvents(tab.WebView.CoreWebView2);
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
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(10, 0, 8, 0),
                Height = 28,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105)),
                BorderThickness = new Thickness(1)
            };

            btnTab.Click += (s, e) => _browserManager.TabManager.SelectTab(tab);
            TabContainer.Children.Add(btnTab);
        }

        private UIElement CreateTabHeaderContent(BrowserTab tab)
        {
            var grid = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txtTitle = new TextBlock
            {
                Text = tab.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 12
            };

            var btnClose = new System.Windows.Controls.Button
            {
                Content = "✕",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                Width = 18,
                Height = 18,
                FontSize = 10,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnClose.Click += (s, e) =>
            {
                e.Handled = true;
                _browserManager.TabManager.CloseTab(tab);
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

        private void TabManager_TabSelected(object? sender, BrowserTab tab)
        {
            WebViewHostGrid.Children.Clear();
            WebViewHostGrid.Children.Add(tab.WebView);
            txtAddressBar.Text = tab.Url;

            foreach (UIElement elem in TabContainer.Children)
            {
                if (elem is System.Windows.Controls.Button btn)
                {
                    bool isActive = (btn.Tag == tab);
                    btn.Background = isActive ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
                    btn.Foreground = isActive ?
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)) :
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
                }
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
        private void TxtAddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var active = _browserManager.TabManager.ActiveTab;
                if (active != null)
                {
                    txtStatus.Text = "Đang chuyển trang...";
                    _webViewManager.Navigate(active.WebView, txtAddressBar.Text);
                }
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
            if (active != null && active.WebView.CanGoBack)
            {
                txtStatus.Text = "Đang quay lại trang trước...";
                active.WebView.GoBack();
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null && active.WebView.CanGoForward)
            {
                txtStatus.Text = "Đang tiến tới trang tiếp...";
                active.WebView.GoForward();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                txtStatus.Text = "Đang tải lại trang...";
                active.WebView.Reload();
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                txtStatus.Text = "Đang về Trang chủ Google...";
                _webViewManager.Navigate(active.WebView, "https://www.google.com");
            }
        }
        #endregion

        #region Bookmarks Manager Modal & Handlers
        private void ToggleBookmarksModal()
        {
            bool wasOpen = BookmarksModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : BookmarksModal);
            BookmarksModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (BookmarksModal.Visibility == Visibility.Visible)
            {
                RefreshBookmarksListUI();
            }
            UpdateModalVisibilities();
        }

        private void BtnBookmark_Click(object sender, RoutedEventArgs e)
        {
            ToggleBookmarksModal();
        }

        private void BtnCloseBookmarksModal_Click(object sender, RoutedEventArgs e)
        {
            ToggleBookmarksModal();
        }

        private void BtnAddCurrentBookmark_Click(object sender, RoutedEventArgs e)
        {
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null && !string.IsNullOrWhiteSpace(active.Url))
            {
                _browserManager.ProfileManager.AddBookmark(active.Url);
                RefreshBookmarksListUI();
                txtStatus.Text = $"Đã lưu Bookmark: {active.Url}";
            }
        }

        private void RefreshBookmarksListUI()
        {
            BookmarksListContainer.Children.Clear();
            var bookmarks = _browserManager.ProfileManager.CurrentProfile.Bookmarks;

            if (bookmarks == null || bookmarks.Count == 0)
            {
                BookmarksListContainer.Children.Add(new TextBlock
                {
                    Text = "Chưa có trang yêu thích nào được lưu. Bấm '+ Lưu Trang Hiện Tại' để thêm.",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    Margin = new Thickness(0, 10, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var url in bookmarks)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txtUrl = new TextBlock
                {
                    Text = url,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = url,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var btnUrl = new System.Windows.Controls.Button
                {
                    Content = txtUrl,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59)),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 6, 12, 6),
                    Height = 36,
                    FontSize = 12
                };
                string targetUrl = url;
                btnUrl.Click += (s, e) =>
                {
                    var active = _browserManager.TabManager.ActiveTab;
                    if (active != null)
                    {
                        _webViewManager.Navigate(active.WebView, targetUrl);
                    }
                    BookmarksModal.Visibility = Visibility.Collapsed;
                    UpdateModalVisibilities();
                };

                var btnDelete = new System.Windows.Controls.Button
                {
                    Content = "✕",
                    ToolTip = "Xóa trang khỏi danh sách yêu thích",
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42)),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(6, 0, 0, 0),
                    Padding = new Thickness(10, 0, 10, 0),
                    Height = 36,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                };
                btnDelete.Click += (s, e) =>
                {
                    _browserManager.ProfileManager.CurrentProfile.Bookmarks.Remove(targetUrl);
                    _browserManager.ProfileManager.SaveProfile(_browserManager.ProfileManager.CurrentProfile);
                    RefreshBookmarksListUI();
                };

                Grid.SetColumn(btnUrl, 0);
                Grid.SetColumn(btnDelete, 1);
                grid.Children.Add(btnUrl);
                grid.Children.Add(btnDelete);

                BookmarksListContainer.Children.Add(grid);
            }
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
                // 1. Disable capture protection so DWM does NOT draw a white box
                WindowProtection.DisableCaptureProtection(this);

                // 2. Hide native WebView2 HWND & minimize/hide main WPF window completely via Win32
                WebViewHostGrid.Visibility = Visibility.Hidden;
                this.WindowState = WindowState.Minimized;
                this.Hide();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_HIDE);

                // 3. Allow DWM compositor 400ms to completely unmap window surface from desktop composite
                await Task.Delay(400);

                // 4. Trigger regional crop capture overlay (Overlay is protected from screen recorders)
                _screenshotManager.TriggerRegionalCapture();
            }
            finally
            {
                // 5. Unhide main WPF window & restore window state & protection mode
                this.Show();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_RESTORE);
                this.WindowState = WindowState.Normal;
                this.Activate();
                UpdateModalVisibilities();
                WindowProtection.ApplyProtection(this, WindowProtection.CurrentMode);
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
                // 1. Disable capture protection so DWM does NOT draw a white box
                WindowProtection.DisableCaptureProtection(this);

                // 2. Hide native WebView2 HWND & minimize/hide main WPF window completely via Win32
                WebViewHostGrid.Visibility = Visibility.Hidden;
                this.WindowState = WindowState.Minimized;
                this.Hide();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_HIDE);

                // 3. Allow DWM compositor 400ms to completely unmap window surface from desktop composite
                await Task.Delay(400);

                // 4. Trigger full screen capture
                _screenshotManager.TriggerFullScreenCapture();
            }
            finally
            {
                // 5. Unhide main WPF window & restore window state & protection mode
                this.Show();
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_RESTORE);
                this.WindowState = WindowState.Normal;
                this.Activate();
                UpdateModalVisibilities();
                WindowProtection.ApplyProtection(this, WindowProtection.CurrentMode);
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

        #region In-App Stealth Notification Overlay System (Replaces Native OS MessageBox)
        private void ShowAppNotification(string message, string title = "Thông báo")
        {
            txtNotificationTitle.Text = title;
            txtNotificationMessage.Text = message;
            btnNotificationOk.Visibility = Visibility.Visible;
            NotificationConfirmButtons.Visibility = Visibility.Collapsed;
            CloseAllModalsExcept(NotificationModal);
            NotificationModal.Visibility = Visibility.Visible;
            UpdateModalVisibilities();
        }

        private Task<bool> ShowAppConfirmationAsync(string message, string title = "Xác nhận thao tác")
        {
            _notificationDialogTask = new TaskCompletionSource<bool>();
            txtNotificationTitle.Text = title;
            txtNotificationMessage.Text = message;
            btnNotificationOk.Visibility = Visibility.Collapsed;
            NotificationConfirmButtons.Visibility = Visibility.Visible;
            CloseAllModalsExcept(NotificationModal);
            NotificationModal.Visibility = Visibility.Visible;
            UpdateModalVisibilities();
            return _notificationDialogTask.Task;
        }

        private void BtnNotificationOk_Click(object sender, RoutedEventArgs e)
        {
            NotificationModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        private void BtnNotificationConfirm_Click(object sender, RoutedEventArgs e)
        {
            NotificationModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            _notificationDialogTask?.TrySetResult(true);
        }

        private void BtnNotificationCancel_Click(object sender, RoutedEventArgs e)
        {
            NotificationModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            _notificationDialogTask?.TrySetResult(false);
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

        private void BtnGoogleSignIn_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                txtStatus.Text = "Đang chuyển tới trang Đăng nhập Google...";
                _webViewManager.Navigate(active.WebView, "https://accounts.google.com/");
            }
        }

        private void BtnOpenChatGPT_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                txtStatus.Text = "Đang mở ChatGPT (https://chatgpt.com/)...";
                _webViewManager.Navigate(active.WebView, "https://chatgpt.com/");
            }
        }

        private void BtnOpenGemini_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            var active = _browserManager.TabManager.ActiveTab;
            if (active != null)
            {
                txtStatus.Text = "Đang mở Google Gemini (https://gemini.google.com/app)...";
                _webViewManager.Navigate(active.WebView, "https://gemini.google.com/app");
            }
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

        private void BtnClearGoogleCache_Click(object sender, RoutedEventArgs e)
        {
            GoogleAccountModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
            SecureClearManager.ClearSensitiveData();
            ShowAppNotification("Đã xóa sạch Cookie và Bộ nhớ đệm Google!", "Đã Dọn Dẹp");
        }
        #endregion






        #region Action Buttons & Settings
        private void BtnDownloads_Click(object sender, RoutedEventArgs e)
        {
            ShowAppNotification(
                $"Thư mục Tải xuống: {_browserManager.DownloadManager.DefaultDownloadPath}\nSố file đang tải: {_browserManager.DownloadManager.Downloads.Count}",
                "Trình Quản Lý Tải Xuống");
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            bool wasOpen = SettingsModal.Visibility == Visibility.Visible;
            CloseAllModalsExcept(wasOpen ? null : SettingsModal);
            SettingsModal.Visibility = wasOpen ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsModal.Visibility == Visibility.Visible)
            {
                txtCleanupStatus.Text = "";
                sldOpacity.Value = this.Opacity;
                txtOpacityPercent.Text = $"{(int)(this.Opacity * 100)}% ({((this.Opacity >= 0.98) ? "Không Trong Suốt" : "Glass Mode")})";
            }
            UpdateModalVisibilities();
        }

        private void BtnCloseSettingsModal_Click(object sender, RoutedEventArgs e)
        {
            SettingsModal.Visibility = Visibility.Collapsed;
            UpdateModalVisibilities();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x00000002;

        public void ApplyWindowOpacity(double opacity)
        {
            this.Opacity = opacity;
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    if (opacity >= 0.98)
                    {
                        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_LAYERED);
                    }
                    else
                    {
                        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED);
                        byte alpha = (byte)(Math.Clamp(opacity, 0.15, 1.0) * 255);
                        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyWindowOpacity error: {ex.Message}");
            }

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
                _appSettings.WindowOpacity = e.NewValue;
                _appSettings.SaveConfig();
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
            if (_isInitializingTheme) return;
            _isInitializingTheme = true;
            try
            {
                bool isLight = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase);
                bool isGlass = this.Opacity < 0.98;

                if (rbDarkTheme != null && rbLightTheme != null)
                {
                    rbDarkTheme.IsChecked = !isLight;
                    rbLightTheme.IsChecked = isLight;
                }

                byte mainAlpha = isGlass ? (byte)60 : (byte)255;
                byte panelAlpha = isGlass ? (byte)150 : (byte)255;
                byte barAlpha = isGlass ? (byte)90 : (byte)255;

                var bgMain = new System.Windows.Media.SolidColorBrush(
                    isLight ? System.Windows.Media.Color.FromArgb(mainAlpha, 241, 245, 249)
                            : System.Windows.Media.Color.FromArgb(mainAlpha, 11, 15, 25));

                var bgPanel = new System.Windows.Media.SolidColorBrush(
                    isLight ? System.Windows.Media.Color.FromArgb(panelAlpha, 226, 232, 240)
                            : System.Windows.Media.Color.FromArgb(panelAlpha, 21, 29, 42));

                var bgBar = new System.Windows.Media.SolidColorBrush(
                    isLight ? System.Windows.Media.Color.FromArgb(barAlpha, 226, 232, 240)
                            : System.Windows.Media.Color.FromArgb(barAlpha, 6, 10, 18));

                var bgToolbar = new System.Windows.Media.SolidColorBrush(
                    isLight ? System.Windows.Media.Color.FromArgb(barAlpha, 241, 245, 249)
                            : System.Windows.Media.Color.FromArgb(barAlpha, 21, 29, 42));

                var borderBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(203, 213, 225) : System.Windows.Media.Color.FromRgb(51, 65, 85));
                var textPrimary = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(15, 23, 42) : System.Windows.Media.Color.FromRgb(248, 250, 252));

                this.Foreground = textPrimary;
                this.Background = isGlass ? System.Windows.Media.Brushes.Transparent : bgMain;

                if (RootGrid != null)
                {
                    RootGrid.Background = bgMain;
                }

                if (TabBarHeader != null) TabBarHeader.Background = bgBar;
                if (AddressBarToolbar != null) AddressBarToolbar.Background = bgToolbar;
                if (StatusBarBorder != null) StatusBarBorder.Background = bgBar;

                Border[] modals = { SettingsModal, BookmarksModal, GoogleAccountModal, HelpModal, ScreenshotModal, NotificationModal };
                foreach (var modal in modals)
                {
                    if (modal != null)
                    {
                        modal.Background = bgPanel;
                        modal.BorderBrush = borderBrush;
                    }
                }

                if (rbDarkTheme != null) rbDarkTheme.Foreground = textPrimary;
                if (rbLightTheme != null) rbLightTheme.Foreground = textPrimary;

                if (_browserManager?.TabManager?.Tabs != null)
                {
                    try
                    {
                        var colorScheme = isLight ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
                        foreach (var tab in _browserManager.TabManager.Tabs)
                        {
                            if (tab.WebView?.CoreWebView2?.Profile != null)
                            {
                                tab.WebView.CoreWebView2.Profile.PreferredColorScheme = colorScheme;
                            }
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                _isInitializingTheme = false;
            }
        }

        private void BtnDoCleanup_Click(object sender, RoutedEventArgs e)
        {
            SecureClearManager.ClearSensitiveData();
            UpdateMemoryDisplay();
            txtCleanupStatus.Text = "✅ Đã giải phóng RAM & dọn dẹp cache bảo mật thành công!";
            ShowAppNotification("Đã xóa an toàn toàn bộ RAM Bitmaps, Khay nhớ đệm riêng tư và Cache nhạy cảm!", "Đã Dọn Dẹp Bảo Mật");
        }

        private void UpdateMemoryDisplay()
        {
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
    }
}

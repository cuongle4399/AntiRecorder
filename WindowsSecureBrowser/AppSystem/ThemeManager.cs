using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using WindowsSecureBrowser.Browser;
using WindowsSecureBrowser.AppSystem;

using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using CheckBox = System.Windows.Controls.CheckBox;

namespace WindowsSecureBrowser.UI
{
    /// <summary>
    /// Encapsulates Light Mode and Dark Mode WPF control styling, WebView color scheme, and dynamic transparency injection.
    /// </summary>
    public class ThemeManager
    {
        private readonly MainWindow _mainWindow;

        public ThemeManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public void ApplyTheme(string themeMode, double currentWindowOpacity, BrowserManager browserManager, AppSettingsManager appSettings, Border[] modals)
        {
            bool isLight = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase);
            bool isGlass = currentWindowOpacity < 0.98;

            byte mainAlpha = isGlass ? (byte)(currentWindowOpacity * 255) : (byte)255;
            byte barAlpha = isGlass ? (byte)(Math.Max(currentWindowOpacity, 0.7) * 255) : (byte)255;
            byte addrAlpha = isGlass ? (byte)(Math.Max(currentWindowOpacity, 0.75) * 255) : (byte)255;

            var bgMain = new SolidColorBrush(
                isLight ? Color.FromArgb(mainAlpha, 241, 245, 249)
                        : Color.FromArgb(mainAlpha, 11, 15, 25));

            var bgPanel = new SolidColorBrush(
                isLight ? Color.FromRgb(255, 255, 255)
                        : Color.FromRgb(21, 29, 42));

            var bgCard = new SolidColorBrush(
                isLight ? Color.FromRgb(241, 245, 249)
                        : Color.FromRgb(30, 41, 59));

            var bgBar = new SolidColorBrush(
                isLight ? Color.FromArgb(barAlpha, 226, 232, 240)
                        : Color.FromArgb(barAlpha, 6, 10, 18));

            var bgToolbar = new SolidColorBrush(
                isLight ? Color.FromArgb(barAlpha, 241, 245, 249)
                        : Color.FromArgb(barAlpha, 21, 29, 42));

            var btnBg = new SolidColorBrush(
                isLight ? Color.FromRgb(255, 255, 255)
                        : Color.FromRgb(30, 41, 59));

            var btnFg = new SolidColorBrush(
                isLight ? Color.FromRgb(15, 23, 42)
                        : Color.FromRgb(255, 255, 255));

            var borderBrush = new SolidColorBrush(
                isLight ? Color.FromRgb(203, 213, 225)
                        : Color.FromRgb(51, 65, 85));

            var textPrimary = new SolidColorBrush(
                isLight ? Color.FromRgb(15, 23, 42)
                        : Color.FromRgb(248, 250, 252));

            var textSecondary = new SolidColorBrush(
                isLight ? Color.FromRgb(51, 65, 85)
                        : Color.FromRgb(203, 213, 225));

            _mainWindow.Foreground = textPrimary;
            _mainWindow.Background = isGlass ? Brushes.Transparent : bgMain;

            if (_mainWindow.OuterWindowBorder != null)
            {
                _mainWindow.OuterWindowBorder.BorderBrush = new SolidColorBrush(
                    isLight ? Color.FromRgb(14, 165, 233)
                            : Color.FromRgb(56, 189, 248));
            }

            if (_mainWindow.RootGrid != null)
            {
                _mainWindow.RootGrid.Background = bgMain;
                UpdateControlThemeRecursive(_mainWindow.RootGrid, textPrimary, textSecondary, bgCard, btnBg, btnFg, borderBrush);
            }

            if (_mainWindow.TabBarHeader != null) _mainWindow.TabBarHeader.Background = bgBar;
            if (_mainWindow.AddressBarToolbar != null) _mainWindow.AddressBarToolbar.Background = bgToolbar;
            if (_mainWindow.StatusBarBorder != null) _mainWindow.StatusBarBorder.Background = bgBar;



            if (_mainWindow.txtStatus != null) _mainWindow.txtStatus.Foreground = textSecondary;
            if (_mainWindow.txtMemoryUsage != null) _mainWindow.txtMemoryUsage.Foreground = textSecondary;

            if (_mainWindow.txtAddressBar != null)
            {
                _mainWindow.txtAddressBar.Background = new SolidColorBrush(
                    isLight ? Color.FromArgb(addrAlpha, 255, 255, 255)
                            : Color.FromArgb(addrAlpha, 11, 15, 25));
                _mainWindow.txtAddressBar.Foreground = textPrimary;
                _mainWindow.txtAddressBar.BorderBrush = borderBrush;
            }

            foreach (var modal in modals)
            {
                if (modal != null)
                {
                    modal.Background = bgPanel;
                    modal.BorderBrush = borderBrush;
                    UpdateControlThemeRecursive(modal, textPrimary, textSecondary, bgCard, btnBg, btnFg, borderBrush);
                }
            }

            if (_mainWindow.rbDarkTheme != null) _mainWindow.rbDarkTheme.Foreground = textPrimary;
            if (_mainWindow.rbLightTheme != null) _mainWindow.rbLightTheme.Foreground = textPrimary;
            if (_mainWindow.chkMuteAudio != null) _mainWindow.chkMuteAudio.Foreground = textPrimary;

            UpdateAllTabStyles(_mainWindow.TabContainer, browserManager?.TabManager?.ActiveTab, themeMode, currentWindowOpacity);

            if (browserManager?.TabManager?.Tabs != null)
            {
                foreach (var tab in browserManager.TabManager.Tabs)
                {
                    ApplyThemeToTab(tab, themeMode, currentWindowOpacity);
                }
            }
        }

        public void ApplyThemeToTab(BrowserTab tab, string themeMode, double currentWindowOpacity)
        {
            if (tab?.WebView?.CoreWebView2 == null) return;

            bool isLight = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase);
            bool isGlass = currentWindowOpacity < 0.98;

            var colorScheme = isLight ? CoreWebView2PreferredColorScheme.Light : CoreWebView2PreferredColorScheme.Dark;
            byte webAlpha = isGlass ? (byte)(currentWindowOpacity * 255) : (byte)255;
            System.Drawing.Color webBg = isLight ?
                System.Drawing.Color.FromArgb(webAlpha, 255, 255, 255) :
                System.Drawing.Color.FromArgb(webAlpha, 15, 23, 42);

            try
            {
                tab.WebView.DefaultBackgroundColor = webBg;
                if (tab.WebView.CoreWebView2.Profile != null)
                {
                    tab.WebView.CoreWebView2.Profile.PreferredColorScheme = colorScheme;
                }

                string opacityVal = currentWindowOpacity.ToString(CultureInfo.InvariantCulture);
                string bgRgb = isLight ? "255, 255, 255" : "15, 23, 42";
                string hexBg = isLight ? "#ffffff" : "#0f172a";
                string schemeVal = isLight ? "light" : "dark";

                string styleCss = isGlass ?
                    $"html, body {{ background-color: rgba({bgRgb}, {opacityVal}) !important; }} img, svg, canvas, video {{ opacity: {opacityVal} !important; transition: opacity 0.2s ease; }}" :
                    $"html, body {{ background-color: {hexBg} !important; }} img, svg, canvas, video {{ opacity: 1 !important; }}";

                string script = $@"
                    (function() {{
                        try {{
                            var style = document.getElementById('antirecorder-opacity-style');
                            if (!style) {{
                                style = document.createElement('style');
                                style.id = 'antirecorder-opacity-style';
                                (document.head || document.documentElement).appendChild(style);
                            }}
                            style.innerHTML = '{styleCss}';

                            var meta = document.querySelector('meta[name=""color-scheme""]');
                            if (!meta) {{
                                meta = document.createElement('meta');
                                meta.name = 'color-scheme';
                                (document.head || document.documentElement).appendChild(meta);
                            }}
                            meta.content = '{schemeVal}';
                        }} catch (e) {{}}
                    }})();
                ";

                tab.WebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        }

        public void UpdateAllTabStyles(StackPanel tabContainer, BrowserTab? activeTab, string themeMode, double currentWindowOpacity = 1.0)
        {
            if (tabContainer == null) return;
            bool isLight = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase);
            bool isGlass = currentWindowOpacity < 0.98;

            byte activeAlpha = isGlass ? (byte)(Math.Max(currentWindowOpacity, 0.8) * 255) : (byte)255;
            byte inactiveAlpha = isGlass ? (byte)(Math.Max(currentWindowOpacity, 0.55) * 255) : (byte)255;

            foreach (UIElement elem in tabContainer.Children)
            {
                if (elem is Button btn)
                {
                    bool isActive = (btn.Tag == activeTab);

                    if (isLight)
                    {
                        btn.Background = isActive ?
                            new SolidColorBrush(Color.FromArgb(activeAlpha, 255, 255, 255)) :
                            new SolidColorBrush(Color.FromArgb(inactiveAlpha, 218, 226, 236));

                        btn.Foreground = isActive ?
                            new SolidColorBrush(Color.FromRgb(15, 23, 42)) :
                            new SolidColorBrush(Color.FromRgb(71, 85, 105));

                        btn.BorderBrush = isActive ?
                            new SolidColorBrush(Color.FromRgb(14, 165, 233)) :
                            new SolidColorBrush(Color.FromRgb(203, 213, 225));
                    }
                    else
                    {
                        btn.Background = isActive ?
                            new SolidColorBrush(Color.FromArgb(activeAlpha, 51, 65, 85)) :
                            new SolidColorBrush(Color.FromArgb(inactiveAlpha, 15, 23, 42));

                        btn.Foreground = isActive ?
                            new SolidColorBrush(Color.FromRgb(248, 250, 252)) :
                            new SolidColorBrush(Color.FromRgb(148, 163, 184));

                        btn.BorderBrush = isActive ?
                            new SolidColorBrush(Color.FromRgb(56, 189, 248)) :
                            new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    }

                    if (btn.Content is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is TextBlock txtTitle)
                            {
                                txtTitle.Foreground = btn.Foreground;
                            }
                            else if (child is Button btnClose)
                            {
                                btnClose.Foreground = isLight ?
                                    new SolidColorBrush(Color.FromRgb(100, 116, 139)) :
                                    new SolidColorBrush(Color.FromRgb(148, 163, 184));
                            }
                        }
                    }
                }
            }
        }

        public void UpdateControlThemeRecursive(DependencyObject parent, Brush fg, Brush secondaryFg, Brush cardBg, Brush btnBg, Brush btnFg, Brush borderBrush)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBlock tb)
                {
                    DependencyObject p = VisualTreeHelper.GetParent(tb);
                    bool isInsideButton = false;
                    while (p != null && p != parent)
                    {
                        if (p is Button)
                        {
                            isInsideButton = true;
                            break;
                        }
                        p = VisualTreeHelper.GetParent(p);
                    }

                    tb.Foreground = isInsideButton ? btnFg : fg;
                }
                else if (child is RadioButton rb)
                {
                    rb.Foreground = fg;
                }
                else if (child is CheckBox cb)
                {
                    cb.Foreground = fg;
                    UpdateControlThemeRecursive(child, fg, secondaryFg, cardBg, btnBg, btnFg, borderBrush);
                }
                else if (child is Button btn)
                {
                    btn.Background = btnBg;
                    btn.Foreground = btnFg;
                    btn.BorderBrush = borderBrush;
                    UpdateControlThemeRecursive(child, fg, secondaryFg, cardBg, btnBg, btnFg, borderBrush);
                }
                else if (child is Border b && b.Name != "OuterWindowBorder")
                {
                    b.Background = cardBg;
                    b.BorderBrush = borderBrush;
                    UpdateControlThemeRecursive(child, fg, secondaryFg, cardBg, btnBg, btnFg, borderBrush);
                }
                else
                {
                    UpdateControlThemeRecursive(child, fg, secondaryFg, cardBg, btnBg, btnFg, borderBrush);
                }
            }
        }
    }
}

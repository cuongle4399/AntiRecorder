using System;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsSecureBrowser
{
    public partial class MainWindow
    {
        private TaskCompletionSource<bool>? _notificationDialogTask;

        private void CloseAllModalsExcept(UIElement? activeModal = null)
        {
            if (GoogleAccountModal != null && GoogleAccountModal != activeModal) GoogleAccountModal.Visibility = Visibility.Collapsed;
            if (BookmarksModal != null && BookmarksModal != activeModal) BookmarksModal.Visibility = Visibility.Collapsed;
            if (DownloadsModal != null && DownloadsModal != activeModal) DownloadsModal.Visibility = Visibility.Collapsed;
            if (ProxyModal != null && ProxyModal != activeModal) ProxyModal.Visibility = Visibility.Collapsed;
            if (ExtensionsModal != null && ExtensionsModal != activeModal) ExtensionsModal.Visibility = Visibility.Collapsed;
            if (ScreenshotModal != null && ScreenshotModal != activeModal) ScreenshotModal.Visibility = Visibility.Collapsed;
            if (SettingsModal != null && SettingsModal != activeModal) SettingsModal.Visibility = Visibility.Collapsed;
            if (HelpModal != null && HelpModal != activeModal) HelpModal.Visibility = Visibility.Collapsed;
            if (NotificationModal != null && NotificationModal != activeModal) NotificationModal.Visibility = Visibility.Collapsed;
            if (OverflowMenuModal != null && OverflowMenuModal != activeModal) OverflowMenuModal.Visibility = Visibility.Collapsed;
        }

        private void UpdateModalVisibilities()
        {
            // WPF Airspace fix: Hide native WebView2 HWND when WPF overlay modals are active
            bool isAnyModalOpen = (GoogleAccountModal != null && GoogleAccountModal.Visibility == Visibility.Visible) ||
                                  (BookmarksModal != null && BookmarksModal.Visibility == Visibility.Visible) ||
                                  (DownloadsModal != null && DownloadsModal.Visibility == Visibility.Visible) ||
                                  (ProxyModal != null && ProxyModal.Visibility == Visibility.Visible) ||
                                  (ExtensionsModal != null && ExtensionsModal.Visibility == Visibility.Visible) ||
                                  (ScreenshotModal != null && ScreenshotModal.Visibility == Visibility.Visible) ||
                                  (SettingsModal != null && SettingsModal.Visibility == Visibility.Visible) ||
                                  (HelpModal != null && HelpModal.Visibility == Visibility.Visible) ||
                                  (NotificationModal != null && NotificationModal.Visibility == Visibility.Visible) ||
                                  (OverflowMenuModal != null && OverflowMenuModal.Visibility == Visibility.Visible);

            WebViewHostGrid.Visibility = isAnyModalOpen ? Visibility.Hidden : Visibility.Visible;
        }

        #region Notification Modal System
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
    }
}

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsSecureBrowser.Privacy
{
    public class PrivateClipboardManager
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static Bitmap? _ramPrivateBitmap;

        public static void SetPrivateBitmap(Bitmap bitmap)
        {
            _ramPrivateBitmap?.Dispose();
            _ramPrivateBitmap = new Bitmap(bitmap); // Retain in internal RAM

            // Synchronize to System Clipboard so Ctrl + V works instantly!
            try
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                    System.Windows.Clipboard.SetImage(bitmapSource);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard sync error: {ex.Message}");
            }
        }

        public static Bitmap? GetPrivateBitmap()
        {
            return _ramPrivateBitmap;
        }

        public static async Task CopyToSystemClipboardWithAutoClearAsync(Bitmap bitmap, int timeoutSeconds = 10)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                System.Windows.Clipboard.SetImage(bitmapSource);
            }
            finally
            {
                // CRITICAL MEMORY FIX: Delete unmanaged GDI object handle to prevent memory leaks
                DeleteObject(hBitmap);
            }

            // Schedule automatic clipboard wipe after timeout
            await Task.Delay(timeoutSeconds * 1000);
            ClearSystemClipboardIfMatching();
        }

        public static void ClearSystemClipboardIfMatching()
        {
            try
            {
                System.Windows.Clipboard.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear system clipboard: {ex.Message}");
            }
        }

        public static void ClearPrivateRam()
        {
            _ramPrivateBitmap?.Dispose();
            _ramPrivateBitmap = null;
        }
    }
}

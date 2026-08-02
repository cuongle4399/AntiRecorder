using System;

namespace WindowsSecureBrowser.Security
{
    public class ClipboardSecurity
    {
        public static void ClearClipboard()
        {
            try
            {
                System.Windows.Clipboard.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardSecurity Clear Error] {ex.Message}");
            }
        }
    }
}

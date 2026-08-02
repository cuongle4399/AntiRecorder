using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using WindowsSecureBrowser.Security;

namespace WindowsSecureBrowser.Privacy
{
    public class ScreenshotManager
    {
        private static bool s_isCapturing = false;
        public event EventHandler<Bitmap>? ScreenshotCaptured;

        public void TriggerFullScreenCapture()
        {
            if (s_isCapturing) return;
            s_isCapturing = true;

            try
            {
                Rectangle bounds = SystemInformation.VirtualScreen;
                Bitmap ramBitmap = CaptureScreenRegion(bounds);
                ScreenshotCaptured?.Invoke(this, ramBitmap);
            }
            finally
            {
                s_isCapturing = false;
            }
        }

        public void TriggerRegionalCapture()
        {
            if (s_isCapturing) return; // Prevent multiple overlay instances
            s_isCapturing = true;

            Bitmap? desktopSnapshot = null;

            try
            {
                Rectangle virtualScreen = SystemInformation.VirtualScreen;

                // 1. Capture screen snapshot behind overlay
                desktopSnapshot = CaptureScreenRegion(virtualScreen);

                // 2. Create full screen selection overlay form (Windows Snipping Tool Style)
                using var overlay = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = virtualScreen,
                    TopMost = true,
                    Cursor = Cursors.Cross,
                    BackgroundImage = desktopSnapshot,
                    BackgroundImageLayout = ImageLayout.None,
                    ShowInTaskbar = false
                };

                // Enable double buffering via reflection
                typeof(Form).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(overlay, true);

                // Enable 100% Stealth Protection on Overlay Window (Hidden from screen recorders)
                overlay.HandleCreated += (s, e) =>
                {
                    SecurityCoreWrapper.SetWindowProtection(overlay.Handle, true);
                };

                Point startPoint = Point.Empty;
                Rectangle selectionRect = Rectangle.Empty;
                bool isSelecting = false;

                overlay.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        isSelecting = true;
                        startPoint = e.Location;
                        selectionRect = new Rectangle(e.X, e.Y, 0, 0);
                        overlay.Invalidate();
                    }
                };

                overlay.MouseMove += (s, e) =>
                {
                    if (isSelecting)
                    {
                        int x = Math.Min(startPoint.X, e.X);
                        int y = Math.Min(startPoint.Y, e.Y);
                        int w = Math.Abs(startPoint.X - e.X);
                        int h = Math.Abs(startPoint.Y - e.Y);
                        selectionRect = new Rectangle(x, y, w, h);
                        overlay.Invalidate();
                    }
                };

                overlay.MouseUp += (s, e) =>
                {
                    if (isSelecting)
                    {
                        isSelecting = false;
                        overlay.Close();

                        if (selectionRect.Width > 5 && selectionRect.Height > 5 && desktopSnapshot != null)
                        {
                            // Crop from desktopSnapshot directly for maximum speed and accuracy
                            Rectangle cropArea = new Rectangle(
                                Math.Max(0, selectionRect.X),
                                Math.Max(0, selectionRect.Y),
                                Math.Min(selectionRect.Width, desktopSnapshot.Width - selectionRect.X),
                                Math.Min(selectionRect.Height, desktopSnapshot.Height - selectionRect.Y)
                            );

                            if (cropArea.Width > 0 && cropArea.Height > 0)
                            {
                                Bitmap ramBitmap = desktopSnapshot.Clone(cropArea, PixelFormat.Format32bppArgb);
                                ScreenshotCaptured?.Invoke(this, ramBitmap);
                            }
                        }
                    }
                };

                overlay.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape)
                    {
                        isSelecting = false;
                        overlay.Close();
                    }
                };

                overlay.Paint += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    if (selectionRect.Width > 0 && selectionRect.Height > 0)
                    {
                        // 1. Darken area OUTSIDE the selected rectangle
                        using (Region maskRegion = new Region(overlay.ClientRectangle))
                        {
                            maskRegion.Exclude(selectionRect);
                            using (Brush dimBrush = new SolidBrush(Color.FromArgb(120, 10, 15, 25)))
                            {
                                g.FillRegion(dimBrush, maskRegion);
                            }
                        }

                        // 2. Draw Cyan Dashed Rectangle Border (Windows Snipping Tool Style)
                        using (Pen cyanPen = new Pen(Color.FromArgb(255, 56, 189, 248), 2.0f))
                        {
                            cyanPen.DashStyle = DashStyle.Dash;
                            g.DrawRectangle(cyanPen, selectionRect);
                        }

                        // Inner white dotted line for high visibility on any background
                        using (Pen whitePen = new Pen(Color.White, 1.0f))
                        {
                            whitePen.DashStyle = DashStyle.Dot;
                            g.DrawRectangle(whitePen, selectionRect.X + 1, selectionRect.Y + 1, Math.Max(1, selectionRect.Width - 2), Math.Max(1, selectionRect.Height - 2));
                        }

                        // 3. Draw Dimension Badge (e.g. 1280 x 720 px)
                        string sizeText = $"{selectionRect.Width} × {selectionRect.Height} px";
                        using Font badgeFont = new Font("Segoe UI", 9f, FontStyle.Bold);
                        SizeF textSize = g.MeasureString(sizeText, badgeFont);
                        int badgeX = selectionRect.X;
                        int badgeY = selectionRect.Y - (int)textSize.Height - 8;
                        if (badgeY < 5) badgeY = selectionRect.Y + 8;

                        RectangleF badgeRect = new RectangleF(badgeX, badgeY, textSize.Width + 12, textSize.Height + 6);
                        using (Brush badgeBg = new SolidBrush(Color.FromArgb(220, 15, 23, 42)))
                        using (Pen badgeBorder = new Pen(Color.FromArgb(255, 56, 189, 248), 1.0f))
                        using (Brush textBrush = new SolidBrush(Color.FromArgb(255, 248, 250, 252)))
                        {
                            g.FillRectangle(badgeBg, badgeRect);
                            g.DrawRectangle(badgeBorder, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
                            g.DrawString(sizeText, badgeFont, textBrush, badgeX + 6, badgeY + 3);
                        }
                    }
                    else
                    {
                        // Full dim backdrop before selection starts
                        using (Brush dimBrush = new SolidBrush(Color.FromArgb(100, 10, 15, 25)))
                        {
                            g.FillRectangle(dimBrush, overlay.ClientRectangle);
                        }

                        // Helper Instruction Text at Top Center
                        string guideText = "✂ Kéo thả chuột để khoanh vùng cần chụp • Nhấn ESC để hủy";
                        using Font guideFont = new Font("Segoe UI", 12f, FontStyle.Bold);
                        SizeF guideSize = g.MeasureString(guideText, guideFont);
                        float guideX = (overlay.ClientRectangle.Width - guideSize.Width) / 2f;
                        float guideY = 24f;

                        RectangleF guideRect = new RectangleF(guideX - 16, guideY - 6, guideSize.Width + 32, guideSize.Height + 12);
                        using (Brush guideBg = new SolidBrush(Color.FromArgb(230, 15, 23, 42)))
                        using (Pen guideBorder = new Pen(Color.FromArgb(255, 56, 189, 248), 1.5f))
                        using (Brush guideTextBrush = new SolidBrush(Color.FromArgb(255, 248, 250, 252)))
                        {
                            g.FillRectangle(guideBg, guideRect);
                            g.DrawRectangle(guideBorder, guideRect.X, guideRect.Y, guideRect.Width, guideRect.Height);
                            g.DrawString(guideText, guideFont, guideTextBrush, guideX, guideY);
                        }
                    }
                };

                // Show overlay stealth modal
                overlay.ShowDialog();
            }
            finally
            {
                desktopSnapshot?.Dispose();
                s_isCapturing = false;
            }
        }


        private Bitmap CaptureScreenRegion(Rectangle area)
        {
            // STRICT RAM-ONLY BITMAP CAPTURE: Zero disk IO, no temp files!
            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            }
            return bmp;
        }

        public static string ConvertBitmapToBase64DataUrl(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                byte[] bytes = ms.ToArray();
                return "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
        }
    }
}

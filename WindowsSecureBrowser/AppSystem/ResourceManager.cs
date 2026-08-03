using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WindowsSecureBrowser.AppSystem
{
    public class ResourceManager
    {
        // Trim Working Set về mức tối thiểu — mạnh hơn SetProcessWorkingSetSize(-1,-1)
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        public static long GetWorkingSetMemoryMB()
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64 / (1024 * 1024);
        }

        /// <summary>
        /// Giải phóng RAM đồng bộ (dùng cho on-demand, ví dụ nút cleanup).
        /// GC chạy trên UI thread — chỉ dùng khi không có lựa chọn khác.
        /// </summary>
        public static void OptimizeMemory()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect();

                using var proc = Process.GetCurrentProcess();
                EmptyWorkingSet(proc.Handle);
            }
            catch { }
        }

        /// <summary>
        /// Giải phóng RAM bất đồng bộ trên background thread — KHÔNG block UI.
        /// aggressive=true  → GC Aggressive + heap compacting (dùng khi ẩn app xuống tray, tối đa hóa giải phóng).
        /// aggressive=false → GC Forced không blocking (dùng cho periodic timer, nhẹ hơn, không gây pause).
        /// </summary>
        public static async Task OptimizeMemoryAsync(bool aggressive = false)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (aggressive)
                    {
                        // Chế độ mạnh: heap compacting, giải phóng tối đa
                        // Dùng khi app ẩn xuống tray — user không tương tác, không ảnh hưởng UX
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    else
                    {
                        // Chế độ nhẹ: không compacting, non-blocking — dùng cho auto-trim định kỳ
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(0, GCCollectionMode.Forced); // Gen0 cleanup
                    }
                });

                // EmptyWorkingSet: trim Working Set sau GC — quick Win32 call
                using var proc = Process.GetCurrentProcess();
                EmptyWorkingSet(proc.Handle);
            }
            catch { }
        }
    }
}

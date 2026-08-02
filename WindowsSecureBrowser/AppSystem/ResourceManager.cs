using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsSecureBrowser.AppSystem
{
    public class ResourceManager
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        public static long GetWorkingSetMemoryMB()
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64 / (1024 * 1024);
        }

        public static void OptimizeMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                using var proc = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }
    }
}

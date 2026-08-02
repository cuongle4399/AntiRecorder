using System;
using System.Diagnostics;

namespace WindowsSecureBrowser.AppSystem
{
    public class ResourceManager
    {
        public static long GetWorkingSetMemoryMB()
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64 / (1024 * 1024);
        }

        public static void OptimizeMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}

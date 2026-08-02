using System;

namespace WindowsSecureBrowser.Security
{
    public class MemoryProtection
    {
        public static void TrimProcessMemory()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                currentProcess.MinWorkingSet = currentProcess.MinWorkingSet;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MemoryProtection Trim Error] {ex.Message}");
            }
        }
    }
}

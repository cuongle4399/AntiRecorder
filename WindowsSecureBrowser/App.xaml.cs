using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace WindowsSecureBrowser
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Kill any old stale background processes so new build/launch never encounters file or port locks
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var processes = System.Diagnostics.Process.GetProcessesByName("AntiRecorder");
                foreach (var p in processes)
                {
                    if (p.Id != currentProcess.Id)
                    {
                        p.Kill();
                    }
                }
            }
            catch { }

            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                string errLog = $"[WPF Error {DateTime.Now}] {args.Exception.Message}\n{args.Exception.StackTrace}\n\n";
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_crash.log"), errLog);
                }
                catch { }

                System.Windows.MessageBox.Show($"Lỗi ứng dụng: {args.Exception.Message}", "AntiRecorder Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                string errLog = $"[AppDomain Error {DateTime.Now}] {ex?.Message}\n{ex?.StackTrace}\n\n";
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_crash.log"), errLog);
                }
                catch { }
            };
        }
    }
}

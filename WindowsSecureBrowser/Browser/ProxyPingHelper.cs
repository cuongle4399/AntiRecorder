using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace WindowsSecureBrowser.Browser
{
    public class ProxyPingHelper
    {
        public static async Task<long> PingProxyAsync(string host, int port, int timeoutMs = 2500)
        {
            if (string.IsNullOrWhiteSpace(host)) return -1;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var delayTask = Task.Delay(timeoutMs);

                var completedTask = await Task.WhenAny(connectTask, delayTask);
                stopwatch.Stop();

                if (completedTask == connectTask && client.Connected)
                {
                    return stopwatch.ElapsedMilliseconds;
                }
                try { client.Close(); } catch { }
                return -1; // Timeout or failed to connect
            }
            catch
            {
                return -1;
            }
        }
    }
}

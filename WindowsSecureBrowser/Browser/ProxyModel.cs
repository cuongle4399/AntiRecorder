using System;

namespace WindowsSecureBrowser.Browser
{
    public class ProxyModel
    {
        public string Server { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string RawInput { get; set; } = "";
        public bool IsEnabled => !string.IsNullOrWhiteSpace(Server);

        public (string host, int port) GetHostAndPort()
        {
            if (!IsEnabled) return ("", 80);
            try
            {
                string s = Server;
                if (s.Contains("://"))
                {
                    var uri = new Uri(s);
                    return (uri.Host, uri.Port);
                }
                string[] parts = s.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int p))
                {
                    return (parts[0].Trim(), p);
                }
                return (parts[0].Trim(), 80);
            }
            catch
            {
                return (Server, 80);
            }
        }

        public static ProxyModel Parse(string input)
        {
            var model = new ProxyModel();
            if (string.IsNullOrWhiteSpace(input)) return model;

            model.RawInput = input.Trim();
            string trimmed = model.RawInput;

            // Handle http://user:pass@ip:port or http://ip:port
            if (trimmed.StartsWith("http://") || trimmed.StartsWith("https://") || trimmed.StartsWith("socks5://"))
            {
                try
                {
                    var uri = new Uri(trimmed);
                    model.Server = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var userParts = uri.UserInfo.Split(':');
                        model.Username = userParts[0];
                        if (userParts.Length > 1) model.Password = userParts[1];
                    }
                    return model;
                }
                catch { }
            }

            // Handle ip:port:user:pass or ip:port
            string[] parts = trimmed.Split(':');
            if (parts.Length == 4)
            {
                model.Server = $"{parts[0].Trim()}:{parts[1].Trim()}";
                model.Username = parts[2].Trim();
                model.Password = parts[3].Trim();
            }
            else if (parts.Length == 2)
            {
                model.Server = $"{parts[0].Trim()}:{parts[1].Trim()}";
            }
            else
            {
                model.Server = trimmed;
            }

            return model;
        }

        public override string ToString()
        {
            if (!IsEnabled) return "Chưa đặt Proxy";
            if (!string.IsNullOrEmpty(Username))
            {
                return $"{Server} ({Username}:***)";
            }
            return Server;
        }
    }
}

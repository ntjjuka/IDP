using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IDPSsoAgentV2
{
    public class HttpServer : IDisposable
    {
        private readonly int _port;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        public bool IsHttps { get; private set; }

        public HttpServer(int port) { _port = port; }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();

            _listener = new HttpListener();
            // IP 프리픽스만 등록(충돌 방지)
            _listener.Prefixes.Add($@"https://127.0.0.1:{_port}/");
            _listener.Prefixes.Add($@"https://[::1]:{_port}/");

            _listener.Start();
            IsHttps = true;

            Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Restart() => Start();

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx = null;
                try { ctx = await _listener.GetContextAsync(); }
                catch { if (ct.IsCancellationRequested) break; continue; }
                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            try
            {
                if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/status")
                {
                    var json = GetStatusJson();
                    var bytes = Encoding.UTF8.GetBytes(json);
                    res.ContentType = "application/json; charset=utf-8";
                    res.StatusCode = 200;
                    await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    res.Close();
                    return;
                }

                res.StatusCode = 404; res.Close();
            }
            catch { try { res.StatusCode = 500; res.Close(); } catch { } }
        }

        private string GetStatusJson()
        {
            var host = Dns.GetHostName();
            var json = $"{{\"ok\":true,\"https\":{IsHttps.ToString().ToLower()},\"port\":{_port},\"host\":\"{host}\",\"now\":\"{DateTimeOffset.Now:O}\"}}";
            return json;
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
        }

        public void Dispose() => Stop();
    }
}

using IDPSsoAgent.Config;
using IDPSsoAgent.Core;
using IDPSsoAgent.Server.Sse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IDPSsoAgent.Server.Http
{
    public class HttpServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly Router _router;
        private CancellationTokenSource _cts;

        public HttpServer(int port)
        {
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _router = new Router();
        }

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            try
            {
                _listener.Start();
                while (!_cts.IsCancellationRequested)
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => Handle(ctx));
                }
            }
            catch (ObjectDisposedException) { }
            catch (HttpListenerException) { }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener.Close();
            }
            catch { }
            finally
            {
                EventBus.Stop();
            }
        }

        private async Task Handle(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.ContentEncoding = Encoding.UTF8;
                ctx.Response.Headers["Cache-Control"] = "no-store";

                // 보안 헤더 체크
                if (!IsAuthorized(ctx))
                {
                    ctx.Response.StatusCode = 401;
                    await WriteJson(ctx, ApiResult<object>.Fail("unauthorized"));
                    return;
                }

                await _router.RouteAsync(ctx, WriteJson);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await WriteJson(ctx, ApiResult<object>.Fail(ex.Message));
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        private bool IsAuthorized(HttpListenerContext ctx)
        {
            var v = ctx.Request.Headers[AppConfig.AgentHeaderName];
            return !string.IsNullOrEmpty(v) && v == AppConfig.AgentHeaderValue;
        }

        public StatusView GetStatus()
        {
            return new StatusView
            {
                listening = _listener.IsListening,
                logged_in = TokenMemoryStore.IsLoggedIn,
                port = AppConfig.ListenPort
            };
        }

        private async Task WriteJson<T>(HttpListenerContext ctx, ApiResult<T> res)
        {
            var json = _json.Serialize(res);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
    }
}

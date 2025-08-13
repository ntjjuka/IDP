using IDPSsoAgent.Config;
using IDPSsoAgent.Core;
using IDPSsoAgent.Server.Sse;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IDPSsoAgent.Server.Http
{
    public class Router
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly ApiHandlers _api = new ApiHandlers();

        public async Task RouteAsync(HttpListenerContext ctx, Func<HttpListenerContext, ApiResult<object>, Task> json)
        {
            var path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var method = ctx.Request.HttpMethod.ToUpperInvariant();

            if (method == "GET" && (path == "" || path == "/" || path == "/status"))
            {
                await json(ctx, ApiResult<object>.Ok(new StatusView
                {
                    listening = true,
                    logged_in = TokenMemoryStore.IsLoggedIn,
                    port = AppConfig.ListenPort
                }));
                return;
            }
            else if (method == "POST" && path == "/login")
            {
                var dto = await Read<LoginRequest>(ctx);
                var res = await _api.LoginAsync(dto);
                await json(ctx, res);
                return;
            }
            else if (method == "POST" && path == "/logout")
            {
                var res = await _api.LogoutAsync();
                await json(ctx, res);
                return;
            }
            else if (method == "GET" && path == "/token")
            {
                var res = await _api.GetTokenAsync();
                await json(ctx, res);
                return;
            }
            else if (method == "GET" && path == "/userinfo")
            {
                var res = await _api.UserInfoAsync();
                await json(ctx, res);
                return;
            }
            else if (method == "POST" && path == "/register")
            {
                var dto = await Read<RegisterRequest>(ctx);
                var res = await _api.RegisterAsync(dto);
                await json(ctx, res);
                return;
            }
            else if (method == "POST" && path == "/password/change")
            {
                var dto = await Read<PasswordChangeRequest>(ctx);
                var res = await _api.ChangePasswordAsync(dto);
                await json(ctx, res);
                return;
            }
            else if (method == "POST" && path == "/register/check-username")
            {
                var dto = await Read<UsernameCheckRequest>(ctx);
                var res = await _api.CheckUsernameAsync(dto);
                await json(ctx, res);
                return;
            }
            else if (method == "GET" && path == "/introspect")
            {
                // ?token=... (옵션) 없으면 현재 세션 토큰 검사
                var token = ctx.Request.QueryString["token"];
                var res = await _api.IntrospectAsync(token);
                await json(ctx, res);
                return;
            }
            else if (method == "GET" && path == "/events")
            {
                // SSE 구독 등록
                var client = new SseClient(ctx);
                var id = EventBus.Add(client);

                // 현재 상태 스냅샷 1회 송신 (선택)
                var snapshot = new
                {
                    loggedIn = TokenMemoryStore.IsLoggedIn,
                    expiresAtUnix = TokenMemoryStore.ExpiresAt.HasValue ? TokenMemoryStore.ExpiresAt.Value.ToUnixTimeSeconds() : (long?)null
                };
                await client.SendEventAsync("session_snapshot", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(snapshot));

                try
                {
                    // 스트림이 끊길 때까지 대기
                    await client.Completed;
                }
                finally
                {
                    EventBus.Remove(id);
                }
                return; // 중요: 여기서 응답 종료
            }
            else
            {
                ctx.Response.StatusCode = 404;
                await json(ctx, ApiResult<object>.Fail("not_found"));
                return;
            }
        }

        private async Task<T> Read<T>(HttpListenerContext ctx) where T : new()
        {
            using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
            {
                var body = await sr.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body)) return new T();
                try { return _json.Deserialize<T>(body); }
                catch { return new T(); }
            }
        }
    }
}

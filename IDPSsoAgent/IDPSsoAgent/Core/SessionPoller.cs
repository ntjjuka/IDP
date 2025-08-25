using IDPSsoAgent.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IDPSsoAgent.Core
{

    public static class SessionPoller
    {
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();


        // ===== 튜닝 포인트 =====
        private const int BasePeriodMs = 1000;   // 기본 주기: 1초
        private const int JitterMaxMs = 250;    // 각 틱마다 0~250ms 랜덤 지터
        private const int TimeoutMs = 4000;   // HTTP 타임아웃
        private static readonly int[] BackoffMsSequence = { 3000, 5000, 10000 }; // 연속 1/2/3+ 회 오류 시 주기

        private static Timer _timer;
        private static volatile bool _running;
        private static int _errorStreak = 0;
        private static readonly Random _rand = new Random();

        // 네트워크 튜닝(앱 시작 시 1회)
        static SessionPoller()
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.DefaultConnectionLimit = 256; // 동시 연결 여유
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
        }

        public static void Start()
        {
            if (_timer != null) return;

            _timer = new Timer(async _ => await TickAsync(), null,
                dueTime: BasePeriodMs,
                period: BasePeriodMs);
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        // 1. ApiHandlers.LogoutAsync()는 인스턴스 메서드이므로, 인스턴스가 필요합니다.
        // 2. SessionPoller에서 사용할 수 있도록 ApiHandlers 인스턴스를 정적으로 보관하거나, DI 등으로 전달받아야 합니다.
        // 3. 여기서는 간단히 static 필드로 인스턴스를 주입받는 방식으로 수정합니다.

        private static ApiHandlers _apiHandlers;

        public static void SetApiHandlers(ApiHandlers apiHandlers)
        {
            _apiHandlers = apiHandlers;
        }

        private static async Task TickAsync()
        {
            if (_running) return;
            _running = true;
            try
            {
                var refreshedOrValid = await _apiHandlers.EnsureFreshTokenAsync();
                if (!refreshedOrValid)
                {
                    await _apiHandlers.LogoutAsync();
                    return;
                }

                if (!TokenMemoryStore.IsLoggedIn) { ResetBackoffIfNeeded(); return; }

                if (JitterMaxMs > 0)
                    await Task.Delay(_rand.Next(0, JitterMaxMs));

                var ok = await CheckAliveAsync();
                if (!ok)
                {
                    // 인스턴스 메서드로 변경
                    if (_apiHandlers != null)
                        await _apiHandlers.LogoutAsync();
                    ApplyBackoff();
                    return;
                } 

                ResetBackoffIfNeeded();
            }
            catch
            {
                ApplyBackoff();
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>
        /// 인트로스펙션 → 실패 시 /userinfo 로 즉시 1회 재시도
        /// 하나라도 true면 살아있다고 판단
        /// </summary>
        private static async Task<bool> CheckAliveAsync()
        {
            var active = await IntrospectActiveAsync();
            if (active) return true;

            // fallback 한 번
            return await UserInfoAliveAsync();
        }

        // ========== 방법 A: /token/introspect ==========
        private static Task<bool> IntrospectActiveAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var access = TokenMemoryStore.AccessToken;
                    if (string.IsNullOrEmpty(access)) return false;

                    var url = $"{AppConfig.KeycloakBase}/realms/{AppConfig.Realm}/protocol/openid-connect/token/introspect";
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/x-www-form-urlencoded";
                    req.UserAgent = "IDPSsoAgent/1.0";
                    req.KeepAlive = true;
                    req.Timeout = TimeoutMs;
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                    var form = new StringBuilder();
                    form.Append("token=").Append(Uri.EscapeDataString(access))
                        .Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.AdminClientId));
                    if (!string.IsNullOrEmpty(AppConfig.AdminClientId))
                        form.Append("&client_secret=").Append(Uri.EscapeDataString(AppConfig.AdminClientSecret));

                    var bytes = Encoding.UTF8.GetBytes(form.ToString());
                    using (var os = req.GetRequestStream()) os.Write(bytes, 0, bytes.Length);

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        var json = sr.ReadToEnd();
                        var dict = _json.Deserialize<Dictionary<string, object>>(json);
                        return dict != null &&
                               dict.TryGetValue("active", out var v) &&
                               v is bool b && b;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        // ========== 방법 B: /userinfo ==========
        private static Task<bool> UserInfoAliveAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var access = TokenMemoryStore.AccessToken;
                    if (string.IsNullOrEmpty(access)) return false;

                    var url = $"{AppConfig.KeycloakBase}/realms/{AppConfig.Realm}/protocol/openid-connect/userinfo";
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.ContentType = "application/json";
                    req.UserAgent = "IDPSsoAgent/1.0";
                    req.KeepAlive = true;
                    req.Timeout = TimeoutMs;
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    req.Headers["Authorization"] = "Bearer " + access;

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        // 200이면 세션 유효
                        return (int)resp.StatusCode == 200;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        // ===== 백오프 제어 =====
        private static void ApplyBackoff()
        {
            _errorStreak++;
            var idx = Math.Min(_errorStreak - 1, BackoffMsSequence.Length - 1);
            var backoff = BackoffMsSequence[idx];

            // 약간의 지터 추가
            var jitter = _rand.Next(0, 250);
            var ms = backoff + jitter;

            _timer.Change(ms, ms);
        }

        private static void ResetBackoffIfNeeded()
        {
            if (_errorStreak == 0) return;
            _errorStreak = 0;
            _timer.Change(BasePeriodMs, BasePeriodMs);
        }
    }
}

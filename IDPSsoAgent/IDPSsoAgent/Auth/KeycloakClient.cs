using IDPSsoAgent.Config;
using IDPSsoAgent.Core;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IDPSsoAgent.Auth
{
    public class KeycloakClient
    {
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        private string Base() => AppConfig.KeycloakBase.TrimEnd('/');
        private string RealmPath() => "/realms/" + AppConfig.Realm + "/protocol/openid-connect";

        private string TokenEndpoint() => Base() + RealmPath() + "/token";
        private string UserInfoEndpoint() => Base() + RealmPath() + "/userinfo";
        private string LogoutEndpoint() => Base() + RealmPath() + "/logout";
        private string IntrospectEndpoint() => Base() + RealmPath() + "/token/introspect";
        private string revokeEndpoint() => Base() + RealmPath() + "/revoke";


        public sealed class CreatedUser
        {
            public string Id { get; set; }
            public string Username { get; set; }
        }

        public class TokenResponse
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public string id_token { get; set; }
            public int expires_in { get; set; }
            public int refresh_expires_in { get; set; }
            public string token_type { get; set; }
            public string error { get; set; }
            public string error_description { get; set; }
        }

        public Task RevokeTokensSafeAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return Task.CompletedTask;

            return Task.Run(() =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(revokeEndpoint());
                    req.Method = "POST";
                    req.ContentType = "application/x-www-form-urlencoded";
                    req.UserAgent = "IDPSsoAgent/1.0";
                    req.KeepAlive = true;
                    req.Timeout = 5000;

                    var form = "token_type_hint=refresh_token" +
                               "&token=" + Uri.EscapeDataString(refreshToken) +
                               "&client_id=" + Uri.EscapeDataString(AppConfig.ClientId) +
                               (string.IsNullOrEmpty(AppConfig.ClientSecret) ? "" :
                                "&client_secret=" + Uri.EscapeDataString(AppConfig.ClientSecret));

                    var bytes = Encoding.UTF8.GetBytes(form);
                    using (var os = req.GetRequestStream()) os.Write(bytes, 0, bytes.Length);
                    using (var resp = (HttpWebResponse)req.GetResponse()) { /* 200 expected */ }
                }
                catch { /* best effort */ }
            });
        }
        private ApiResult<TokenResponse> ParseTokenResponse(string json)
        {
            var obj = _json.Deserialize<TokenResponse>(json);
            if (!string.IsNullOrEmpty(obj.error))
                return ApiResult<TokenResponse>.Fail(obj.error + (string.IsNullOrEmpty(obj.error_description) ? "" : (": " + obj.error_description)));
            return ApiResult<TokenResponse>.Ok(obj);
        }

        // ROPC 로그인 (username/password)
        public System.Threading.Tasks.Task<ApiResult<TokenResponse>> LoginWithPasswordAsync(string username, string password, string scope)
        {
            return PostTokenForm(new StringBuilder()
                .Append("grant_type=password")
                .Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.ClientId))
                .Append(AppConfig.ClientSecret != null ? "&client_secret=" + Uri.EscapeDataString(AppConfig.ClientSecret) : "")
                .Append("&username=").Append(Uri.EscapeDataString(username))
                .Append("&password=").Append(Uri.EscapeDataString(password))
                .Append(!string.IsNullOrEmpty(scope) ? "&scope=" + Uri.EscapeDataString(scope) : "")
                .ToString());
        }

        // refresh_token으로 재발급
        public System.Threading.Tasks.Task<ApiResult<TokenResponse>> RefreshAsync(string refreshToken)
        {
            return PostTokenForm(new StringBuilder()
                .Append("grant_type=refresh_token")
                .Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.ClientId))
                .Append(AppConfig.ClientSecret != null ? "&client_secret=" + Uri.EscapeDataString(AppConfig.ClientSecret) : "")
                .Append("&refresh_token=").Append(Uri.EscapeDataString(refreshToken))
                .ToString());
        }

        private System.Threading.Tasks.Task<ApiResult<TokenResponse>> PostTokenForm(string form)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var req = (HttpWebRequest)WebRequest.Create(TokenEndpoint());
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "IDPSsoAgent/1.0";
                var body = Encoding.UTF8.GetBytes(form);
                using (var s = req.GetRequestStream()) { s.Write(body, 0, body.Length); }

                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var sr = new StreamReader(rs, Encoding.UTF8))
                        return ParseTokenResponse(sr.ReadToEnd());
                }
                catch (WebException wex)
                {
                    try
                    {
                        using (var rs = wex.Response?.GetResponseStream())
                        using (var sr = new StreamReader(rs, Encoding.UTF8))
                            return ParseTokenResponse(sr.ReadToEnd());
                    }
                    catch { return ApiResult<TokenResponse>.Fail(wex.Message); }
                }
            });
        }

        // userinfo
        public System.Threading.Tasks.Task<ApiResult<object>> GetUserInfoAsync(string accessToken)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var req = (HttpWebRequest)WebRequest.Create(UserInfoEndpoint());
                req.Method = "GET";
                req.UserAgent = "IDPSsoAgent/1.0";
                req.Headers["Authorization"] = "Bearer " + accessToken;

                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var sr = new StreamReader(rs, Encoding.UTF8))
                    {
                        var json = sr.ReadToEnd();
                        var obj = _json.Deserialize<object>(json);
                        return ApiResult<object>.Ok(obj);
                    }
                }
                catch (WebException wex)
                {
                    try
                    {
                        using (var rs = wex.Response?.GetResponseStream())
                        using (var sr = new StreamReader(rs, Encoding.UTF8))
                            return ApiResult<object>.Fail(sr.ReadToEnd());
                    }
                    catch { return ApiResult<object>.Fail(wex.Message); }
                }
            });
        }

        // introspect (confidential client 권장)
        public System.Threading.Tasks.Task<ApiResult<object>> IntrospectAsync(string token)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var req = (HttpWebRequest)WebRequest.Create(IntrospectEndpoint());
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "IDPSsoAgent/1.0";

                var sb = new StringBuilder();
                sb.Append("token=").Append(Uri.EscapeDataString(token));
                sb.Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.ClientId));
                if (!string.IsNullOrEmpty(AppConfig.ClientSecret))
                    sb.Append("&client_secret=").Append(Uri.EscapeDataString(AppConfig.ClientSecret));

                var body = Encoding.UTF8.GetBytes(sb.ToString());
                using (var s = req.GetRequestStream()) { s.Write(body, 0, body.Length); }

                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var sr = new StreamReader(rs, Encoding.UTF8))
                    {
                        var json = sr.ReadToEnd();
                        var obj = _json.Deserialize<object>(json); // { active: true/false, ... }
                        return ApiResult<object>.Ok(obj);
                    }
                }
                catch (WebException wex)
                {
                    try
                    {
                        using (var rs = wex.Response?.GetResponseStream())
                        using (var sr = new StreamReader(rs, Encoding.UTF8))
                            return ApiResult<object>.Fail(sr.ReadToEnd());
                    }
                    catch { return ApiResult<object>.Fail(wex.Message); }
                }
            });
        }

        // 로그아웃
        public System.Threading.Tasks.Task LogoutIfPossibleAsync(string refreshToken)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                if (string.IsNullOrEmpty(refreshToken)) return;

                var req = (HttpWebRequest)WebRequest.Create(LogoutEndpoint());
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "IDPSsoAgent/1.0";

                var sb = new StringBuilder();
                sb.Append("refresh_token=").Append(Uri.EscapeDataString(refreshToken));
                sb.Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.ClientId));
                if (!string.IsNullOrEmpty(AppConfig.ClientSecret))
                    sb.Append("&client_secret=").Append(Uri.EscapeDataString(AppConfig.ClientSecret));

                var body = Encoding.UTF8.GetBytes(sb.ToString());
                try
                {
                    using (var s = req.GetRequestStream()) { s.Write(body, 0, body.Length); }
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        // OK (200/204)
                    }
                }
                catch { /* ignore */ }
            });
        }
    }
}

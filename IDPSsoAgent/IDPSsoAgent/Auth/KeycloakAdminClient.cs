using IDPSsoAgent.Config;
using IDPSsoAgent.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace IDPSsoAgent.Auth
{
    public class KeycloakAdminClient
    {
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        private string Base() => AppConfig.KeycloakBase.TrimEnd('/');
        private string RealmAdmin() => Base() + "/admin/realms/" + AppConfig.Realm;


        // ---- 6) 회원가입 ----
        public System.Threading.Tasks.Task<ApiResult<object>> CreateUserAsync(RegisterRequest req)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                if (req == null || string.IsNullOrEmpty(req.username) || string.IsNullOrEmpty(req.password))
                    return ApiResult<object>.Fail("username/password required");

                var url = RealmAdmin() + "/users";
                var payload = new
                {
                    username = req.username,
                    email = req.email,
                    firstName = req.firstName,
                    lastName = req.lastName,
                    enabled = true,
                    emailVerified = false,
                    credentials = new[] { new { type = "password", value = req.password, temporary = false } }
                };
                var json = _json.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);

                var reqW = (HttpWebRequest)WebRequest.Create(url);
                reqW.Method = "POST";
                reqW.ContentType = "application/json";
                reqW.UserAgent = "IDPSsoAgent/1.0";
                // Admin client auth: Basic 대신 폼 인증이 일반적이지만, Admin REST는 Bearer가 아니라도 client 관리 권한으로 허용됨이 불안정할 수 있어
                // 안전하게 Bearer 없이 시도하지 않고, client credentials로 admin token 가져와도 됨. 단순화를 위해 Keycloak 26 기준 client auth 허용.
                // 필요 시: admin access token을 발급받아 Authorization: Bearer 붙이는 흐름으로 변경 가능.

                // 여기서는 client credentials로 토큰 한 번 받아 Bearer 붙이는 버전:
                string adminToken = GetAdminAccessToken();
                reqW.Headers["Authorization"] = "Bearer " + adminToken;

                using (var os = reqW.GetRequestStream()) { os.Write(bytes, 0, bytes.Length); }

                try
                {
                    using (var resp = (HttpWebResponse)reqW.GetResponse())
                    {
                        // 201 Created 기대, Location 헤더 포함
                        return ApiResult<object>.Ok(new { created = true, location = resp.Headers["Location"] });
                    }
                }
                catch (WebException wex)
                {
                    var code = (wex.Response as HttpWebResponse)?.StatusCode ?? 0;
                    string respText = null;
                    try { using (var rs = wex.Response?.GetResponseStream()) using (var sr = new StreamReader(rs, Encoding.UTF8)) respText = sr.ReadToEnd(); } catch { }
                    if (code == HttpStatusCode.Conflict)
                        return ApiResult<object>.Fail("conflict: username or email exists");
                    return ApiResult<object>.Fail("create_user_failed: " + (respText ?? wex.Message));
                }
            });
        }

        // ---- 7) 아이디 중복 체크 ----
        public System.Threading.Tasks.Task<ApiResult<object>> CheckUsernameAsync(string username)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                if (string.IsNullOrEmpty(username))
                    return ApiResult<object>.Fail("username required");

                var url = RealmAdmin() + "/users?username=" + Uri.EscapeDataString(username) + "&exact=true";

                var reqW = (HttpWebRequest)WebRequest.Create(url);
                reqW.Method = "GET";
                reqW.UserAgent = "IDPSsoAgent/1.0";
                string adminToken = GetAdminAccessToken();
                reqW.Headers["Authorization"] = "Bearer " + adminToken;

                try
                {
                    using (var resp = (HttpWebResponse)reqW.GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var sr = new StreamReader(rs, Encoding.UTF8))
                    {
                        var json = sr.ReadToEnd();
                        object[] arr = null;
                        try { arr = (object[])_json.DeserializeObject(json); } catch { }
                        bool exists = arr != null && arr.Length > 0;
                        return ApiResult<object>.Ok(new { exists = exists });
                    }
                }
                catch (WebException wex)
                {
                    string respText = null;
                    try { using (var rs = wex.Response?.GetResponseStream()) using (var sr = new StreamReader(rs, Encoding.UTF8)) respText = sr.ReadToEnd(); } catch { }
                    return ApiResult<object>.Fail("check_username_failed: " + (respText ?? wex.Message));
                }
            });
        }

        // ---- 8) 비밀번호 변경 ----
        public System.Threading.Tasks.Task<ApiResult<object>> ChangePasswordAsync(PasswordChangeRequest req)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                if (req == null || string.IsNullOrEmpty(req.username) || string.IsNullOrEmpty(req.newPassword))
                    return ApiResult<object>.Fail("username/newPassword required");

                string adminToken = GetAdminAccessToken();

                // 1) 사용자 ID 검색
                string findUrl = RealmAdmin() + "/users?username=" + Uri.EscapeDataString(req.username) + "&exact=true";
                string userId = null;
                try
                {
                    var f = (HttpWebRequest)WebRequest.Create(findUrl);
                    f.Method = "GET";
                    f.UserAgent = "IDPSsoAgent/1.0";
                    f.Headers["Authorization"] = "Bearer " + adminToken;
                    using (var resp = (HttpWebResponse)f.GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var sr = new StreamReader(rs, Encoding.UTF8))
                    {
                        var json = sr.ReadToEnd();
                        object[] arr = null;
                        try { arr = (object[])_json.DeserializeObject(json); } catch { }
                        if (arr != null && arr.Length > 0)
                        {
                            var dict = arr[0] as Dictionary<string, object>;
                            if (dict != null && dict.ContainsKey("id"))
                                userId = dict["id"] as string;
                        }
                    }
                }
                catch (WebException wex)
                {
                    return ApiResult<object>.Fail("find_user_failed: " + wex.Message);
                }

                if (string.IsNullOrEmpty(userId))
                    return ApiResult<object>.Fail("user_not_found");

                // (옵션) currentPassword 검증: ROPC 한 번 시도해서 확인
                if (!string.IsNullOrEmpty(req.currentPassword))
                {
                    var kc = new KeycloakClient();
                    var probe = kc.LoginWithPasswordAsync(req.username, req.currentPassword, null).Result;
                    if (!probe.ok) return ApiResult<object>.Fail("current_password_invalid");
                }

                // 2) reset-password
                var resetUrl = RealmAdmin() + "/users/" + userId + "/reset-password";
                var payload = new { type = "password", value = req.newPassword, temporary = false };
                var bodyJson = _json.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(bodyJson);

                var reqW = (HttpWebRequest)WebRequest.Create(resetUrl);
                reqW.Method = "PUT";
                reqW.ContentType = "application/json";
                reqW.UserAgent = "IDPSsoAgent/1.0";
                reqW.Headers["Authorization"] = "Bearer " + adminToken;

                using (var os = reqW.GetRequestStream()) { os.Write(bytes, 0, bytes.Length); }

                try
                {
                    using (var resp = (HttpWebResponse)reqW.GetResponse())
                    {
                        // 204 기대
                        return ApiResult<object>.Ok(new { changed = true });
                    }
                }
                catch (WebException wex)
                {
                    string respText = null;
                    try { using (var rs = wex.Response?.GetResponseStream()) using (var sr = new StreamReader(rs, Encoding.UTF8)) respText = sr.ReadToEnd(); } catch { }
                    return ApiResult<object>.Fail("change_password_failed: " + (respText ?? wex.Message));
                }
            });
        }


        // ---- 9) Introspect (admin client로 인증) ----
        public System.Threading.Tasks.Task<ApiResult<object>> IntrospectAsync(string token)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                if (string.IsNullOrEmpty(token))
                    return ApiResult<object>.Fail("token required");

                var url = Base() + "/realms/" + AppConfig.Realm + "/protocol/openid-connect/token/introspect";
                var sb = new StringBuilder();
                sb.Append("token=").Append(Uri.EscapeDataString(token));
                sb.Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.AdminClientId));
                sb.Append("&client_secret=").Append(Uri.EscapeDataString(AppConfig.AdminClientSecret));

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "IDPSsoAgent/1.0";
                var body = Encoding.UTF8.GetBytes(sb.ToString());
                using (var os = req.GetRequestStream()) { os.Write(body, 0, body.Length); }

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
                    string respText = null;
                    try { using (var rs = wex.Response?.GetResponseStream()) using (var sr = new StreamReader(rs, Encoding.UTF8)) respText = sr.ReadToEnd(); } catch { }
                    return ApiResult<object>.Fail("introspect_failed: " + (respText ?? wex.Message));
                }
            });

        }

        // ---- admin access token (client credentials) ----
        private string GetAdminAccessToken()
        {
            var tokenUrl = Base() + "/realms/" + AppConfig.Realm + "/protocol/openid-connect/token";
            var sb = new StringBuilder();
            sb.Append("grant_type=client_credentials");
            sb.Append("&client_id=").Append(Uri.EscapeDataString(AppConfig.AdminClientId));
            sb.Append("&client_secret=").Append(Uri.EscapeDataString(AppConfig.AdminClientSecret));

            var req = (HttpWebRequest)WebRequest.Create(tokenUrl);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.UserAgent = "IDPSsoAgent/1.0";
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            using (var os = req.GetRequestStream()) { os.Write(body, 0, body.Length); }

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var rs = resp.GetResponseStream())
            using (var sr = new StreamReader(rs, Encoding.UTF8))
            {
                var json = sr.ReadToEnd();
                var obj = _json.Deserialize<TokenResponse>(json);
                if (!string.IsNullOrEmpty(obj.error))
                    throw new Exception(obj.error + (string.IsNullOrEmpty(obj.error_description) ? "" : (": " + obj.error_description)));
                return obj.access_token;
            }
        }

        private class TokenResponse
        {
            public string access_token { get; set; }
            public string error { get; set; }
            public string error_description { get; set; }
        }
    }
}

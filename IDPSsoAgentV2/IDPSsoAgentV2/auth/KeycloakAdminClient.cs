using IDPSsoAgent.Config;
using IDPSsoAgent.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using static IDPSsoAgent.Auth.KeycloakClient;

namespace IDPSsoAgent.Auth
{
    public class KeycloakAdminClient
    {
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        private string Base() => AppConfig.KeycloakBase.TrimEnd('/');
        private string RealmAdmin() => Base() + "/admin/realms/" + AppConfig.Realm;

        public Task<ApiResult<bool>> LogoutAllSessionsByUsernameAsync(string username)
        {
            return Task.Run(() =>
            {
                try
                {
                    // 1) username → userId 조회
                    var urlSearch = RealmAdmin() + "/users?username=" + Uri.EscapeDataString(username);
                    var reqS = (HttpWebRequest)WebRequest.Create(urlSearch);
                    reqS.Method = "GET";
                    reqS.ContentType = "application/json";
                    reqS.UserAgent = "IDPSsoAgent/1.0";
                    reqS.Headers["Authorization"] = "Bearer " + GetAdminAccessToken();

                    string userId = null;
                    using (var resp = (HttpWebResponse)reqS.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        var json = sr.ReadToEnd();
                        var users = _json.Deserialize<List<Dictionary<string, object>>>(json);
                        if (users == null || users.Count == 0)
                            return ApiResult<bool>.Fail("user_not_found");
                        userId = users[0]["id"].ToString();
                    }

                    // 2) 모든 세션 종료: POST /users/{id}/logout
                    var url = RealmAdmin() + "/users/" + userId + "/logout";
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.UserAgent = "IDPSsoAgent/1.0";
                    req.Headers["Authorization"] = "Bearer " + GetAdminAccessToken();

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        // 일반적으로 204 No Content
                        if ((int)resp.StatusCode == 204 || (int)resp.StatusCode == 200)
                            return ApiResult<bool>.Ok(true);
                        return ApiResult<bool>.Fail("logout_failed: " + resp.StatusCode);
                    }
                }
                catch (WebException wex)
                {
                    var code = (wex.Response as HttpWebResponse) != null
                        ? ((HttpWebResponse)wex.Response).StatusCode.ToString()
                        : "no_response";
                    return ApiResult<bool>.Fail("logout_failed: " + code);
                }
                catch (Exception ex)
                {
                    return ApiResult<bool>.Fail("exception: " + ex.Message);
                }
            });
        }

        // ---- 6) 회원가입 ----
        public Task<ApiResult<CreatedUser>> CreateUserAsync(string username, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    var url = RealmAdmin() + "/users";
                    var body = new
                    {
                        username = username,
                        enabled = true,
                        credentials = new[] {
                    new { type = "password", value = password, temporary = false }
                }
                    };
                    var json = _json.Serialize(body);
                    var bytes = Encoding.UTF8.GetBytes(json);

                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.UserAgent = "IDPSsoAgent/1.0";
                    req.Headers["Authorization"] = "Bearer " + GetAdminAccessToken();

                    using (var os = req.GetRequestStream())
                    {
                        os.Write(bytes, 0, bytes.Length);
                    }

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        // Keycloak 보통: 201 Created + Location: /users/{id}
                        if ((int)resp.StatusCode == 201)
                        {
                            string id = null;
                            var loc = resp.Headers["Location"];
                            if (!string.IsNullOrEmpty(loc))
                            {
                                var idx = loc.LastIndexOf('/');
                                if (idx >= 0 && idx + 1 < loc.Length)
                                    id = loc.Substring(idx + 1);
                            }

                            // 혹시 Location 미제공 시: username 검색으로 보강
                            if (string.IsNullOrEmpty(id))
                            {
                                var search = RealmAdmin() + "/users?username=" + Uri.EscapeDataString(username);
                                var reqS = (HttpWebRequest)WebRequest.Create(search);
                                reqS.Method = "GET";
                                reqS.ContentType = "application/json";
                                reqS.UserAgent = "IDPSsoAgent/1.0";
                                reqS.Headers["Authorization"] = "Bearer " + GetAdminAccessToken();

                                using (var respS = (HttpWebResponse)reqS.GetResponse())
                                using (var sr = new StreamReader(respS.GetResponseStream()))
                                {
                                    var txt = sr.ReadToEnd();
                                    var arr = _json.Deserialize<List<Dictionary<string, object>>>(txt);
                                    if (arr != null && arr.Count > 0 && arr[0].ContainsKey("id") && arr[0]["id"] != null)
                                        id = arr[0]["id"].ToString();
                                }
                            }

                            return ApiResult<CreatedUser>.Ok(new CreatedUser { Id = id, Username = username });
                        }

                        if ((int)resp.StatusCode == 409)
                            return ApiResult<CreatedUser>.Fail("username_exists");

                        return ApiResult<CreatedUser>.Fail("register_failed: " + resp.StatusCode);
                    }
                }
                catch (WebException wex)
                {
                    var code = (wex.Response as HttpWebResponse) != null
                        ? ((HttpWebResponse)wex.Response).StatusCode.ToString()
                        : "no_response";
                    return ApiResult<CreatedUser>.Fail("register_failed: " + code);
                }
                catch (Exception ex)
                {
                    return ApiResult<CreatedUser>.Fail("exception: " + ex.Message);
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

        public Task<ApiResult<object>> AdminChangePasswordAsync(string username, string newPassword)
        {
            return Task.Run(() =>
            {
                try
                {
                    // 1) username 으로 사용자 검색
                    var urlSearch = RealmAdmin() + $"/users?username={Uri.EscapeDataString(username)}";
                    var reqS = (HttpWebRequest)WebRequest.Create(urlSearch);
                    reqS.Method = "GET";
                    reqS.ContentType = "application/json";
                    reqS.UserAgent = "IDPSsoAgent/1.0";
                    reqS.Headers["Authorization"] = "Bearer " + GetAdminAccessToken();

                    string userId;
                    using (var resp = (HttpWebResponse)reqS.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        var json = sr.ReadToEnd();
                        var users = _json.Deserialize<List<Dictionary<string, object>>>(json);
                        if (users == null || users.Count == 0)
                            return ApiResult<object>.Fail("user_not_found");

                        userId = users[0]["id"].ToString();
                    }

                    // 2) 비밀번호 변경 요청
                    var url = RealmAdmin() + $"/users/{userId}/reset-password";
                    var payload = new { type = "password", value = newPassword, temporary = false };
                    var jsonBody = _json.Serialize(payload);
                    var bytes = Encoding.UTF8.GetBytes(jsonBody);

                    var reqW = (HttpWebRequest)WebRequest.Create(url);
                    reqW.Method = "PUT";
                    reqW.ContentType = "application/json";
                    reqW.UserAgent = "IDPSsoAgent/1.0";
                    reqW.Headers["Authorization"] = "Bearer " + GetAdminAccessToken();

                    using (var os = reqW.GetRequestStream()) { os.Write(bytes, 0, bytes.Length); }
                    using (var resp = (HttpWebResponse)reqW.GetResponse())
                    {
                        if ((int)resp.StatusCode == 204 || (int)resp.StatusCode == 200)
                            return ApiResult<object>.Ok(new { changed = true });

                        return ApiResult<object>.Fail("change_failed: " + resp.StatusCode);
                    }
                }
                catch (WebException wex)
                {
                    return ApiResult<object>.Fail("change_failed: " + (wex.Response as HttpWebResponse)?.StatusCode);
                }
                catch (Exception ex)
                {
                    return ApiResult<object>.Fail("exception: " + ex.Message);
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
                        var obj = _json.Deserialize<object>(json);
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

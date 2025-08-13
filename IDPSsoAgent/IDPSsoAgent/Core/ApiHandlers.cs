using IDPSsoAgent.Auth;
using IDPSsoAgent.Config;
using IDPSsoAgent.Server.Sse;
using System;
using System.Threading.Tasks;

namespace IDPSsoAgent.Core
{
    public class ApiHandlers
    {
        private readonly KeycloakClient _kc = new KeycloakClient();
        private readonly KeycloakAdminClient _admin = new KeycloakAdminClient();

        //1)  토큰 만료 전이면 그대로, 만료면 refresh 시도
        private async Task<bool> EnsureFreshTokenAsync()
        {
            // 로그인 안 되어 있음
            if (!TokenMemoryStore.IsLoggedIn) return false;

            // 아직 유효
            if (TokenMemoryStore.ExpiresAt.HasValue && TokenMemoryStore.ExpiresAt.Value > DateTimeOffset.UtcNow.AddSeconds(5))
                return true;

            // refresh 시도
            var rt = TokenMemoryStore.RefreshToken;
            if (string.IsNullOrEmpty(rt)) return false;

            var res = await _kc.RefreshAsync(rt);
            if (!res.ok) return false;

            TokenMemoryStore.SaveTokens(
                res.data.access_token,
                res.data.refresh_token,
                res.data.id_token,
                res.data.expires_in);

            //토큰 자동 갱신 알림
            await EventBus.BroadcastAsync("token_refresh", new
            {
                loggedIn = true,
                expiresAtUnix = TokenMemoryStore.ExpiresAt.HasValue ? TokenMemoryStore.ExpiresAt.Value.ToUnixTimeSeconds() : (long?)null
            });

            return true;
        }

        //2) 로그인
        public async Task<ApiResult<object>> LoginAsync(LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.username) || string.IsNullOrWhiteSpace(req?.password))
                return ApiResult<object>.Fail("username/password required");

            if (TokenMemoryStore.IsLoggedIn && AppConfig.DenyLoginWhenActive)
            {
                var stillValid = await EnsureFreshTokenAsync();
                if (stillValid)
                {
                    return ApiResult<object>.Ok(new
                    {
                        loggedIn = true,
                        note = "already_active",
                        expires_at_unix = TokenMemoryStore.ExpiresAt.HasValue
                            ? TokenMemoryStore.ExpiresAt.Value.ToUnixTimeSeconds() : (long?)null
                    });
                }
            }

            var result = await _kc.LoginWithPasswordAsync(req.username, req.password, req.scope);
            if (!result.ok) return ApiResult<object>.Fail(result.error);

            TokenMemoryStore.SaveTokens(
                result.data.access_token,
                result.data.refresh_token,
                result.data.id_token,
                result.data.expires_in);

            //로그인 완료 알림
            await EventBus.BroadcastAsync("session_login", new
            {
                loggedIn = true,
                expiresAtUnix = TokenMemoryStore.ExpiresAt.HasValue ? TokenMemoryStore.ExpiresAt.Value.ToUnixTimeSeconds() : (long?)null
            });

            return ApiResult<object>.Ok(new { loggedIn = true });
        }

        //3) 로그아웃
        public async Task<ApiResult<object>> LogoutAsync()
        {
            await _kc.LogoutIfPossibleAsync(TokenMemoryStore.RefreshToken);
            TokenMemoryStore.Clear();

            //로그아웃 완료 알림
            await EventBus.BroadcastAsync("session_logout", new
            {
                loggedIn = false
            });

            return ApiResult<object>.Ok(new { loggedIn = false });
        }

        //4) 토큰
        public async Task<ApiResult<object>> GetTokenAsync()
        {
            if (!await EnsureFreshTokenAsync())
                return ApiResult<object>.Fail("not_logged_in");

            var v = new TokenView
            {
                access_token = TokenMemoryStore.AccessToken,
                has_refresh = !string.IsNullOrEmpty(TokenMemoryStore.RefreshToken),
                expires_at_unix = TokenMemoryStore.ExpiresAt?.ToUnixTimeSeconds()
            };
            return ApiResult<object>.Ok(v);
        }

        //5) 사용자 조회
        public async Task<ApiResult<object>> UserInfoAsync()
        {
            if (!await EnsureFreshTokenAsync())
                return ApiResult<object>.Fail("not_logged_in");

            var res = await _kc.GetUserInfoAsync(TokenMemoryStore.AccessToken);
            return res.ok ? ApiResult<object>.Ok(res.data) : ApiResult<object>.Fail(res.error);
        }

        // 6) 회원가입
        public async Task<ApiResult<object>> RegisterAsync(RegisterRequest req)
            => await _admin.CreateUserAsync(req);

        // 7) 아이디 중복
        public async Task<ApiResult<object>> CheckUsernameAsync(UsernameCheckRequest req)
            => await _admin.CheckUsernameAsync(req.username);

        // 8) 비밀번호 변경
        public async Task<ApiResult<object>> ChangePasswordAsync(PasswordChangeRequest req)
            => await _admin.ChangePasswordAsync(req);

        // 9) access_token 유효성 (관리자 클라이언트 사용)
        public async Task<ApiResult<object>> IntrospectAsync(string tokenFromQuery = null)
        {
            string token = tokenFromQuery;
            if (string.IsNullOrEmpty(token))
            {
                if (!await EnsureFreshTokenAsync())
                    return ApiResult<object>.Fail("not_logged_in");
                token = TokenMemoryStore.AccessToken;
            }

            var res = await _admin.IntrospectAsync(token);
            return res.ok ? ApiResult<object>.Ok(res.data) : ApiResult<object>.Fail(res.error);
        }
    }
}

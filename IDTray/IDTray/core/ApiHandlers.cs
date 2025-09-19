using IDTray.Auth;
using IDTray.Config;
using IDTray.Server.Sse;
using System;
using System.Threading.Tasks;

namespace IDTray.Core
{
    public class ApiHandlers
    {
        private readonly KeycloakClient _kc = new KeycloakClient();
        private readonly KeycloakAdminClient _admin = new KeycloakAdminClient();
        private static readonly System.Threading.SemaphoreSlim _refreshLock = new System.Threading.SemaphoreSlim(1, 1);

        //1)  토큰 만료 전이면 그대로, 만료면 refresh 시도
        public async Task<bool> EnsureFreshTokenAsync()
        {
            // 로그인 안 되어 있음
            if (!TokenMemoryStore.IsLoggedIn) return false;

            // 아직 유효(30초 여유)
            if (TokenMemoryStore.ExpiresAt.HasValue && TokenMemoryStore.ExpiresAt.Value > DateTimeOffset.UtcNow.AddSeconds(30))
                return true;
            
            // 리프레시 자체가 만료되었는지 먼저 확인
            if (TokenMemoryStore.RefreshExpiresAt.HasValue &&
                TokenMemoryStore.RefreshExpiresAt.Value <= DateTimeOffset.UtcNow)
                return false;

            // refresh 시도
            var rt = TokenMemoryStore.RefreshToken;
            if (string.IsNullOrEmpty(rt)) return false;


            // 동시 갱신 방지
            await _refreshLock.WaitAsync();
            try
            {
                // 다른 쓰레드가 먼저 갱신했을 수 있으니 재확인
                if (TokenMemoryStore.ExpiresAt.HasValue &&
                    TokenMemoryStore.ExpiresAt.Value > DateTimeOffset.UtcNow.AddSeconds(30))
                    return true;

                var res = await _kc.RefreshAsync(rt);
                if (!res.ok) return false;

                TokenMemoryStore.SaveTokens(
                    res.data.access_token,
                    string.IsNullOrEmpty(res.data.refresh_token) ? TokenMemoryStore.RefreshToken : res.data.refresh_token,
                    res.data.id_token,
                    res.data.expires_in,
                    res.data.refresh_expires_in
                );

                await EventBus.BroadcastAsync("token_refresh", new
                {
                    loggedIn = true,
                    expiresAtUnix = TokenMemoryStore.ExpiresAt?.ToUnixTimeSeconds()
                });

                return true;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        //2) 로그인
        public async Task<ApiResult<object>> LoginAsync(LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.username) || string.IsNullOrWhiteSpace(req?.password))
                return ApiResult<object>.Fail("username/password required");

            // 0) "마지막 로그인만 유효" 보장: 로그인 직전에 해당 사용자 모든 세션 강제 종료
            //    (다른 PC/브라우저/에이전트 포함)
            var kill = await _admin.LogoutAllSessionsByUsernameAsync(req.username);
            if (!kill.ok)
                return ApiResult<object>.Fail(kill.error ?? "terminate_sessions_failed");

            // 1) 로컬에 기존 세션이 있으면(동일/다른 유저 무관) 깨끗이 내려놓고 시작
            //    - 로컬 상태 불일치/레이스 방지
            if (TokenMemoryStore.IsLoggedIn)
            {
                // (선택) 리프레시 토큰이 있고, 안전하게 취소하고 싶으면 아래처럼 처리
                try { await _kc.RevokeTokensSafeAsync(TokenMemoryStore.RefreshToken); } catch { /* ignore */ }

                TokenMemoryStore.Clear();

                await EventBus.BroadcastAsync("session_logout", new
                {
                    loggedIn = false
                });
            }

            // 2) 새 로그인 시도
            var result = await _kc.LoginWithPasswordAsync(req.username, req.password, req.scope);
            if (!result.ok)
                return ApiResult<object>.Fail(result.error ?? "login_failed");

            // 3) 토큰 저장 + 로컬 상태 true
            TokenMemoryStore.SaveTokens(
                result.data.access_token,
                result.data.refresh_token,
                result.data.id_token,
                result.data.expires_in,
                result.data.refresh_expires_in);

            // 4) 로그인 완료 브로드캐스트
            await EventBus.BroadcastAsync("session_login", new
            {
                loggedIn = true,
                expiresAtUnix = TokenMemoryStore.ExpiresAt.HasValue
                    ? TokenMemoryStore.ExpiresAt.Value.ToUnixTimeSeconds()
                    : (long?)null
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
        // Register and return { sub, username } as in /userinfo
        public async Task<ApiResult<object>> RegisterAsync(RegisterRequest req)
        {
            var created = await _admin.CreateUserAsync(req.username, req.password);
            if (!created.ok) return ApiResult<object>.Fail(created.error);
            return ApiResult<object>.Ok(new { sub = created.data.Id, username = created.data.Username });
        }
        // 7) 아이디 중복
        public async Task<ApiResult<object>> CheckUsernameAsync(UsernameCheckRequest req)
            => await _admin.CheckUsernameAsync(req.username);

        // 8) 비밀번호 변경
        public async Task<ApiResult<object>> ChangePasswordAsync(PasswordChangeRequest req)
            => await _admin.ChangePasswordAsync(req);

        // 9) 관리자 비밀번호 변경
        public async Task<ApiResult<object>> AdminChangePasswordAsync(PasswordAdminChangeRequest req)
            => await _admin.AdminChangePasswordAsync(req.username, req.newPassword);

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

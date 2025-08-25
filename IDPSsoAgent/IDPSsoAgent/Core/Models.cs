using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDPSsoAgent.Core
{
    // 공통 응답
    public class ApiResult<T>
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public T data { get; set; }

        public static ApiResult<T> Ok(T d) => new ApiResult<T> { ok = true, data = d };
        public static ApiResult<T> Fail(string e) => new ApiResult<T> { ok = false, error = e };
    }

    // 요청 DTO들
    public class LoginRequest
    {
        public string username { get; set; }
        public string password { get; set; }
        public string scope { get; set; } // optional
    }

    public class RegisterRequest
    {
        public string username { get; set; }
        public string password { get; set; }
        public string email { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
    }

    public class PasswordChangeRequest
    {
        public string username { get; set; }
        public string currentPassword { get; set; }
        public string newPassword { get; set; }
    }

    public class PasswordAdminChangeRequest
    {
        public string username { get; set; }
        public string newPassword { get; set; }
    }

    public class UsernameCheckRequest
    {
        public string username { get; set; }
    }

    // 토큰/상태 응답
    public class TokenView
    {
        public string access_token { get; set; }   // 마스킹할 수도 있음
        public long? expires_at_unix { get; set; } // 만료시각(초)
        public bool has_refresh { get; set; }
    }

    public class StatusView
    {
        public bool listening { get; set; }
        public bool loggedIn { get; set; }
        public int port { get; set; }
    }
}

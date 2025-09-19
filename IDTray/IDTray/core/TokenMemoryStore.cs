using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDTray.Core
{
    public static class TokenMemoryStore
    {
        private static readonly object _lock = new object();

        public static string AccessToken { get; private set; }
        public static string RefreshToken { get; private set; }
        public static string IdToken { get; private set; }
        public static DateTimeOffset? ExpiresAt { get; private set; }
        public static string LastUsername { get; private set; }
        public static DateTimeOffset? LoggedInAt { get; private set; }
        public static DateTimeOffset? RefreshExpiresAt { get; private set; }

        public static bool IsLoggedIn { get; private set; }

        public static void SaveTokens(string access, string refresh, string id, int expiresInSeconds, int? refreshExpiresInSeconds = null, string usernameHint = null)
        {
            lock (_lock)
            {
                AccessToken = access;
                if (!string.IsNullOrEmpty(refresh))
                    RefreshToken = refresh; // 회전 시 새 RT로 교체
                IdToken = id;
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
                LoggedInAt = DateTimeOffset.UtcNow;
                if (!string.IsNullOrEmpty(usernameHint)) LastUsername = usernameHint;
                if (refreshExpiresInSeconds.HasValue && refreshExpiresInSeconds.Value > 0)
                    RefreshExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshExpiresInSeconds.Value);
                IsLoggedIn = true;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                AccessToken = null;
                RefreshToken = null;
                IdToken = null;
                ExpiresAt = null;
                LoggedInAt = null;
                IsLoggedIn = false;
            }
        }
    }
}

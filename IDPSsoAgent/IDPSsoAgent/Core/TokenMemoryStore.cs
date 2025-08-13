using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDPSsoAgent.Core
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

        public static bool IsLoggedIn
        {
            get
            {
                lock (_lock)
                {
                    return !string.IsNullOrEmpty(AccessToken) && ExpiresAt.HasValue && ExpiresAt.Value > DateTimeOffset.UtcNow;
                }
            }
        }


        public static void SaveTokens(string access, string refresh, string id, int expiresInSeconds, string usernameHint = null)
        {
            lock (_lock)
            {
                AccessToken = access;
                RefreshToken = refresh;
                IdToken = id;
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
                LoggedInAt = DateTimeOffset.UtcNow;
                if (!string.IsNullOrEmpty(usernameHint)) LastUsername = usernameHint;
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
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDPSsoAgent.Config
{
    public static class AppConfig
    {
        // 포트 고정: 5400
        public const int ListenPort = 5400;

        // Keycloak 고정 설정 (http만 사용)
        public static string KeycloakBase = "http://127.0.0.1:5500";    // IDP 서버 주소, 포트
        public static string Realm = "master";                          // 고정 realm
        public static string ClientId = "cpp_sp_app";                   // 고정 client
        public static string ClientSecret = null;                       // public client이면 null

        // 로컬 호출 보호를 위한 헤더키 (SP와 사전 공유)
        public const string AgentHeaderName = "X-SSO-Agent-Key";
        public const string AgentHeaderValue = "change-me-very-secret"; // 실제 배포 전 교체

        public static bool DenyLoginWhenActive = true; // 이미 세션이 있으면 /login 거절(권장)

        // Admin API용 계정
        public static string AdminClientId = "sso-agent-admin";             //confidentail client
        public static string AdminClientSecret = "sAFLO7yWaRZAkLgZfYBMIbjc2GyOYH4W";   // secret key
    }
}

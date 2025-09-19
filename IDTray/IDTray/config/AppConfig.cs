using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IDTray.Config
{
    // =========================
    // AppConfig : INI 기반 로더
    // =========================
    public static class AppConfig
    {
        // 실행 파일 옆의 agent.ini 사용 (포터블 배포용)
        private static readonly string IniPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent.ini");

        // 내부 캐시 (핫리로드 필요하면 Reload() 호출)
        private static volatile AgentConfigV1 _current;
        private static readonly object _lock = new object();

        // ----- 외부에서 쓰던 접근 방식 유지 (이름, 타입 그대로) -----
        public static int ListenPort => Current.ListenPort;

        public static string KeycloakBase => Current.KeycloakBase;
        public static string Realm => Current.Realm;
        public static string ClientId => Current.ClientId;
        public static string ClientSecret => Current.ClientSecret;

        public static string AgentHeaderName => Current.AgentHeaderName;
        public static string AgentHeaderValue => Current.AgentHeaderValue;

        public static bool DenyLoginWhenActive => Current.DenyLoginWhenActive;

        public static string AdminClientId => Current.AdminClientId;
        public static string AdminClientSecret => Current.AdminClientSecret;

        // 필요시 런타임 재적용
        public static void Reload()
        {
            lock (_lock) _current = null;
        }

        private static AgentConfigV1 Current
        {
            get
            {
                if (_current != null) return _current;
                lock (_lock)
                {
                    if (_current == null)
                        _current = LoadFromIniOrCreateDefault();
                    return _current;
                }
            }
        }

        private static AgentConfigV1 LoadFromIniOrCreateDefault()
        {
            // 1) 기본값
            var cfg = new AgentConfigV1
            {
                ListenPort = 5400,

                KeycloakBase = null,
                Realm = "master",
                ClientId = null,
                ClientSecret = null,

                AgentHeaderName = "X-SSO-Agent-Key",
                AgentHeaderValue = null,

                DenyLoginWhenActive = true,

                AdminClientId = null,
                AdminClientSecret = null
            };

            // 2) INI가 없으면 생성 후 기본값 반환
            if (!File.Exists(IniPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IniPath));
                File.WriteAllText(IniPath, SampleIni(cfg), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return cfg;
            }

            // 3) INI 읽기
            var ini = IniFile.Load(IniPath);

            // ----- 매핑 (섹션/키가 없으면 기본값 유지) -----
            if (ini.TryGet("Agent", "ListenPort", out var sp) && int.TryParse(sp, out var port)) cfg.ListenPort = port;

            if (ini.TryGet("Keycloak", "BaseUrl", out var kc)) cfg.KeycloakBase = Unwrap(kc);
            if (ini.TryGet("Keycloak", "Realm", out var realm)) cfg.Realm = Unwrap(realm);
            if (ini.TryGet("Keycloak", "ClientId", out var cid)) cfg.ClientId = Unwrap(cid);
            if (ini.TryGet("Keycloak", "ClientSecret", out var cs)) cfg.ClientSecret = Unwrap(cs);

            if (ini.TryGet("Agent", "HeaderName", out var hn)) cfg.AgentHeaderName = Unwrap(hn);
            if (ini.TryGet("Agent", "HeaderValue", out var hv)) cfg.AgentHeaderValue = Unwrap(hv);

            if (ini.TryGet("Agent", "DenyLoginWhenActive", out var d) && bool.TryParse(d, out var deny))
                cfg.DenyLoginWhenActive = deny;

            if (ini.TryGet("Admin", "AdminClientId", out var aid)) cfg.AdminClientId = Unwrap(aid);
            if (ini.TryGet("Admin", "AdminClientSecret", out var asv)) cfg.AdminClientSecret = Unwrap(asv);

            // 4) 환경변수(옵션)로 오버라이드
            string Env(string k) => Environment.GetEnvironmentVariable(k);
            if (int.TryParse(Env("IDP_LISTEN_PORT"), out var ep)) cfg.ListenPort = ep;

            cfg.KeycloakBase = Env("IDP_KEYCLOAK_BASE") ?? cfg.KeycloakBase;
            cfg.Realm = Env("IDP_REALM") ?? cfg.Realm;
            cfg.ClientId = Env("IDP_CLIENT_ID") ?? cfg.ClientId;
            cfg.ClientSecret = Env("IDP_CLIENT_SECRET") ?? cfg.ClientSecret;
            cfg.AgentHeaderName = Env("IDP_AGENT_HEADER_NAME") ?? cfg.AgentHeaderName;
            cfg.AgentHeaderValue = Env("IDP_AGENT_HEADER_VALUE") ?? cfg.AgentHeaderValue;
            cfg.AdminClientId = Env("IDP_ADMIN_CLIENT_ID") ?? cfg.AdminClientId;
            cfg.AdminClientSecret = Env("IDP_ADMIN_CLIENT_SECRET") ?? cfg.AdminClientSecret;
            if (bool.TryParse(Env("IDP_DENY_LOGIN_WHEN_ACTIVE"), out var ed)) cfg.DenyLoginWhenActive = ed;

            return cfg;
        }

        // 값 복호화 훅: aespb:/dpapi: 프리픽스 지원
        private static string Unwrap(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            value = value.Trim();

            if (value.StartsWith("aespb:", StringComparison.OrdinalIgnoreCase))
                return PortableCrypto.Decrypt(value);

            if (value.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase))
                return DpapiHelper.UnprotectFromString(value);

            return value; // 평문
        }

        private static string SampleIni(AgentConfigV1 c) =>
$@"
;; IDTray portable config (UTF-8)
;; 평문으로 시작해도 되고, 민감값은 aespb:/dpapi: 로 암호문을 넣을 수 있습니다.
;;  - aespb:  휴대형(다른 PC로 복사 동작) — 보안은 제한적
;;  - dpapi:  해당 PC에 종속 — 복사 불가(보안 강함)

[Agent]
HeaderValue = {c.AgentHeaderValue}

[Keycloak]
BaseUrl = {c.KeycloakBase}
Realm = {c.Realm}
ClientId = {c.ClientId}

[Admin]
AdminClientId = {c.AdminClientId}
AdminClientSecret =       ; 평문 또는 aespb:/dpapi:
";
    }

    // =========================
    // 구성 모델
    // =========================
    public class AgentConfigV1
    {
        public int ListenPort { get; set; }

        public string KeycloakBase { get; set; }
        public string Realm { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }

        public string AgentHeaderName { get; set; }
        public string AgentHeaderValue { get; set; }
        public bool DenyLoginWhenActive { get; set; }

        public string AdminClientId { get; set; }
        public string AdminClientSecret { get; set; }
    }

    // =========================
    // 단순 INI 파서 (주석 ; # 지원)
    // =========================
    internal sealed class IniFile
    {
        private readonly Dictionary<(string Section, string Key), string> _map;

        private IniFile(Dictionary<(string, string), string> map) => _map = map;

        public static IniFile Load(string path)
        {
            var map = new Dictionary<(string, string), string>(StringTupleComparer.OrdinalIgnoreCase);
            string section = "";

            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                // 인라인 주석 제거(간단 규칙)
                int iHash = val.IndexOf('#');
                int iSemi = val.IndexOf(';');
                int cut = new[] { iHash, iSemi }.Where(i => i >= 0).DefaultIfEmpty(int.MaxValue).Min();
                if (cut != int.MaxValue) val = val.Substring(0, cut).Trim();

                // 따옴표 제거
                if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
                    val = val.Substring(1, val.Length - 2);

                map[(section, key)] = val;
            }

            return new IniFile(map);
        }

        public bool TryGet(string section, string key, out string value)
            => _map.TryGetValue((section, key), out value);

        // 섹션/키 대소문자 무시용
        private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
        {
            public static readonly StringTupleComparer OrdinalIgnoreCase = new StringTupleComparer();
            public bool Equals((string, string) x, (string, string) y) =>
                string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string, string) obj) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1) * 397 ^
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2);
        }
    }

    // =========================
    // 선택) dpapi: 프리픽스 복호화
    // =========================
    internal static class DpapiHelper
    {
        // 서비스면 LocalMachine, 사용자 앱이면 CurrentUser로 바꿔도 됨
        private const DataProtectionScope Scope = DataProtectionScope.LocalMachine;

        // 설치 시 난수로 교체/외부 보관하면 더 안전
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IDTray-v1");

        public static string UnprotectFromString(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            if (!token.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) return token;

            var b64 = token.Substring(6);
            var cipher = Convert.FromBase64String(b64);
            var plain = ProtectedData.Unprotect(cipher, Entropy, Scope);
            return Encoding.UTF8.GetString(plain);
        }

        public static string ProtectToString(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain;
            var bytes = Encoding.UTF8.GetBytes(plain);
            var cipher = ProtectedData.Protect(bytes, Entropy, Scope);
            return "dpapi:" + Convert.ToBase64String(cipher);
        }
    }

    // =========================
    // 선택) aespb: 휴대형(포터블) 암복호화
    //  - 다른 PC로 복사해도 동작(보안은 제한적)
    // =========================
    internal static class PortableCrypto
    {
        // ⚠️ 빌드마다 바꾸고, 난독화/분할 상수 등으로 지연 방어 권장
        private static byte[] Key()
        {
            var pass = "IDTray#K3y-2025"; // 예시. 파이프라인에서 교체 자동화 추천
            return SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(pass));
        }

        public static string Encrypt(string plain)
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Key();
            aes.GenerateIV();

            var enc = aes.CreateEncryptor();
            var pt = Encoding.UTF8.GetBytes(plain);
            var ct = enc.TransformFinalBlock(pt, 0, pt.Length);

            var h = new HMACSHA256(Key());
            var tag = h.ComputeHash(Concat(aes.IV, ct));

            return "aespb:" + Convert.ToBase64String(Concat(aes.IV, ct, tag));
        }

        public static string Decrypt(string token)
        {
            if (!token.StartsWith("aespb:", StringComparison.OrdinalIgnoreCase))
                return token;

            var blob = Convert.FromBase64String(token.Substring(6));


            // C# 7.3 호환: 수동으로 배열 복사
            var iv = new byte[16];
            Buffer.BlockCopy(blob, 0, iv, 0, 16);

            var tag = new byte[32];
            Buffer.BlockCopy(blob, blob.Length - 32, tag, 0, 32);

            var ctLen = blob.Length - 16 - 32;
            var ct = new byte[ctLen];
            Buffer.BlockCopy(blob, 16, ct, 0, ctLen);

            var h = new HMACSHA256(Key());
            var expect = h.ComputeHash(Concat(iv, ct));
            if (!CryptographicOperations.FixedTimeEquals(tag, expect))
                throw new CryptographicException("Integrity check failed");

            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            aes.Key = Key(); aes.IV = iv;

            var dec = aes.CreateDecryptor();
            var pt = dec.TransformFinalBlock(ct, 0, ct.Length);
            return Encoding.UTF8.GetString(pt);
        }

        private static byte[] Concat(params byte[][] xs)
        {
            var ms = new MemoryStream();
            foreach (var x in xs) ms.Write(x, 0, x.Length);
            return ms.ToArray();
        }
    }

    // ... (생략)

    // PortableCrypto 클래스 내부에 CryptographicOperations가 없을 경우 직접 구현 추가
    internal static class CryptographicOperations
    {
        // .NET Core 2.x/Framework 호환: FixedTimeEquals 직접 구현
        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
                diff |= left[i] ^ right[i];
            return diff == 0;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace IDTray
{
    public static class HttpsSetup
    {
        public const string ROOT_CN = "IDP Agent Root CA";
        public const string LEAF_CN = "IDP Agent Local HTTPS";

        private const int ROOT_VALID_YEARS = 60; // 루트 60년
        private const int LEAF_VALID_YEARS = 50; // 리프 50년

        // ─────────────────────────────────────────────────────────────────────
        // 메인 셋업
        // ─────────────────────────────────────────────────────────────────────
        public static void EnsureAll(int port, string logPath = null)
        {
            void Log(string m)
            {
                if (string.IsNullOrEmpty(logPath)) return;
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {m}\r\n", Encoding.UTF8);
            }

            var pd = Program.PD;
            var crlPath = Path.Combine(pd, "crl", "root.crl");
            var cdpUri = "file:///" + crlPath.Replace("\\", "/");

            Log("setup start");

            // (0) 잔여물 정리
            NetshHelper.ResetAllForPort(port);
            Log("netsh cleaned");

            // (1) 루트 생성/선택 (LM\My)
            var root = GetOrCreateRootInLMMy(ROOT_CN, Log);

            // (2) 루트 신뢰 등록
            TrustRootIfNeeded(root, Log);

            // (3) CRL 생성/갱신
            EnsureRootCrl(root, crlPath, Log);

            // (4) 리프 발급 (SAN=127.0.0.1, ::1, CDP=file:///.../root.crl)
            var leaf = CreateOrUpdateLeafWithCdp(root, LEAF_CN, cdpUri, Log);

            // (5) http.sys 바인딩 (정확 IP만)
            RebindIpOnly(port, leaf.Thumbprint, Log);

            Log("setup ok");
            // Program에서 따로 센티널을 쓰지만, 혹시 단독 실행 대비
            try { File.WriteAllText(Path.Combine(Program.PD, "setup.ok"), DateTimeOffset.Now.ToString("O")); } catch { }
        }

        public static void FullWipe(int port, string logPath = null)
        {
            void Log(string m)
            {
                if (string.IsNullOrEmpty(logPath)) return;
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {m}\r\n", Encoding.UTF8);
            }

            Log("full wipe start");

            NetshHelper.ResetAllForPort(port);
            Log("netsh cleaned");

            RemoveCertsByCn(LEAF_CN, StoreName.My, StoreLocation.LocalMachine, Log);
            RemoveCertsByCn(ROOT_CN, StoreName.My, StoreLocation.LocalMachine, Log);
            RemoveCertsByCn(ROOT_CN, StoreName.Root, StoreLocation.LocalMachine, Log);
            RemoveCertsByCn(ROOT_CN, StoreName.Root, StoreLocation.CurrentUser, Log);

            var crlDir = Path.Combine(Program.PD, "crl");
            try { if (Directory.Exists(crlDir)) Directory.Delete(crlDir, true); } catch { }
            try { if (File.Exists(Path.Combine(Program.PD, "setup.ok"))) File.Delete(Path.Combine(Program.PD, "setup.ok")); } catch { }

            Log("certs/CRL removed");
            Log("full wipe done");
        }

        // 점검용
        public static bool IsRootTrusted(string cn)
        {
            bool Has(string simpleCn, StoreLocation loc)
            {
                var st = new X509Store(StoreName.Root, loc);
                st.Open(OpenFlags.ReadOnly);
                foreach (var c in st.Certificates.OfType<X509Certificate2>())
                {
                    try
                    {
                        if (string.Equals(c.GetNameInfo(X509NameType.SimpleName, false), simpleCn,
                            StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
                return false;
            }
            return Has(cn, StoreLocation.LocalMachine) || Has(cn, StoreLocation.CurrentUser);
        }

        public static bool IsBindingOk(int port)
        {
            var o = NetshHelper.Run("http show sslcert");
            return System.Text.RegularExpressions.Regex.IsMatch(o, $@"127\.0\.0\.1:{port}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && System.Text.RegularExpressions.Regex.IsMatch(o, $@"\[\:\:1\]:{port}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // ── 루트 확보 ─────────────────────────────────────────────────────────
        static X509Certificate2 GetOrCreateRootInLMMy(string cn, Action<string> Log)
        {
            var candidates = Find(StoreName.My, StoreLocation.LocalMachine, cn)
                             .OrderByDescending(c => c.NotAfter).ToList();

            var nowUtc = DateTimeOffset.UtcNow;
            var desiredRootNa = nowUtc.AddYears(ROOT_VALID_YEARS);

            var exist = candidates.FirstOrDefault();
            if (exist != null)
            {
                bool longEnough = exist.NotAfter.ToUniversalTime() >= desiredRootNa.AddDays(-30).UtcDateTime;
                bool exportable = CanExportPrivateKey(exist);
                if (longEnough && exportable)
                {
                    Log($"root exist (satisfy): {exist.Thumbprint}");
                    return exist;
                }

                Log($"root exist but {(longEnough ? "" : "short ")}{(exportable ? "" : "non-exportable ")}→ replace");
                RemoveCertsByCn(cn, StoreName.My, StoreLocation.LocalMachine, Log);
                RemoveCertsByCn(cn, StoreName.Root, StoreLocation.LocalMachine, Log);
                RemoveCertsByCn(cn, StoreName.Root, StoreLocation.CurrentUser, Log);
            }

            // ★ CSP 기반(Exportable) 루트 새로 생성
            var root = CreateRootExportable(cn, Log);

            using (var my = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            { my.Open(OpenFlags.ReadWrite); my.Add(root); }

            Log($"root created: {root.Thumbprint}");
            return root;
        }

        // CSP 전용(안정)
        private static X509Certificate2 CreateRootExportable(string cn, Action<string> Log)
        {
            var csp = new CspParameters(24) // PROV_RSA_AES (SHA-256)
            {
                ProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider",
                KeyContainerName = "IDPAgentRoot-" + Guid.NewGuid().ToString("N"),
                KeyNumber = (int)KeyNumber.Signature,
                Flags = CspProviderFlags.UseMachineKeyStore
                      | CspProviderFlags.UseArchivableKey
                      | CspProviderFlags.NoPrompt
            };

            var rsa = new RSACryptoServiceProvider(2048, csp) { PersistKeyInCsp = true };

            var req = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var nb = DateTimeOffset.UtcNow.AddMinutes(-5);
            var na = nb.AddYears(ROOT_VALID_YEARS);
            var tmp = req.CreateSelfSigned(nb, na);

            var pfx = tmp.Export(X509ContentType.Pfx, "");
            var root = new X509Certificate2(pfx, "",
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

            using (var priv = root.GetRSAPrivateKey()) { _ = priv.ExportParameters(true); } // sanity

            Log("root created via CSP (exportable)");
            return root;
        }

        static void TrustRootIfNeeded(X509Certificate2 root, Action<string> Log)
        {
            foreach (var loc in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                var st = new X509Store(StoreName.Root, loc);
                st.Open(OpenFlags.ReadWrite);
                if (!st.Certificates.OfType<X509Certificate2>()
                    .Any(c => c.Thumbprint.Equals(root.Thumbprint, StringComparison.OrdinalIgnoreCase)))
                {
                    st.Add(new X509Certificate2(root.RawData));
                    Log($"root trusted -> {loc}");
                }
            }
        }

        private static bool CanExportPrivateKey(X509Certificate2 cert)
        {
            try
            {
                var rsa = cert.GetRSAPrivateKey();
                if (rsa == null) return false;
                _ = rsa.ExportParameters(true);
                return true;
            }
            catch { return false; }
        }

        // ── CRL ───────────────────────────────────────────────────────────────
        static void EnsureRootCrl(X509Certificate2 root, string crlPath, Action<string> Log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(crlPath));

            var bcRoot = new X509CertificateParser().ReadCertificate(root.RawData);
            var bcKey = DotNetUtilities.GetKeyPair(root.GetRSAPrivateKey()).Private;

            var gen = new X509V2CrlGenerator();
            gen.SetIssuerDN(bcRoot.SubjectDN);
            var now = DateTime.UtcNow.AddMinutes(-5);
            gen.SetThisUpdate(now);
            gen.SetNextUpdate(now.AddDays(30)); // 30일 후 갱신

            var sigFactory = new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory("SHA256WITHRSA", bcKey);
            var crl = gen.Generate(sigFactory);
            File.WriteAllBytes(crlPath, crl.GetEncoded());

            Log($"crl written: {crlPath}");
        }


        static X509Certificate2 CreateOrUpdateLeafWithCdp(X509Certificate2 issuer, string cn, string cdpFileUri, Action<string> Log)
        {
            // 1) 머신 키 저장소에 영구/내보내기 가능한 CSP 키 생성
            var csp = new CspParameters(24) // PROV_RSA_AES (SHA-256)
            {
                ProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider",
                KeyContainerName = "IDPAgentLeaf-" + Guid.NewGuid().ToString("N"),
                KeyNumber = (int)KeyNumber.Exchange, // 서버 인증 용도면 Exchange로 생성하는 게 안전
                Flags = CspProviderFlags.UseMachineKeyStore
                      | CspProviderFlags.UseArchivableKey
                      | CspProviderFlags.NoPrompt
            };
            var rsa = new RSACryptoServiceProvider(2048, csp) { PersistKeyInCsp = true };

            var req = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            // EKU: 서버 인증
            var eku = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));

            // KeyUsage
            req.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

            // SAN: 127.0.0.1, ::1
            var san = new SubjectAlternativeNameBuilder();
            san.AddIpAddress(IPAddress.Loopback);
            san.AddIpAddress(IPAddress.IPv6Loopback);
            req.CertificateExtensions.Add(san.Build());

            // CDP(file:///.../root.crl)
            var dpName = new Org.BouncyCastle.Asn1.X509.DistributionPointName(
                new Org.BouncyCastle.Asn1.X509.GeneralNames(
                    new Org.BouncyCastle.Asn1.X509.GeneralName(
                        Org.BouncyCastle.Asn1.X509.GeneralName.UniformResourceIdentifier, cdpFileUri)));
            var dp = new Org.BouncyCastle.Asn1.X509.DistributionPoint(dpName, null, null);
            var cdp = new Org.BouncyCastle.Asn1.X509.CrlDistPoint(new[] { dp });
            req.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509Extension("2.5.29.31", cdp.GetDerEncoded(), false));

            // 유효기간(발급자 범위로 클램핑)
            var nowUtc = DateTimeOffset.UtcNow;
            var issuerNB = new DateTimeOffset(issuer.NotBefore.ToUniversalTime());
            var issuerNA = new DateTimeOffset(issuer.NotAfter.ToUniversalTime());

            var desiredNB = nowUtc.AddMinutes(-5);
            var desiredNA = nowUtc.AddYears(LEAF_VALID_YEARS);

            var notBefore = desiredNB < issuerNB ? issuerNB.AddSeconds(1) : desiredNB;
            var notAfter = desiredNA > issuerNA ? issuerNA.AddDays(-1) : desiredNA;

            var issuerKey = issuer.GetRSAPrivateKey();
            var serial = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
            var tmpLeaf = req.Create(issuer, notBefore, notAfter, serial);

            // 2) PFX로 강제 내보내기 → MachineKeySet으로 재임포트 (개인키 연결 보장)
            var pfx = tmpLeaf.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx, "");
            var leaf = new X509Certificate2(pfx, "",
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

            // 3) 이전 동일 CN 정리(선택)
            RemoveCertsByCn(LEAF_CN, StoreName.My, StoreLocation.LocalMachine, _ => { });

            // 4) LM\My 저장
            using (var my = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            { my.Open(OpenFlags.ReadWrite); my.Add(leaf); }

            // sanity
            using (var test = leaf.GetRSAPrivateKey()) { _ = test.ExportParameters(true); }

            Log($"leaf issued: {leaf.Thumbprint}");
            return leaf;
        }


        // ── http.sys 바인딩 ──────────────────────────────────────────────────
        static void RebindIpOnly(int port, string thumb, Action<string> Log)
        {
            string tp = (thumb ?? "").Replace(" ", "").ToUpperInvariant();

            // 기존 바인딩 삭제
            NetshHelper.Run($"http delete sslcert ipport=127.0.0.1:{port}");
            NetshHelper.Run($"http delete sslcert ipport=[::1]:{port}");
            NetshHelper.Run($"http delete sslcert ipport=0.0.0.0:{port}");
            NetshHelper.Run($"http delete sslcert ipport=[::]:{port}");
            NetshHelper.Run($"http delete urlacl url=https://127.0.0.1:{port}/");
            NetshHelper.Run($"http delete urlacl url=https://[::1]:{port}/");
            NetshHelper.Run($"http delete urlacl url=https://+:{port}/");

            // 바인딩 추가
            var appid = "{" + Guid.NewGuid().ToString() + "}";
            NetshHelper.RunExpectZero($"http add urlacl url=https://127.0.0.1:{port}/ sddl=D:(A;;GX;;;WD)");
            NetshHelper.RunExpectZero($"http add urlacl url=https://[::1]:{port}/     sddl=D:(A;;GX;;;WD)");
            NetshHelper.RunExpectZero($"http add sslcert ipport=127.0.0.1:{port} certhash={tp} certstorename=MY appid=\"{appid}\"");
            NetshHelper.RunExpectZero($"http add sslcert ipport=[::1]:{port}     certhash={tp} certstorename=MY appid=\"{appid}\"");

            Log("http.sys bound to leaf");
        }

        // ── 스토어 헬퍼 ──────────────────────────────────────────────────────
        static void RemoveCertsByCn(string cn, StoreName name, StoreLocation loc, Action<string> Log)
        {
            var st = new X509Store(name, loc);
            st.Open(OpenFlags.ReadWrite);
            var hits = st.Certificates.OfType<X509Certificate2>().Where(c => c.Subject == $"CN={cn}").ToList();
            foreach (var c in hits)
            {
                try { st.Remove(c); Log($"removed {cn} from {name}/{loc}: {c.Thumbprint}"); } catch { }
            }
        }

        static System.Collections.Generic.IEnumerable<X509Certificate2> Find(StoreName name, StoreLocation loc, string cn)
        {
            var st = new X509Store(name, loc);
            st.Open(OpenFlags.ReadOnly);
            var list = st.Certificates.OfType<X509Certificate2>().Where(c => c.Subject == $"CN={cn}").ToArray();
            return list;
        }
    }
}

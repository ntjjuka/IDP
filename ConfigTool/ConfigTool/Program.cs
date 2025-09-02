using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IDPSsoAgent.Tools
{
    internal static class PortableCrypto
    {
        // ⚠️ 빌드마다 바꾸고, 난독화/분할 상수 등으로 지연 방어 권장
        private static byte[] Key()
        {
            var pass = "IDPSsoAgent#K3y-2025";
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(pass));
        }

        public static string Encrypt(string plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Key();
                aes.GenerateIV();

                var pt = Encoding.UTF8.GetBytes(plain);
                byte[] ct;
                using (var enc = aes.CreateEncryptor())
                    ct = enc.TransformFinalBlock(pt, 0, pt.Length);

                // 무결성(HMAC-SHA256)
                byte[] tag;
                using (var h = new HMACSHA256(Key()))
                    tag = h.ComputeHash(Concat(aes.IV, ct));

                return "aespb:" + Convert.ToBase64String(Concat(aes.IV, ct, tag));
            }
        }

        public static string Decrypt(string token)
        {
            if (!token.StartsWith("aespb:", StringComparison.OrdinalIgnoreCase))
                return token;

            var blob = Convert.FromBase64String(token.Substring(6));
            var iv = Sub(blob, 0, 16);
            var tag = Sub(blob, blob.Length - 32, 32);
            var ct = Sub(blob, 16, blob.Length - 16 - 32);

            byte[] expect;
            using (var h = new HMACSHA256(Key()))
                expect = h.ComputeHash(Concat(iv, ct));

            if (!FixedTimeEquals(tag, expect))
                throw new CryptographicException("Integrity check failed");

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.Key = Key(); aes.IV = iv;

                using (var dec = aes.CreateDecryptor())
                {
                    var pt = dec.TransformFinalBlock(ct, 0, ct.Length);
                    return Encoding.UTF8.GetString(pt);
                }
            }
        }

        private static byte[] Concat(params byte[][] xs)
        {
            using (var ms = new MemoryStream())
            {
                foreach (var x in xs) ms.Write(x, 0, x.Length);
                return ms.ToArray();
            }
        }
        private static byte[] Sub(byte[] src, int ofs, int len)
        {
            var dst = new byte[len];
            Buffer.BlockCopy(src, ofs, dst, 0, len);
            return dst;
        }
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    internal static class DpapiHelper
    {
        // AppConfig.cs의 DpapiHelper와 동일해야 함
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IDPSsoAgent-v1");

        public static string ProtectToString(string plain, bool machineScope)
        {
            var scope = machineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
            var bytes = Encoding.UTF8.GetBytes(plain);
            var cipher = ProtectedData.Protect(bytes, Entropy, scope);
            return "dpapi:" + Convert.ToBase64String(cipher);
        }
    }

    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2 || !args[0].Equals("enc", StringComparison.OrdinalIgnoreCase))
                return Help();

            var mode = args[1].ToLowerInvariant(); // "aespb" or "dpapi"
            bool machineScope = true; // dpapi 기본 LocalMachine
            bool useStdin = false;
            string input = null;

            for (int i = 2; i < args.Length; i++)
            {
                var a = args[i];
                if (a.Equals("--stdin", StringComparison.OrdinalIgnoreCase)) { useStdin = true; continue; }
                if (a.Equals("--scope", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    machineScope = args[++i].Equals("machine", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (input == null) input = a;
            }

            if (useStdin)
                input = Console.In.ReadToEnd().TrimEnd('\r', '\n');

            if (string.IsNullOrEmpty(input))
                return Help("no input provided");

            try
            {
                string outToken;
                switch (mode)
                {
                    case "aespb":
                        outToken = PortableCrypto.Encrypt(input);
                        break;
                    case "dpapi":
                        outToken = DpapiHelper.ProtectToString(input, machineScope);
                        break;
                    default:
                        return Help("mode must be 'aespb' or 'dpapi'");
                }

                Console.WriteLine(outToken);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        static int Help(string msg = null)
        {
            if (!string.IsNullOrEmpty(msg))
                Console.Error.WriteLine("ERROR: " + msg);

            Console.WriteLine("Usage:");
            Console.WriteLine("  ConfigTool enc aespb  <plain>          # 포터블 암호문 생성");
            Console.WriteLine("  ConfigTool enc aespb  --stdin          # 표준입력에서 읽기");
            Console.WriteLine("  ConfigTool enc dpapi  <plain> [--scope machine|user]");
            Console.WriteLine("  ConfigTool enc dpapi  --stdin [--scope machine|user]");
            Console.WriteLine();
            Console.WriteLine("Tips:");
            Console.WriteLine("  - dpapi는 대상 PC에서 생성해야 복호화됨(LocalMachine 기본)");
            Console.WriteLine("  - aespb는 다른 PC로 복사해도 동작(보안은 제한적)");
            return 1;
        }
    }
}

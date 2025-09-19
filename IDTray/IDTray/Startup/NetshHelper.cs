using System;
using System.Diagnostics;

namespace IDTray
{
    internal static class NetshHelper
    {
        public static string Run(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var p = System.Diagnostics.Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(e))
                    Program.SafeLog($"netsh [{args}] err: {e.Trim()}");
                return o + e;
            }
            catch (Exception ex)
            {
                Program.SafeLog($"netsh [{args}] failed: {ex.Message}");
                return "";
            }
        }

        public static void ResetAllForPort(int port)
        {
            // URLACL
            Run($"http delete urlacl url=https://127.0.0.1:{port}/");
            Run($"http delete urlacl url=https://[::1]:{port}/");
            Run($"http delete urlacl url=https://+:{port}/");      
            Run($"http delete urlacl url=http://127.0.0.1:{port}/");
            Run($"http delete urlacl url=http://[::1]:{port}/");
            Run($"http delete urlacl url=http://+:{port}/");       

            // SSLCERT
            Run($"http delete sslcert ipport=127.0.0.1:{port}");
            Run($"http delete sslcert ipport=[::1]:{port}");
            Run($"http delete sslcert ipport=0.0.0.0:{port}");     
            Run($"http delete sslcert ipport=[::]:{port}");        
        }

        public static (string output, string error, int code) RunRaw(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (o, e, p.ExitCode);
        }

        public static void RunExpectZero(string args)
        {
            var (o, e, code) = RunRaw(args);
            if (!string.IsNullOrEmpty(e)) Program.SafeLog($"netsh [{args}] err: {e.Trim()}");
            if (code != 0)
                throw new ApplicationException($"netsh failed (code {code}): {args}\n{e}\n{o}");
        }
    }
}

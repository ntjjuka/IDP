using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IDPSsoAgentV2
{
    internal static class Program
    {
        // ===== 설정 =====
        public const int PORT = 5400;

        // ProgramData: C:\ProgramData\TrayHttpsAgent
        public static readonly string PD = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TrayHttpsAgent");

        public static readonly string SETUP_LOG = Path.Combine(PD, "setup.log");
        public static readonly string SETUP_OK = Path.Combine(PD, "setup.ok");

        // 싱글 인스턴스
        private static Mutex _mutex;
        private static bool _hasMutex;

        // 에이전트용 디렉터리: C:\Users\<User>\AppData\Local\TrayHttpsAgent
        public static readonly string AGENT_DIR = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "TrayHttpsAgent");
        public static readonly string AGENT_LOG = Path.Combine(AGENT_DIR, "agent.log");

        // 에이전트용 로거
        public static void AgentLog(string msg)
        {
            try
            {
                Directory.CreateDirectory(AGENT_DIR);
                File.AppendAllText(AGENT_LOG, $"[{DateTimeOffset.Now:O}] {msg}\r\n");
            }
            catch { }
        }
        [STAThread]
        static void Main(string[] args)
        {
            Directory.CreateDirectory(PD);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => {
                AgentLog("ThreadException: " + e.Exception);
                MessageBox.Show("예상치 못한 오류가 발생했습니다.\n" + e.Exception.Message, "IDP Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                AgentLog("UnhandledException: " + e.ExceptionObject);
            };
            TaskScheduler.UnobservedTaskException += (s, e) => {
                AgentLog("UnobservedTaskException: " + e.Exception);
                e.SetObserved();
            };
            // 0) 유지보수 모드 (뮤텍스 X)
            if (args.Length >= 1 && args[0].Equals("--setup-https", StringComparison.OrdinalIgnoreCase))
            {
                int port = (args.Length >= 2 && int.TryParse(args[1], out var p)) ? p : PORT;
                try
                {
                    SafeLog($"setup invoked (port={port})");
                    HttpsSetup.EnsureAll(port, SETUP_LOG);
                    // 센티널
                    File.WriteAllText(SETUP_OK, DateTimeOffset.Now.ToString("O"));
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    SafeLog("setup failed(top): " + ex);
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 1 && args[0].Equals("--reset-https", StringComparison.OrdinalIgnoreCase))
            {
                int port = (args.Length >= 2 && int.TryParse(args[1], out var p)) ? p : PORT;
                try
                {
                    SafeLog($"reset invoked (port={port})");
                    HttpsSetup.FullWipe(port, SETUP_LOG);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    SafeLog("reset failed(top): " + ex);
                    Environment.Exit(1);
                }
                return;
            }

            // 1) 정상 실행 경로 → 싱글 인스턴스
            _mutex = new Mutex(false, "Global\\IDPAgentV2-Mutex");
            _hasMutex = _mutex.WaitOne(0, false);
            if (!_hasMutex)
            {
                MessageBox.Show("이미 실행 중입니다.", "IDP Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Environment.Exit(2);
                return;
            }
            Application.ApplicationExit += (s, e) => { try { if (_hasMutex) _mutex.ReleaseMutex(); } catch { } };

            // 2) 초기 설정 필요 시 권한 상승 자식 실행
            if (!IsConfiguredOk(PORT))
            {
                if (!IsElevated())
                {
                    try
                    {
                        // 센티널 제거
                        try { if (File.Exists(SETUP_OK)) File.Delete(SETUP_OK); } catch { }

                        string exe = GetSelfExePath();
                        var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;

                        var psi = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = $"--setup-https {PORT}",
                            Verb = "runas",
                            UseShellExecute = true,
                            WorkingDirectory = workDir,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        SafeLog($"Elevate prepared: EXE={exe}, WD={workDir}, ARGS={psi.Arguments}");

                        var p = Process.Start(psi);
                        if (p != null)
                        {
                            p.WaitForExit();
                            SafeLog($"Elevate child exited: {p.ExitCode}");
                            if (p.ExitCode == 1223)
                            {
                                MessageBox.Show("관리자 권한 승인이 취소되었습니다.", "IDP Agent",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                Environment.Exit(1);
                                return;
                            }
                        }
                        else
                        {
                            SafeLog("Process.Start returned null (shell verb)");
                        }

                        // 폴링: 센티널 + 구성점검
                        if (!WaitForFile(SETUP_OK, 120_000) || !WaitUntilConfiguredOk(PORT, 120_000))
                        {
                            MessageBox.Show("HTTPS 초기 설정이 완료되지 않았습니다. setup.log를 확인하세요.",
                                "IDP Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Environment.Exit(1);
                            return;
                        }
                    }
                    catch (System.ComponentModel.Win32Exception wex)
                    {
                        SafeLog($"Elevate Win32Exception {wex.NativeErrorCode}: {wex.Message}");
                        MessageBox.Show($"권한 상승 실행 실패 (코드 {wex.NativeErrorCode}). setup.log를 확인하세요.",
                            "IDP Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Environment.Exit(1);
                        return;
                    }
                    catch (Exception ex)
                    {
                        SafeLog("Elevate general exception: " + ex);
                        MessageBox.Show("권한 상승 실행 중 알 수 없는 오류가 발생했습니다.",
                            "IDP Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Environment.Exit(1);
                        return;
                    }
                }
                else
                {
                    // 이미 관리자면 직접 수행
                    try
                    {
                        HttpsSetup.EnsureAll(PORT, SETUP_LOG);
                        File.WriteAllText(SETUP_OK, DateTimeOffset.Now.ToString("O"));
                        if (!IsConfiguredOk(PORT))
                        {
                            MessageBox.Show("HTTPS 초기 설정이 완료되지 않았습니다. setup.log를 확인하세요.",
                                "IDP Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Environment.Exit(1);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        SafeLog("setup failed(elevated): " + ex);
                        MessageBox.Show("HTTPS 초기 설정 중 오류가 발생했습니다. setup.log를 확인하세요.",
                            "IDP Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Environment.Exit(1);
                        return;
                    }
                }
            }

            // 3) 트레이/서버 실행
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayForm(PORT)); // 당신이 이미 가진 트레이 폼
        }

        // ===== helpers =====

        public static void SafeLog(string msg)
        {
            try
            {
                Directory.CreateDirectory(PD);
                File.AppendAllText(SETUP_LOG, $"[{DateTimeOffset.Now:O}] {msg}\r\n");
            }
            catch { MessageBox.Show("SafeLog Error.", "SafeLog Error.", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static string GetSelfExePath()
        {
            return (Environment.GetEnvironmentVariable("PROCESS_PATH")
                    ?? Application.ExecutablePath
                    ?? Process.GetCurrentProcess().MainModule?.FileName) ?? "";
        }

        public static bool IsElevated()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                var p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception e) {
                SafeLog($"IsConfiguredOk Fail : {e.Message}");
                return false; }
        }

        public static bool IsConfiguredOk(int port)
        {
            try
            {
                var rootOk = HttpsSetup.IsRootTrusted(HttpsSetup.ROOT_CN);
                var bindOk = HttpsSetup.IsBindingOk(port);
                var crlOk = File.Exists(Path.Combine(PD, "crl", "root.crl"));
                SafeLog($"rootOk : {rootOk}");
                SafeLog($"bindOk : {bindOk}");
                SafeLog($"crlOk : {crlOk}");
                return rootOk && bindOk && crlOk;
            }
            catch (Exception e) { SafeLog($"IsConfiguredOk Fail : {e.Message}");  return false; }
        }

        public static bool WaitForFile(string path, int timeoutMs)
        {
            SafeLog($"WaitForFile 체크 : {path}");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (File.Exists(path)) return true;
                Thread.Sleep(300);
            }
            return false;
        }

        public static bool WaitUntilConfiguredOk(int port, int timeoutMs)
        {
            SafeLog($"WaitUntilConfiguredOk 체크 : {port}");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (IsConfiguredOk(port)) return true;
                Thread.Sleep(500);
            }
            SafeLog($"WaitUntilConfiguredOk Fail : false");
            return false;
        }
    }
}

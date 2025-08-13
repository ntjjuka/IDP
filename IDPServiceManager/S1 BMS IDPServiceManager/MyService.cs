using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;

namespace S1_BMS_IDPServiceManager
{
    public partial class MyService : ServiceBase
    {
        private Process _keycloakProcess;
        private volatile bool _isStopping;
        private string _httpPort;

        public MyService()
        {
            this.ServiceName = "KeycloakService";
        }

        protected override void OnStart(string[] args)
        {
            Log("서비스 시작됨.");
            _isStopping = false;
            ThreadPool.QueueUserWorkItem(_ => Run());
        }

        private void Run()
        {
            try
            {
                EnvLoader.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"));

                string keycloakHomeRaw = EnvLoader.Get("KEYCLOAK_HOME", @"C:\keycloak");
                string keycloakHome = Path.IsPathRooted(keycloakHomeRaw)
                    ? keycloakHomeRaw
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, keycloakHomeRaw);
                string javaHome = EnvLoader.Get("JAVA_HOME", "");

                string libDir = Path.Combine(keycloakHome, "lib");
                string confDir = Path.Combine(keycloakHome, "conf");
                string themeDir = Path.Combine(keycloakHome, "themes");
                string jarPath = Path.Combine(libDir, "quarkus-run.jar");
                string javaExe = Path.Combine(javaHome, "bin", "java.exe");

                if (!File.Exists(javaExe) || !File.Exists(jarPath))
                {
                    Log("Java 실행 파일 또는 Keycloak jar가 존재하지 않습니다.");
                    return;
                }

                var iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "idp-args.ini");
                if (!File.Exists(iniPath))
                {
                    Log(".ini 파일이 없습니다: " + iniPath);
                    return;
                }

                var lines = File.ReadAllLines(iniPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                    .Select(l => l.Trim())
                    .ToArray();

                _httpPort = ExtractHttpPort(lines);
                if (!string.IsNullOrEmpty(_httpPort))
                    KillProcessOnPort(_httpPort);

                var buildCommand = lines.FirstOrDefault(l => l.StartsWith("build", StringComparison.OrdinalIgnoreCase));
                var startCommand = lines.FirstOrDefault(l => l.StartsWith("start", StringComparison.OrdinalIgnoreCase) || l.StartsWith("start-dev", StringComparison.OrdinalIgnoreCase));
                var otherArgs = lines.Where(l => !l.StartsWith("build") && !l.StartsWith("start")).ToArray();

                var javaOpts = string.Join(" ",
                    "-XX:MetaspaceSize=96M",
                    "-XX:MaxMetaspaceSize=256m",
                    "-Dfile.encoding=UTF-8",
                    "-Dsun.stdout.encoding=UTF-8",
                    "-Dsun.err.encoding=UTF-8",
                    "-XX:+ExitOnOutOfMemoryError",
                    "-Djava.security.egd=file:/dev/urandom",
                    "-XX:+UseG1GC",
                    "-Xms64m",
                    "-Xmx512m",
                    "--add-opens=java.base/java.util=ALL-UNNAMED",
                    "--add-opens=java.base/java.util.concurrent=ALL-UNNAMED",
                    "--add-opens=java.base/java.security=ALL-UNNAMED",
                    "-Duser.language=en",
                    "-Duser.country=US",
                    "-Dprogram.name=kc.bat",
                    $"-Dkc.home.dir=\"{keycloakHome.Replace("\\", "/")}\"",
                    $"-Djboss.server.config.dir=\"{confDir}\"",
                    $"-Dblocked-ips-file=\"{confDir}\\blocked-ips.conf\"",
                    $"-Dkeycloak.theme.dir=\"{themeDir}\"",
                    "-Djava.util.concurrent.ForkJoinPool.common.threadFactory=io.quarkus.bootstrap.forkjoin.QuarkusForkJoinWorkerThreadFactory"
                );

                var buildLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs/build.done");
                if (!File.Exists(buildLogPath) && !string.IsNullOrEmpty(buildCommand))
                {
                    Log("Keycloak 최초 빌드 시작...");
                    RunJavaProcessAndWait(javaExe, jarPath, javaOpts, buildCommand);
                    File.WriteAllText(buildLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                if (!string.IsNullOrEmpty(startCommand))
                {
                    Log("Keycloak 실행 시작...");
                    Log($"javaExe: {javaExe}");
                    Log($"jarPath: {jarPath}");
                    Log($"javaOpts: {javaOpts}");
                    Log($"startCommand: {startCommand}");
                    Log($"otherArgs: {string.Join(" ", otherArgs)}");
                    RunJavaProcess(javaExe, jarPath, javaOpts, $"{startCommand} {string.Join(" ", otherArgs)}");

                    while (!_isStopping)
                    {
                        if (_keycloakProcess != null && _keycloakProcess.HasExited)
                        {
                            Log("Keycloak 프로세스가 예기치 않게 종료되었습니다.");
                            break;
                        }

                        Thread.Sleep(1000);
                    }

                    Log("Run() 종료됨.");
                }
            }
            catch (Exception ex)
            {
                Log($"[오류] {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void RunJavaProcessAndWait(string javaExe, string jarPath, string javaOpts, string arguments)
        {
            var psi = new ProcessStartInfo

            {
                FileName = javaExe,
                Arguments = $"{javaOpts} -cp \"{jarPath}\" io.quarkus.bootstrap.runner.QuarkusEntryPoint {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                if (proc == null)
                {
                    Log("Keycloak 프로세스 실행 실패");
                    return;
                }

                proc.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("[ERR] " + e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                Log("Keycloak 프로세스가 실행되었습니다.");

                proc.WaitForExit();
                Log("Keycloak 프로세스가 종료되었습니다.");
            }
        }

        private void RunJavaProcess(string javaExe, string jarPath, string javaOpts, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = $"{javaOpts} -cp \"{jarPath}\" io.quarkus.bootstrap.runner.QuarkusEntryPoint {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _keycloakProcess = Process.Start(psi);
            if (_keycloakProcess == null)
            {
                Log("Keycloak 프로세스 실행 실패");
                return;
            }

            _keycloakProcess.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
            _keycloakProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("[ERR] " + e.Data); };
            _keycloakProcess.BeginOutputReadLine();
            _keycloakProcess.BeginErrorReadLine();

            Log("Keycloak 프로세스가 실행되었습니다.");
        }

        protected override void OnStop()
        {
            Log("서비스 종료됨. Keycloak 프로세스를 종료합니다.");
            _isStopping = true;

            try
            {
                if (_keycloakProcess != null && !_keycloakProcess.HasExited)
                {
                    Log($"Keycloak 프로세스(PID: {_keycloakProcess.Id}) 강제 종료 시도");
                    _keycloakProcess.Kill();
                    _keycloakProcess.WaitForExit(5000);
                    _keycloakProcess.Dispose();
                    Log("Keycloak 프로세스 종료 완료.");
                }
            }
            catch (Exception ex)
            {
                Log("Keycloak 종료 중 오류: " + ex.Message);
            }

            if (!string.IsNullOrEmpty(_httpPort))
            {
                KillProcessOnPort(_httpPort);
                Log($"포트({_httpPort}) 점유 프로세스도 종료했습니다.");
            }

            Log("서비스가 완전히 종료되었습니다.");
        }

        private static void KillProcessOnPort(string port)
        {
            try
            {
                var procStart = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-aon",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(procStart))
                {
                    var output = proc?.StandardOutput.ReadToEnd();
                    var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                    foreach (var line in lines ?? Array.Empty<string>())
                    {
                        if (line.Contains($":{port}"))
                        {
                            var parts = Regex.Split(line, @"\\s+");
                            if (parts.Length >= 5 && int.TryParse(parts[4], out int pid))
                            {
                                Log($"포트 {port} 점유 중인 프로세스 종료: PID={pid}");
                                Process.GetProcessById(pid).Kill();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[포트 종료 오류] {ex.Message}");
            }
        }

        private static string ExtractHttpPort(string[] args)
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith("--http-port="))
                {
                    var match = Regex.Match(arg, @"--http-port=(\\d+)");
                    return match.Success ? match.Groups[1].Value : "";
                }
            }
            return "";
        }

        private static void Log(string message)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string date = DateTime.Now.ToString("yyyy-MM-dd");
                string logFilePath = Path.Combine(logDir, $"idp-worker-{date}.log");

                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                Console.WriteLine(log);

                File.AppendAllText(logFilePath, log + Environment.NewLine);
            }
            catch
            {
                // 로그 작성 실패는 무시
            }
        }
    }
}

using IDPSsoAgent.Infra;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace IDPSsoAgent.Startup
{
    public static class SystemSetup
    {
        public static bool EnsureUrlAcl(int port)
        {
            try
            {
                string user = Environment.UserDomainName + "\\" + Environment.UserName;

                // 이미 등록돼 있어도 /add 시 실패할 수 있으니, 일단 시도하고 실패하면 무시
                bool ok1 = RunNetsh($"http add urlacl url=http://127.0.0.1:{port}/ user=\"{user}\"");
                bool ok2 = RunNetsh($"http add urlacl url=http://localhost:{port}/ user=\"{user}\"");

                return ok1 || ok2; // 둘 중 하나라도 성공하면 OK로 간주
            }
            catch (Exception ex)
            {
                Logger.Error("EnsureUrlAcl failed: " + ex);
                return false;
            }
        }

        private static bool RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);

                    Logger.Info($"netsh {args} -> exit={p.ExitCode}, out={o}, err={e}");
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RunNetsh failed: " + ex);
                return false;
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                var wi = WindowsIdentity.GetCurrent();
                var wp = new WindowsPrincipal(wi);
                return wp.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string GetExePath()
        {
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\\IDPSsoAgent.exe"; }
        }

        public static string GetExeDir()
        {
            try { return Path.GetDirectoryName(GetExePath()); }
            catch { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static bool RegisterAutostartTaskXml(string taskName)
        {
            try
            {
                string exe = GetExePath();
                string dir = GetExeDir();

                // XML 템플릿을 임시파일로 저장
                string xml = BuildTaskXml(exe, dir);
                string tmp = Path.Combine(Path.GetTempPath(), "idp_autostart.xml");
                File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);

                var psi = new ProcessStartInfo("schtasks.exe",
                    "/Create /TN \"" + taskName + "\" /XML \"" + tmp + "\" /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(5000);
                    Logger.Info("RegisterAutostartTaskXml exit=" + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RegisterAutostartTaskXml failed: " + ex);
                return false;
            }
        }

        public static bool UnregisterAutostartTask(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe",
                    "/Delete /TN \"" + taskName + "\" /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(5000);
                    Logger.Info("UnregisterAutostartTask exit=" + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("UnregisterAutostartTask failed: " + ex);
                return false;
            }
        }

        private static string BuildTaskXml(string exePath, string workDir)
        {
            // UTF-16(XML 헤더 그대로)로 저장 예정
            return
            @"<?xml version=""1.0"" encoding=""UTF-16""?>
            <Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
                <RegistrationInfo>
                <Description>IDP SSO Agent Autostart</Description>
                </RegistrationInfo>
                <Triggers>
                <LogonTrigger>
                    <Enabled>true</Enabled>
                    <Delay>PT5S</Delay>
                </LogonTrigger>
                </Triggers>
                <Principals>
                <Principal id=""Author"">
                    <LogonType>InteractiveToken</LogonType>
                    <RunLevel>HighestAvailable</RunLevel>
                </Principal>
                </Principals>
                <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                    <StopOnIdleEnd>false</StopOnIdleEnd>
                    <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                </Settings>
                <Actions Context=""Author"">
                <Exec>
                    <Command>" + SecurityElement.Escape(exePath) + @"</Command>
                    <WorkingDirectory>" + SecurityElement.Escape(workDir) + @"</WorkingDirectory>
                </Exec>
                </Actions>
            </Task>";
        }



        // 첫 실행 때 사용할, 관리자 승격 URLACL 등록
        public static bool EnsureUrlAclElevated(int port)
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string u1 = "http add urlacl url=http://127.0.0.1:" + port + "/ user=\"" + user + "\"";
            string u2 = "http add urlacl url=http://localhost:" + port + "/ user=\"" + user + "\"";

            bool ok1 = RunNetshAdmin(u1);
            bool ok2 = RunNetshAdmin(u2);
            return ok1 || ok2;
        }

        // 예전 Run 키/작업 이름들 정리 (중복 자동실행 방지)
        public static void CleanupLegacyAutostarts()
        {
            try
            {
                // 1) Run 키 정리 (이전 이름들 가능성)
                RemoveRunValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "KeycloakSSOAgent");
                RemoveRunValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "IDPSsoAgent");
            }
            catch (Exception ex)
            {
                Logger.Error("Cleanup Run key failed: " + ex);
            }

            try
            {
                // 2) 과거 작업 스케줄러 이름들 제거 (있다면)
                TryDeleteTask("KeycloakSSOAgent_Autostart");
                // 우리 표준 이름은 "IDPSsoAgent_Autostart" 하나만 사용할 예정
                // 필요시 여기에 더 추가
            }
            catch (Exception ex)
            {
                Logger.Error("Cleanup scheduled task failed: " + ex);
            }
        }

        private static void RemoveRunValue(string keyPath, string name)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
                {
                    if (key != null && key.GetValue(name) != null)
                    {
                        key.DeleteValue(name);
                        Logger.Info("Removed Run key: " + name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RemoveRunValue failed: " + ex);
            }
        }

        private static void TryDeleteTask(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", "/Delete /TN \"" + taskName + "\" /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.WaitForExit(3000);
                        Logger.Info("TryDeleteTask " + taskName + " exit=" + p.ExitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("TryDeleteTask failed: " + ex);
            }
        }

        // 관리자 권한으로 netsh 실행 (URLACL)
        private static bool RunNetshAdmin(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit();
                    Logger.Info("netsh (elevated) " + args + " -> exit=" + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RunNetshAdmin failed: " + ex);
                return false;
            }
        }


    }
}



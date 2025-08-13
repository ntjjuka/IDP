using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace IDPSsoAgent.Startup
{
    public enum AutostartMode { TaskScheduler = 0, RegistryRun = 1 }

    public static class AutoStart
    {
        // 정규화된 작업 이름(루트 폴더)
        private const string TaskName = @"\IDPSsoAgent_Autostart";

        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "IDPSsoAgent";
        private const string ModeRegKey = @"Software\IDPSsoAgent";
        private const string ModeRegValue = "AutostartMode";

        public static AutostartMode Mode
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(ModeRegKey))
                    {
                        var v = k?.GetValue(ModeRegValue);
                        if (v == null) return AutostartMode.TaskScheduler;
                        int i; if (int.TryParse(v.ToString(), out i) && Enum.IsDefined(typeof(AutostartMode), i))
                            return (AutostartMode)i;
                    }
                }
                catch { }
                return AutostartMode.TaskScheduler;
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(ModeRegKey))
                        k.SetValue(ModeRegValue, (int)value, RegistryValueKind.DWord);
                }
                catch { }
            }
        }

        public static bool IsEnabled()
            => Mode == AutostartMode.TaskScheduler ? IsTaskPresentAndEnabled() : IsRunValuePresent();

        public static bool Enable()
            => Mode == AutostartMode.TaskScheduler ? EnsureTaskEnabled() : EnsureRunValue();

        public static bool Disable()
            => Mode == AutostartMode.TaskScheduler ? RemoveTask() : RemoveRunValue();

        public static bool UseTaskSchedulerMode() { Disable(); Mode = AutostartMode.TaskScheduler; return true; }
        public static bool UseRegistryRunMode() { Disable(); Mode = AutostartMode.RegistryRun; return true; }

        // ---------- Task Scheduler ----------

        private static bool EnsureTaskEnabled()
        {
            if (!IsTaskPresent())
                if (!RegisterTask()) return false;
            return EnableTask();
        }

        private static bool RegisterTask()
        {
            try
            {
                string exe = SystemSetup.GetExePath();
                string dir = SystemSetup.GetExeDir();
                string xml = BuildTaskXml(exe, dir);
                string tmp = Path.Combine(Path.GetTempPath(), "idp_autostart.xml");
                File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);

                return RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
            }
            catch { return false; }
        }

        private static bool EnableTask()
            => RunSchTasks($"/Change /TN \"{TaskName}\" /ENABLE");

        private static bool RemoveTask()
        {
            // 존재하지 않아도 성공으로 간주(아이덤포턴트)
            if (!IsTaskPresent()) return true;
            var ok = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
            return ok || !IsTaskPresent();
        }

        private static bool IsTaskPresent()
            => RunSchTasks($"/Query /TN \"{TaskName}\"");

        private static bool IsTaskPresentAndEnabled()
        {
            string output;
            if (!TryRunWithOutput($"/Query /TN \"{TaskName}\" /V /FO LIST", out output))
                return false;

            // 영어: Scheduled Task State: Enabled/Disabled
            var line = ExtractLine(output, "Scheduled Task State:");
            if (!string.IsNullOrEmpty(line))
            {
                if (line.IndexOf("Enabled", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (line.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }

            // 한글: 예약된 작업 상태: 사용 / 사용 안 함
            line = ExtractLine(output, "예약된 작업 상태:");
            if (!string.IsNullOrEmpty(line))
            {
                var val = line.Replace(" ", "");
                if (val.IndexOf("사용안함", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (val.IndexOf("사용", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool RunSchTasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(8000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private static bool TryRunWithOutput(string args, out string output)
        {
            output = null;
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
                }
            }
            catch { return false; }
        }

        // "키:" 라인의 값만 추출
        private static string ExtractLine(string all, string keyWithColon)
        {
            var lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.IndexOf(keyWithColon, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0 && idx + 1 < line.Length) return line.Substring(idx + 1).Trim();
                    return line.Trim();
                }
            }
            return null;
        }

        private static string BuildTaskXml(string exePath, string workDir)
        {
            return
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>IDP SSO Agent Autostart</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT5S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <Enabled>true</Enabled><Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>" + System.Security.SecurityElement.Escape(exePath) + @"</Command>
      <WorkingDirectory>" + System.Security.SecurityElement.Escape(workDir) + @"</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        // ---------- Registry Run ----------

        private static bool EnsureRunValue()
        {
            try
            {
                string exe = SystemSetup.GetExePath();
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    key.SetValue(RunValueName, "\"" + exe + "\"", RegistryValueKind.String);
                    return true;
                }
            }
            catch { return false; }
        }

        private static bool RemoveRunValue()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return true;
                    if (key.GetValue(RunValueName) != null) key.DeleteValue(RunValueName);
                    return true;
                }
            }
            catch { return false; }
        }

        private static bool IsRunValuePresent()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }
    }
}

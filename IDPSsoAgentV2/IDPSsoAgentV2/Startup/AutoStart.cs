using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace IDPSsoAgentV2
{
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "IDP Agent";

        /// <summary>현재 사용자 로그인 시 자동 시작 활성화</summary>
        public static void EnableForCurrentUser(string extraArgs = "--autostart")
        {
            try
            {
                string exe = ProgramPath();
                string args = string.IsNullOrWhiteSpace(extraArgs) ? "" : " " + extraArgs;
                string cmd = $"\"{exe}\"{args}";

                var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                              ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key.SetValue(ValueName, cmd, RegistryValueKind.String);

                Program.AgentLog($"AutoStart enabled (HKCU Run): {cmd}");
            }
            catch (Exception ex)
            {
                Program.AgentLog("AutoStart enable failed: " + ex);
            }
        }

        /// <summary>현재 사용자 자동 시작 비활성화</summary>
        public static void DisableForCurrentUser()
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null) return;
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Program.AgentLog("AutoStart disabled (HKCU Run).");
            }
            catch (Exception ex)
            {
                Program.AgentLog("AutoStart disable failed: " + ex);
            }
        }

        public static bool IsEnabledForCurrentUser()
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (key == null) return false;
                var v = key.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(v);
            }
            catch { return false; }
        }

        private static string ProgramPath()
        {
            return (System.Windows.Forms.Application.ExecutablePath
                    ?? Process.GetCurrentProcess().MainModule?.FileName) ?? "";
        }
    }
}

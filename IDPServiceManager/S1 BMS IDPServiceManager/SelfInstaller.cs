using System;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Reflection;


namespace S1_BMS_IDPServiceManager
{

    public static class SelfInstaller
    {
        static readonly string exePath = Assembly.GetExecutingAssembly().Location;
        static readonly string serviceName = "KeycloakService"; // 여기에 서비스 이름을 지정하세요

        public static void InstallMe()
        {
            if (!File.Exists(exePath))
            {
                Console.WriteLine($"[ERROR] 실행 파일이 존재하지 않습니다: {exePath}");
                return;
            }

            try
            {
                Console.WriteLine("[INFO] InstallHelper를 사용해 서비스 설치를 시작합니다.");
                ManagedInstallerClass.InstallHelper(new string[] { exePath });
                Console.WriteLine("[SUCCESS] 서비스가 성공적으로 설치되었습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 서비스 설치 중 오류 발생: {ex.Message}");
            }
        }

        public static void UninstallMe()
        {
            if (!File.Exists(exePath))
            {
                Console.WriteLine($"[WARN] exe가 존재하지 않습니다: {exePath}");
                Console.WriteLine("[INFO] sc.exe를 사용해 강제로 서비스 삭제를 시도합니다.");
                ForceDeleteService(serviceName);
                return;
            }

            try
            {
                Console.WriteLine("[INFO] InstallHelper를 사용해 서비스 제거를 시작합니다.");
                ManagedInstallerClass.InstallHelper(new string[] { "/u", exePath });
                Console.WriteLine("[SUCCESS] 서비스가 성공적으로 제거되었습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] InstallHelper로 제거 중 오류 발생: {ex.Message}");
                Console.WriteLine("[INFO] sc.exe를 사용해 강제로 서비스 삭제를 시도합니다.");
                ForceDeleteService(serviceName);
            }
        }

        private static void ForceDeleteService(string name)
        {
            try
            {
                Process sc = new Process();
                sc.StartInfo.FileName = "sc.exe";
                sc.StartInfo.Arguments = $"delete \"{name}\"";
                sc.StartInfo.RedirectStandardOutput = true;
                sc.StartInfo.RedirectStandardError = true;
                sc.StartInfo.UseShellExecute = false;
                sc.StartInfo.CreateNoWindow = true;
                sc.Start();

                string output = sc.StandardOutput.ReadToEnd();
                string error = sc.StandardError.ReadToEnd();
                sc.WaitForExit();

                if (sc.ExitCode == 0)
                {
                    Console.WriteLine($"[SUCCESS] sc.exe로 서비스가 삭제되었습니다: {name}");
                }
                else
                {
                    Console.WriteLine($"[ERROR] sc.exe로 서비스 삭제 실패: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] sc.exe 실행 중 오류 발생: {ex.Message}");
            }
        }
    }
}

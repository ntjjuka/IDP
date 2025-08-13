using IDPSsoAgent.Config;
using IDPSsoAgent.Infra;
using IDPSsoAgent.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace IDPSsoAgent.Startup
{
    internal static class Program
    {
        private static readonly string SetupMarkerDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IDPSsoAgent");
        private static readonly string SetupMarkerFile = Path.Combine(SetupMarkerDir, "setup.done");

        [STAThread]
        static void Main()
        {
            try
            {
                // 0) 워킹 디렉터리 고정 (스케줄러/서비스 시작 대비)
                try
                {
                    var exe = Process.GetCurrentProcess().MainModule.FileName;
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir)) Directory.SetCurrentDirectory(dir);
                }
                catch { /* ignore */ }

                //특수 인자 먼저 처리 (관리자 권한으로 작업스케줄러 실행시)
                var args = Environment.GetCommandLineArgs();
                bool elevatedEnable = Array.Exists(args, a => string.Equals(a, "--elevated-enable-autostart", StringComparison.OrdinalIgnoreCase));
                bool elevatedDisable = Array.Exists(args, a => string.Equals(a, "--elevated-disable-autostart", StringComparison.OrdinalIgnoreCase));

                if (elevatedEnable || elevatedDisable)
                {
                    bool ok = false;
                    try
                    {
                        // 작업 스케줄러 기준으로만 동작하게 고정(필요시 현 Mode 유지로 변경 가능)
                        AutoStart.UseTaskSchedulerMode();
                        ok = elevatedEnable ? AutoStart.Enable()
                                            : AutoStart.Disable();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Elevated autostart action failed: " + ex);
                        ok = false;
                    }

                    MessageBox.Show(
                        ok
                            ? (elevatedEnable ? "관리자 권한으로 자동 시작 등록을 완료했습니다."
                                              : "관리자 권한으로 자동 시작 해제를 완료했습니다.")
                            : (elevatedEnable ? "관리자 권한 자동 시작 등록에 실패했습니다."
                                              : "관리자 권한 자동 시작 해제에 실패했습니다."),
                        "SSO Agent",
                        MessageBoxButtons.OK,
                        ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                    return; // UI 띄우지 않고 종료
                }

                Logger.Info("App starting...");

                // 1) 단일 인스턴스 보장
                if (!SingleInstance.Acquire())
                {
                    Logger.Info("Another instance detected. Exiting.");
                    return;
                }

                // 2) 전역 예외 핸들러
                Application.ThreadException += (s, e) => Logger.Error("ThreadException: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Logger.Error("UnhandledException: " + (e.ExceptionObject ?? "").ToString());
                };

                // 3) 첫 실행 필요 시 관리자 승격
                bool needsSetup = NeedsSetup();
                if (needsSetup && !IsAdministrator())
                {
                    Logger.Info("Needs setup and not elevated. Relaunching with runas...");
                    RelaunchAsAdmin();
                    return;
                }

                // 4) (관리자) 첫 실행 설정
                if (needsSetup)
                {
                    Directory.CreateDirectory(SetupMarkerDir);

                    var urlOk = SystemSetup.EnsureUrlAclElevated(AppConfig.ListenPort);
                    Logger.Info("URLACL result: " + urlOk);
                    if (!urlOk)
                    {
                        MessageBox.Show("URL ACL 등록에 실패했습니다. 관리자 권한으로 다시 실행해 주세요.",
                            "IDP SSO Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        // URLACL 실패면 HTTP 리스닝 자체가 막히므로, 마커 생성 없이 종료 권장
                        SingleInstance.Release();
                        return;
                    }

                    // 기본 모드: 작업 스케줄러
                    AutoStart.UseTaskSchedulerMode();
                    var autoOk = AutoStart.Enable();
                    Logger.Info("Autostart enable result: " + autoOk);
                    if (!autoOk)
                    {
                        MessageBox.Show("자동 시작 등록에 실패했습니다. 트레이 메뉴에서 다시 시도해 주세요.",
                            "IDP SSO Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        // 자동시작 실패는 치명적이지 않으므로 계속 진행
                    }

                    File.WriteAllText(SetupMarkerFile, "v1");
                }

                // 5) 현재 자동시작 상태 로그
                try
                {
                    var mode = AutoStart.Mode.ToString();
                    var enabled = AutoStart.IsEnabled();
                    Logger.Info($"Autostart mode={mode}, enabled={enabled}");
                }
                catch (Exception ex) { Logger.Error("Autostart status check failed: " + ex.Message); }

                // 6) 정상 실행
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ApplicationExit += (s, e) => { SingleInstance.Release(); };
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal: " + ex);
            }
            finally
            {
                // SingleInstance.Release()가 멱등이라면 유지, 아니라면 이 줄 제거
                SingleInstance.Release();
            }
        }

        private static bool NeedsSetup()
        {
            try { return !File.Exists(SetupMarkerFile); }
            catch { return true; }
        }

        private static bool IsAdministrator()
        {
            try
            {
                var wi = WindowsIdentity.GetCurrent();
                var wp = new WindowsPrincipal(wi);
                return wp.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void RelaunchAsAdmin()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Error("RelaunchAsAdmin failed: " + ex);
                MessageBox.Show("관리자 권한 상승이 취소되었거나 실패했습니다.", "IDP SSO Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}

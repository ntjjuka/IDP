using IDPSsoAgent.Config;
using IDPSsoAgent.Core;
using IDPSsoAgent.Infra;
using IDPSsoAgent.Server.Http;
using IDPSsoAgent.Startup;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IDPSsoAgent.UI
{
    public partial class MainForm : Form
    {
        private NotifyIcon _tray;
        private ContextMenu _menu;
        private HttpServer _server;

        public MainForm()
        {
            try
            {
                InitializeComponent();
                Logger.Info("MainForm init...");

                _menu = BuildMenu();
                _tray = new NotifyIcon
                {
                    Icon = Properties.Resources.icon, // Resources에 icon 포함 필요
                    Text = "IDP SSO Agent",
                    Visible = true,
                    ContextMenu = _menu
                };

                // 트레이 전용 앱처럼 동작
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                Visible = false;

                // 서버 시작(백그라운드)
                _server = new HttpServer(AppConfig.ListenPort);
                Task.Run(async () =>
                {
                    try
                    {
                        await _server.StartAsync();
                        Logger.Info("HttpServer started.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("HttpServer start failed: " + ex);
                        SafeUi(() => MessageBox.Show(
                            "내부 HTTP 서버 시작 중 오류가 발생했습니다.\n\n" + ex.Message,
                            "IDP SSO Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                    }
                });

                UpdateModeChecks();
            }
            catch (Exception ex)
            {
                Logger.Error("MainForm ctor failed: " + ex);
                throw;
            }
        }

        private ContextMenu BuildMenu()
        {
            return new ContextMenu(new[]
            {
                new MenuItem("상태", OnStatus),
                new MenuItem("자동 시작", new[]
                {
                    new MenuItem("작업 스케줄러 방식 사용", (s, e) => WithUiErrors(() =>
                    {
                        AutoStart.UseTaskSchedulerMode();
                        UpdateModeChecks();
                    })),
                    new MenuItem("Run 레지스트리 방식 사용", (s, e) => WithUiErrors(() =>
                    {
                        AutoStart.UseRegistryRunMode();
                        UpdateModeChecks();
                    })),
                    new MenuItem("-"),
                    new MenuItem("켜기", (s, e) => WithUiErrors(() =>
                    {
                        var ok = AutoStart.Enable();
                        if (!ok && AutoStart.Mode == AutostartMode.TaskScheduler)
                        {
                            var r = MessageBox.Show(
                                "작업 스케줄러 등록은 관리자 권한이 필요합니다.\n관리자 권한으로 다시 시도할까요?",
                                "SSO Agent", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                            if (r == DialogResult.Yes)
                            {
                                try
                                {
                                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                                    var psi = new System.Diagnostics.ProcessStartInfo(exe, "--elevated-enable-autostart")
                                    {
                                        UseShellExecute = true,
                                        Verb = "runas" // 관리자 상승
                                    };
                                    System.Diagnostics.Process.Start(psi);
                                    // 선택: 현재 인스턴스 종료 (사용자 경험 선호에 따라)
                                    // Application.Exit();
                                    return;
                                }
                                catch (Exception ex2)
                                {
                                    Logger.Error("Elevated retry launch failed: " + ex2);
                                    MessageBox.Show("관리자 권한 재시도 실행에 실패했습니다.", "Autostart",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            }
                        }

                        MessageBox.Show(ok ? "자동 시작 설정 완료" : "자동 시작 설정 실패",
                            "SSO Agent", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                        UpdateModeChecks();
                    })),
                    new MenuItem("끄기", (s, e) => WithUiErrors(() =>
                    {
                        var ok = AutoStart.Disable();
                        if (!ok && AutoStart.Mode == AutostartMode.TaskScheduler)
                        {
                            var r = MessageBox.Show(
                                "작업 스케줄러 해제는 관리자 권한이 필요합니다. \n관리자 권한으로 다시 시도할까요?",
                                "SSO Agent", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                            if (r == DialogResult.Yes)
                            {
                                try
                                {
                                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                                    var psi = new System.Diagnostics.ProcessStartInfo(exe, "--elevated-disable-autostart")
                                    {
                                        UseShellExecute = true,
                                        Verb = "runas" // 관리자 상승
                                    };
                                    System.Diagnostics.Process.Start(psi);
                                    // 선택: 현재 인스턴스 종료 여부는 UX에 따라
                                    // Application.Exit();
                                    return;
                                }
                                catch (Exception ex2)
                                {
                                    Logger.Error("Elevated disable launch failed: " + ex2);
                                    MessageBox.Show("관리자 권한 재시도 실행에 실패했습니다.", "Autostart",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            }
                        }

                        MessageBox.Show(ok ? "자동 시작 해제 완료" : "자동 시작 해제 실패",
                            "SSO Agent", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                        UpdateModeChecks();
                    })),

                }),
                new MenuItem("-"),
                new MenuItem("종료", OnExit)
            });
        }

        private void OnStatus(object sender, EventArgs e)
        {
            try
            {
                // GetStatus()가 null을 반환할 수도 있으니 방어
                var stat = (_server != null) ? _server.GetStatus() : null;
                bool listening = stat != null && stat.listening;

                string modeText, enabledText;
                try
                {
                    modeText = (AutoStart.Mode == AutostartMode.TaskScheduler) ? "작업 스케줄러 방식" : "레지스트리 방식";
                    enabledText = AutoStart.IsEnabled() ? "켜짐" : "꺼짐";
                }
                catch (Exception ex)
                {
                    Logger.Error("AutoStart status read failed: " + ex);
                    modeText = "Unknown";
                    enabledText = "Unknown";
                }

                var sb = new StringBuilder();
                sb.AppendLine("리스닝 : " + listening);
                sb.AppendLine("포트 : " + AppConfig.ListenPort);
                sb.AppendLine("로그인 여부 : " + TokenMemoryStore.IsLoggedIn);
                sb.AppendLine($"자동실행 : {enabledText} ({modeText})");

                MessageBox.Show(sb.ToString(), "SSO Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("OnStatus failed: " + ex);
                MessageBox.Show("상태 조회 중 오류가 발생했습니다.\n\n" + ex.Message,
                    "SSO Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateModeChecks()
        {
            try
            {
                if (_menu == null || _menu.MenuItems == null || _menu.MenuItems.Count < 2) return;

                var autoStartSubMenu = _menu.MenuItems[1];
                if (autoStartSubMenu?.MenuItems == null || autoStartSubMenu.MenuItems.Count < 2) return;

                bool isTask = (AutoStart.Mode == AutostartMode.TaskScheduler);
                autoStartSubMenu.MenuItems[0].Checked = isTask;   // 작업 스케줄러
                autoStartSubMenu.MenuItems[1].Checked = !isTask;  // 레지스트리 Run
            }
            catch (Exception ex)
            {
                Logger.Error("UpdateModeChecks failed: " + ex);
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            try
            {
                // 트레이/서버 자원 정리
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
                if (_server != null)
                {
                    _server.Stop();
                    _server = null;
                }
                Application.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error("OnExit failed: " + ex);
                Application.Exit();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
                if (_server != null)
                {
                    _server.Stop();
                    _server = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OnFormClosing cleanup failed: " + ex);
            }
            finally
            {
                base.OnFormClosing(e);
            }
        }

        private void SafeUi(Action a)
        {
            try
            {
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch (Exception ex)
            {
                Logger.Error("SafeUi failed: " + ex);
            }
        }

        private void WithUiErrors(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Logger.Error("UI action failed: " + ex);
                MessageBox.Show(ex.Message, "IDP SSO Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}

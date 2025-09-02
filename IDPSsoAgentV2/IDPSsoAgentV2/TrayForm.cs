using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace IDPSsoAgentV2
{
    public class TrayForm : Form
    {
        private readonly int _port;
        private readonly HttpServer _server;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;

        public TrayForm(int port)
        {
            _port = port;
            _server = new HttpServer(port);

            // 창 숨김
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Load += (s, e) => { Hide(); };

            _menu = new ContextMenuStrip();
            _menu.Items.Add("상태 열기", null, (_, __) => OpenStatus());
            _menu.Items.Add("재시작", null, (_, __) => { _server.Restart(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("종료", null, (_, __) => { Close(); });

            _tray = new NotifyIcon
            {
                Text = "IDP SSO Agent",
                Visible = true,
                Icon = SystemIcons.Shield, // 기본 아이콘
                ContextMenuStrip = _menu
            };

            Shown += (_, __) => { _server.Start(); };
            FormClosing += (_, __) => { try { _server.Stop(); } catch { } };
            Disposed += (_, __) => { try { _tray.Visible = false; _tray.Dispose(); } catch { } };
        }

        private void OpenStatus()
        {
            var url = $"https://127.0.0.1:{_port}/status";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}

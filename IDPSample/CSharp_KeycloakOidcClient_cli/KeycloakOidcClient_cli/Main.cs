// Keycloak CLI 클라이언트 - Main Form
// 자동 로그인, 토큰 동기화, 사용자 등록, 사용자 정보 표시 기능 포함

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;

namespace KeycloakOidcClient_cli
{
    public partial class Main : Form
    {
        private readonly string clientId = "cpp_sp_app";
        private readonly string realm = "master";
        private readonly string redirectUri = "http://localhost:3000/callback";
        private readonly string authServer = "http://192.168.0.2:5500";
        private const string TokenDir = @"C:\sso";
        private const string TokenPath = @"C:\sso\refresh_token.txt";

        private bool isLoggedIn = false;
        private string accessToken;
        private string refreshToken;
        private string adminToken;

        private FileSystemWatcher tokenWatcher;

        public Main()
        {
            InitializeComponent();

            // refresh_token 파일이 존재하면 자동 로그인 시도
            if (File.Exists(TokenPath))
            {
                refreshToken = File.ReadAllText(TokenPath);
                checkBox1.Checked = true;
            }

            if (checkBox1.Checked && !string.IsNullOrEmpty(refreshToken))
                TryAutoLogin();

            InitTokenWatcher();
        }

        /// <summary>
        /// 파일 변경 감지로 SSO 동기화
        /// </summary>
        private void InitTokenWatcher()
        {
            tokenWatcher = new FileSystemWatcher
            {
                Path = TokenDir,
                Filter = "refresh_token.txt",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            tokenWatcher.Changed += OnTokenChanged;
            tokenWatcher.Created += OnTokenChanged;
            tokenWatcher.Deleted += OnTokenDeleted;
            tokenWatcher.EnableRaisingEvents = true;
        }

        private void OnTokenChanged(object sender, FileSystemEventArgs e)
        {
            System.Threading.Thread.Sleep(200);

            try
            {
                if (File.Exists(TokenPath))
                {
                    string newToken = File.ReadAllText(TokenPath);
                    if (string.IsNullOrEmpty(newToken) || isLoggedIn) return;

                    refreshToken = newToken;
                    this.Invoke(new Action(() => TryAutoLogin()));
                }
            }
            catch (IOException) { }
        }

        private void OnTokenDeleted(object sender, FileSystemEventArgs e)
        {
            this.Invoke(new Action(() => btnLogout_Click(sender, e)));
        }

        /// <summary>
        /// Resource Owner Password 방식 로그인 처리
        /// </summary>
        private async void btnLogin_Click(object sender, EventArgs e)
        {
            string id = textBoxId.Text.Trim();
            string pw = textBoxPw.Text.Trim();

            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("grant_type", "password"),
                    new KeyValuePair<string, string>("username", id),
                    new KeyValuePair<string, string>("password", pw),
                    new KeyValuePair<string, string>("scope", "openid profile email")
                });

                var resp = await client.PostAsync($"{authServer}/realms/{realm}/protocol/openid-connect/token", content);
                string body = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    dynamic json = JsonConvert.DeserializeObject(body);
                    accessToken = json.access_token;
                    refreshToken = json.refresh_token;
                    isLoggedIn = true;

                    SaveRefreshToken(refreshToken);
                    ParseToken(accessToken);
                    MessageBox.Show("cli - 로그인 성공");
                }
                else
                {
                    MessageBox.Show("로그인 실패: " + body);
                }
            }
        }

        /// <summary>
        /// 로그아웃 및 토큰 정리
        /// </summary>
        private async void btnLogout_Click(object sender, EventArgs e)
        {
            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("refresh_token", refreshToken)
                });

                await client.PostAsync($"{authServer}/realms/{realm}/protocol/openid-connect/logout", content);
            }

            accessToken = null;
            refreshToken = null;
            adminToken = null;
            isLoggedIn = false;

            if (File.Exists(TokenPath))
                File.Delete(TokenPath);

            checkBox1.Checked = false;
            textBox1.Text = textBox2.Text = textBox3.Text = textBox4.Text = textBox5.Text = "";
            MessageBox.Show("로그아웃 완료");
        }

        /// <summary>
        /// refresh_token으로 access_token 재발급
        /// </summary>
        private async void TryAutoLogin()
        {
            if (string.IsNullOrEmpty(refreshToken)) return;

            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", refreshToken)
                });

                var resp = await client.PostAsync($"{authServer}/realms/{realm}/protocol/openid-connect/token", content);
                string body = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    dynamic json = JsonConvert.DeserializeObject(body);
                    accessToken = json.access_token;
                    refreshToken = json.refresh_token;
                    isLoggedIn = true;
                    ParseToken(accessToken);
                }
            }
        }

        /// <summary>
        /// refresh_token 파일 저장
        /// </summary>
        private void SaveRefreshToken(string token)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TokenPath));
                File.WriteAllText(TokenPath, token);
            }
            catch (Exception ex)
            {
                MessageBox.Show("refresh_token 저장 실패: " + ex.Message);
            }
        }

        /// <summary>
        /// 사용자 등록 처리
        /// </summary>
        private async void button1_Click(object sender, EventArgs e)
        {
            await GetAdminToken();
            string username = textBox7.Text.Trim();
            string password = textBox6.Text.Trim();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + adminToken);
                var userObj = new { username = username, enabled = true, emailVerified = true };
                var json = new StringContent(JsonConvert.SerializeObject(userObj), Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{authServer}/admin/realms/{realm}/users", json);
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show("회원 생성 실패");
                    return;
                }

                // 사용자 ID 조회 및 비밀번호 설정
                var idResp = await client.GetAsync($"{authServer}/admin/realms/{realm}/users?username={username}");
                var body = await idResp.Content.ReadAsStringAsync();
                var users = JsonConvert.DeserializeObject<List<dynamic>>(body);
                string userId = users.Count > 0 ? users[0].id : null;

                var pwObj = new { type = "password", value = password, temporary = false };
                var pwJson = new StringContent(JsonConvert.SerializeObject(pwObj), Encoding.UTF8, "application/json");

                var pwResp = await client.PutAsync($"{authServer}/admin/realms/{realm}/users/{userId}/reset-password", pwJson);
                MessageBox.Show(pwResp.IsSuccessStatusCode ? "회원가입 완료" : "비밀번호 설정 실패");
            }
        }

        /// <summary>
        /// 사용자 중복 확인
        /// </summary>
        private async void button2_Click(object sender, EventArgs e)
        {
            await GetAdminToken();
            string username = textBox7.Text.Trim();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + adminToken);
                var resp = await client.GetAsync($"{authServer}/admin/realms/{realm}/users?username={username}");
                var body = await resp.Content.ReadAsStringAsync();
                var list = JsonConvert.DeserializeObject<List<dynamic>>(body);
                MessageBox.Show(list.Count > 0 ? "이미 존재하는 아이디입니다." : "사용 가능한 아이디입니다.");
            }
        }

        /// <summary>
        /// access_token에서 사용자 정보 파싱
        /// </summary>
        private void ParseToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var claims = jwt.Claims.ToDictionary(c => c.Type, c => c.Value);

            textBox1.Text = claims.ContainsKey("sub") ? claims["sub"] : "";
            textBox2.Text = claims.ContainsKey("preferred_username") ? claims["preferred_username"] : "";
            textBox3.Text = JsonConvert.SerializeObject(claims);
            textBox4.Text = refreshToken;
            textBox5.Text = token;
        }

        /// <summary>
        /// 관리자 토큰 요청 (admin-cli)
        /// </summary>
        private async System.Threading.Tasks.Task GetAdminToken()
        {
            if (!string.IsNullOrEmpty(adminToken)) return;

            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", "admin-cli"),
                    new KeyValuePair<string, string>("grant_type", "password"),
                    new KeyValuePair<string, string>("username", "sp_master"),
                    new KeyValuePair<string, string>("password", "1q2w3e4r%T"),
                    new KeyValuePair<string, string>("scope", "openid profile email")
                });

                var resp = await client.PostAsync($"{authServer}/realms/master/protocol/openid-connect/token", content);
                string body = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    dynamic json = JsonConvert.DeserializeObject(body);
                    adminToken = json.access_token;
                }
                else
                {
                    MessageBox.Show("관리자 토큰 발급 실패: " + body);
                }
            }
        }

        /// <summary>
        /// 자동 로그인 체크박스 설정 저장
        /// </summary>
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                File.WriteAllText("auto_login.txt", checkBox1.Checked ? "1" : "0");
            }
            catch (Exception ex)
            {
                MessageBox.Show("자동로그인 설정 저장 실패: " + ex.Message);
            }
        }
    }
}
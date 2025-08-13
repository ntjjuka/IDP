using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Windows.Forms;

namespace KeycloakOidcClient_webview
{
    public partial class MainForm : Form
    {
        private readonly string clientId = "cpp_sp_app";
        private readonly string realm = "master";
        private readonly string redirectUri = "http://localhost:3000/callback";
        private readonly string authServer = "http://192.168.0.2:5500";

        private const string TokenPath = @"C:\sso\refresh_token.txt";
        private const string TokenDir = @"C:\sso";

        private string accessToken;
        private string refreshToken;
        private string codeVerifier;
        private bool isLoggedIn = false;

        private HttpListener listener;
        private FileSystemWatcher tokenWatcher;

        public MainForm()
        {
            InitializeComponent();
            InitWebView();
            InitTokenWatcher();
            UpdateButtonStates(false);
        }

        /// <summary>
        /// WebView 및 초기 자동 로그인 로직
        /// </summary>
        private async void InitWebView()
        {
            var env = await CoreWebView2Environment.CreateAsync(null, @"C:\WebView2UserData");
            await fWebView.EnsureCoreWebView2Async(env);
            fWebView.Visible = false;

            if (File.Exists(TokenPath))
            {
                refreshToken = File.ReadAllText(TokenPath);
                await TryAutoLogin();
            }
        }

        /// <summary>
        /// FileSystemWatcher 초기화 – refresh_token 변경 감지
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

        /// <summary>
        /// refresh_token 변경 시 자동 로그인 시도
        /// </summary>
        private void OnTokenChanged(object sender, FileSystemEventArgs e)
        {
            System.Threading.Thread.Sleep(200);

            try
            {
                if (!File.Exists(TokenPath)) return;

                string newToken = File.ReadAllText(TokenPath);
                if (string.IsNullOrEmpty(newToken) || isLoggedIn) return;

                refreshToken = newToken;
                this.Invoke(new Action(async () => await TryAutoLogin()));
            }
            catch (IOException) { /* 무시 */ }
        }

        /// <summary>
        /// refresh_token 삭제 시 로그아웃 처리
        /// </summary>
        private void OnTokenDeleted(object sender, FileSystemEventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                LogoutButton_Click(sender, e);
            }));
        }

        /// <summary>
        /// refresh_token을 통한 자동 로그인
        /// </summary>
        private async System.Threading.Tasks.Task TryAutoLogin()
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
                    dynamic json = JObject.Parse(body);
                    accessToken = json.access_token;
                    refreshToken = json.refresh_token;

                    File.WriteAllText(TokenPath, refreshToken);

                    textBoxAccessToken.Text = accessToken;
                    textBoxRefreshToken.Text = refreshToken;
                    GetUserProfile();
                    UpdateButtonStates(true);
                    isLoggedIn = true;
                }
            }
        }

        /// <summary>
        /// 회원가입 버튼 클릭
        /// </summary>
        private void SignupButton_Click(object sender, EventArgs e)
        {
            string signupUrl = $"{authServer}/realms/{realm}/protocol/openid-connect/registrations" +
                $"?client_id={clientId}" +
                $"&redirect_uri={HttpUtility.UrlEncode(redirectUri)}" +
                $"&response_type=code" +
                $"&scope=openid profile email" +
                $"&code_challenge_method=S256" +
                $"&code_challenge={GenerateCodeChallenge(CodeVerifier)}";

            StartHttpListener();
            fWebView.Visible = true;
            fWebView.Source = new Uri(signupUrl);
        }

        /// <summary>
        /// 로그인 버튼 클릭
        /// </summary>
        private void LoginButton_Click(object sender, EventArgs e)
        {
            StartHttpListener();
            BeginAuthorizationWithPkce();
        }

        /// <summary>
        /// 로그인 인증 요청 (PKCE)
        /// </summary>
        private void BeginAuthorizationWithPkce()
        {
            string authUrl = $"{authServer}/realms/{realm}/protocol/openid-connect/auth" +
                $"?client_id={clientId}" +
                $"&redirect_uri={HttpUtility.UrlEncode(redirectUri)}" +
                $"&response_type=code" +
                $"&scope=openid profile email" +
                $"&code_challenge_method=S256" +
                $"&code_challenge={GenerateCodeChallenge(CodeVerifier)}";

            fWebView.Visible = true;
            fWebView.Source = new Uri(authUrl);
        }

        /// <summary>
        /// HttpListener 시작 – 콜백 리스닝
        /// </summary>
        private void StartHttpListener()
        {
            if (listener != null && listener.IsListening)
                return;

            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:3000/callback/");
            listener.Start();
            listener.BeginGetContext(OnCallbackReceived, null);
        }

        /// <summary>
        /// 로그인 성공 후 callback 처리
        /// </summary>
        private void OnCallbackReceived(IAsyncResult ar)
        {
            try
            {
                var context = listener.EndGetContext(ar);
                var request = context.Request;
                var response = context.Response;
                string code = HttpUtility.ParseQueryString(request.Url.Query).Get("code");

                byte[] buffer = Encoding.UTF8.GetBytes("<html><body>Login Successful! You can close this window.</body></html>");
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                this.Invoke(new Action(() =>
                {
                    fWebView.Visible = false;
                    RequestToken(code);
                    GetUserProfile();

                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath));
                            File.WriteAllText(TokenPath, refreshToken);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("refresh_token 저장 실패: " + ex.Message);
                        }
                    }
                }));

                listener.Stop();
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show("리디렉션 처리 중 오류: " + ex.Message);
                }));
            }
        }

        /// <summary>
        /// Authorization Code 기반 토큰 요청
        /// </summary>
        private void RequestToken(string code)
        {
            using (var client = new WebClient())
            {
                var data = new System.Collections.Specialized.NameValueCollection
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = clientId,
                    ["code_verifier"] = CodeVerifier
                };

                byte[] response = client.UploadValues($"{authServer}/realms/{realm}/protocol/openid-connect/token", "POST", data);
                string json = Encoding.UTF8.GetString(response);
                var result = JObject.Parse(json);
                accessToken = result.Value<string>("access_token");
                refreshToken = result.Value<string>("refresh_token");

                textBoxAccessToken.Text = accessToken;
                textBoxRefreshToken.Text = refreshToken;
                isLoggedIn = true;
                UpdateButtonStates(true);
            }
        }

        /// <summary>
        /// 사용자 프로필 자동 조회 (access_token 기반)
        /// </summary>
        private void GetUserProfile()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("Authorization", "Bearer " + accessToken);
                    string json = client.DownloadString($"{authServer}/realms/{realm}/protocol/openid-connect/userinfo");
                    textBoxUserInfo.Text = json;
                }
            }
            catch (WebException ex)
            {
                using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                {
                    string error = reader.ReadToEnd();
                    MessageBox.Show("사용자 정보 조회 실패:\n" + error);
                }
            }
        }

        /// <summary>
        /// 프로필 조회 버튼 클릭
        /// </summary>
        private void GetProfile_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                MessageBox.Show("먼저 로그인하세요.");
                return;
            }

            GetUserProfile();
        }

        /// <summary>
        /// 로그아웃 버튼 클릭
        /// </summary>
        private void LogoutButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                MessageBox.Show("이미 로그아웃된 상태입니다.");
                return;
            }

            try
            {
                using (var client = new WebClient())
                {
                    var data = new System.Collections.Specialized.NameValueCollection
                    {
                        ["client_id"] = clientId,
                        ["refresh_token"] = refreshToken
                    };

                    client.UploadValues($"{authServer}/realms/{realm}/protocol/openid-connect/logout", "POST", data);
                }

                accessToken = null;
                refreshToken = null;
                codeVerifier = null;
                isLoggedIn = false;
                UpdateButtonStates(false);

                textBoxAccessToken.Text = "";
                textBoxRefreshToken.Text = "";
                textBoxUserId.Text = "";
                textBoxUserUid.Text = "";
                textBoxUserInfo.Text = "";

                if (File.Exists(TokenPath))
                    File.Delete(TokenPath);

                MessageBox.Show("로그아웃 완료!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("로그아웃 실패: " + ex.Message);
            }
        }

        /// <summary>
        /// PKCE Code Verifier 생성
        /// </summary>
        private string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// PKCE Code Challenge 생성
        /// </summary>
        private string GenerateCodeChallenge(string verifier)
        {
            var bytes = Encoding.ASCII.GetBytes(verifier);
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                return Base64UrlEncode(hash);
            }
        }

        /// <summary>
        /// base64 -> base64url 인코딩 변환
        /// </summary>
        private string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        /// <summary>
        /// 로그인/로그아웃 버튼 상태 제어
        /// </summary>
        private void UpdateButtonStates(bool loggedIn)
        {
            btnLogin.Enabled = !loggedIn;
            btnSign.Enabled = !loggedIn;

            btnLogout.Enabled = loggedIn;
            btnGetProfile.Enabled = loggedIn;
        }

        /// <summary>
        /// PKCE CodeVerifier Getter
        /// </summary>
        private string CodeVerifier
        {
            get
            {
                if (string.IsNullOrEmpty(codeVerifier))
                    codeVerifier = GenerateCodeVerifier();
                return codeVerifier;
            }
        }
    }
}

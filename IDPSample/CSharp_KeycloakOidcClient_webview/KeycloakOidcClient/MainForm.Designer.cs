namespace KeycloakOidcClient_webview
{
    partial class MainForm
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        /// <summary>
        /// 디자이너 지원에 필요한 메서드입니다. 
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            this.fWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.btnLogin = new System.Windows.Forms.Button();
            this.btnLogout = new System.Windows.Forms.Button();
            this.btnGetProfile = new System.Windows.Forms.Button();
            this.textBoxAccessToken = new System.Windows.Forms.TextBox();
            this.btnSign = new System.Windows.Forms.Button();
            this.labelAccessToken = new System.Windows.Forms.Label();
            this.labelRefreshToken = new System.Windows.Forms.Label();
            this.textBoxRefreshToken = new System.Windows.Forms.TextBox();
            this.container = new System.Windows.Forms.Panel();
            this.labelUserInfo = new System.Windows.Forms.Label();
            this.textBoxUserInfo = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.textBoxUserId = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.textBoxUserUid = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.fWebView)).BeginInit();
            this.container.SuspendLayout();
            this.SuspendLayout();
            // 
            // fWebView
            // 
            this.fWebView.AllowExternalDrop = true;
            this.fWebView.CreationProperties = null;
            this.fWebView.DefaultBackgroundColor = System.Drawing.Color.White;
            this.fWebView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.fWebView.Location = new System.Drawing.Point(0, 0);
            this.fWebView.Name = "fWebView";
            this.fWebView.Size = new System.Drawing.Size(884, 1009);
            this.fWebView.TabIndex = 0;
            this.fWebView.Visible = false;
            this.fWebView.ZoomFactor = 1D;
            // 
            // btnLogin
            // 
            this.btnLogin.Location = new System.Drawing.Point(420, 28);
            this.btnLogin.Name = "btnLogin";
            this.btnLogin.Size = new System.Drawing.Size(150, 32);
            this.btnLogin.TabIndex = 1;
            this.btnLogin.Text = "로그인";
            this.btnLogin.UseVisualStyleBackColor = true;
            this.btnLogin.Click += new System.EventHandler(this.LoginButton_Click);
            // 
            // btnLogout
            // 
            this.btnLogout.Location = new System.Drawing.Point(576, 30);
            this.btnLogout.Name = "btnLogout";
            this.btnLogout.Size = new System.Drawing.Size(162, 30);
            this.btnLogout.TabIndex = 2;
            this.btnLogout.Text = "로그아웃";
            this.btnLogout.UseVisualStyleBackColor = true;
            this.btnLogout.Click += new System.EventHandler(this.LogoutButton_Click);
            // 
            // btnGetProfile
            // 
            this.btnGetProfile.Location = new System.Drawing.Point(744, 30);
            this.btnGetProfile.Name = "btnGetProfile";
            this.btnGetProfile.Size = new System.Drawing.Size(199, 30);
            this.btnGetProfile.TabIndex = 3;
            this.btnGetProfile.Text = "프로필 보기";
            this.btnGetProfile.UseVisualStyleBackColor = true;
            this.btnGetProfile.Click += new System.EventHandler(this.GetProfile_Click);
            // 
            // textBoxAccessToken
            // 
            this.textBoxAccessToken.Location = new System.Drawing.Point(134, 1173);
            this.textBoxAccessToken.Name = "textBoxAccessToken";
            this.textBoxAccessToken.Size = new System.Drawing.Size(978, 28);
            this.textBoxAccessToken.TabIndex = 5;
            // 
            // btnSign
            // 
            this.btnSign.Location = new System.Drawing.Point(264, 28);
            this.btnSign.Name = "btnSign";
            this.btnSign.Size = new System.Drawing.Size(150, 32);
            this.btnSign.TabIndex = 6;
            this.btnSign.Text = "회원가입";
            this.btnSign.UseVisualStyleBackColor = true;
            this.btnSign.Click += new System.EventHandler(this.SignupButton_Click);
            // 
            // labelAccessToken
            // 
            this.labelAccessToken.AutoSize = true;
            this.labelAccessToken.Location = new System.Drawing.Point(8, 1176);
            this.labelAccessToken.Name = "labelAccessToken";
            this.labelAccessToken.Size = new System.Drawing.Size(120, 18);
            this.labelAccessToken.TabIndex = 7;
            this.labelAccessToken.Text = "AccessToken";
            // 
            // labelRefreshToken
            // 
            this.labelRefreshToken.AutoSize = true;
            this.labelRefreshToken.Location = new System.Drawing.Point(8, 1231);
            this.labelRefreshToken.Name = "labelRefreshToken";
            this.labelRefreshToken.Size = new System.Drawing.Size(120, 18);
            this.labelRefreshToken.TabIndex = 10;
            this.labelRefreshToken.Text = "RefreshToken";
            // 
            // textBoxRefreshToken
            // 
            this.textBoxRefreshToken.Location = new System.Drawing.Point(134, 1228);
            this.textBoxRefreshToken.Name = "textBoxRefreshToken";
            this.textBoxRefreshToken.Size = new System.Drawing.Size(978, 28);
            this.textBoxRefreshToken.TabIndex = 11;
            // 
            // container
            // 
            this.container.Controls.Add(this.fWebView);
            this.container.Location = new System.Drawing.Point(97, 65);
            this.container.Name = "container";
            this.container.Size = new System.Drawing.Size(884, 1009);
            this.container.TabIndex = 12;
            // 
            // labelUserInfo
            // 
            this.labelUserInfo.AutoSize = true;
            this.labelUserInfo.Location = new System.Drawing.Point(8, 1135);
            this.labelUserInfo.Name = "labelUserInfo";
            this.labelUserInfo.Size = new System.Drawing.Size(74, 18);
            this.labelUserInfo.TabIndex = 13;
            this.labelUserInfo.Text = "UserInfo";
            // 
            // textBoxUserInfo
            // 
            this.textBoxUserInfo.Location = new System.Drawing.Point(134, 1125);
            this.textBoxUserInfo.Name = "textBoxUserInfo";
            this.textBoxUserInfo.Size = new System.Drawing.Size(978, 28);
            this.textBoxUserInfo.TabIndex = 14;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("굴림", 15F);
            this.label1.Location = new System.Drawing.Point(6, 1080);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(163, 30);
            this.label1.TabIndex = 15;
            this.label1.Text = "사용자 ID :";
            // 
            // textBoxUserId
            // 
            this.textBoxUserId.Font = new System.Drawing.Font("굴림", 13F);
            this.textBoxUserId.Location = new System.Drawing.Point(166, 1077);
            this.textBoxUserId.Name = "textBoxUserId";
            this.textBoxUserId.Size = new System.Drawing.Size(180, 37);
            this.textBoxUserId.TabIndex = 16;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("굴림", 15F);
            this.label2.Location = new System.Drawing.Point(361, 1080);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(184, 30);
            this.label2.TabIndex = 17;
            this.label2.Text = "사용자 UID :";
            // 
            // textBoxUserUid
            // 
            this.textBoxUserUid.Font = new System.Drawing.Font("굴림", 13F);
            this.textBoxUserUid.Location = new System.Drawing.Point(548, 1077);
            this.textBoxUserUid.Name = "textBoxUserUid";
            this.textBoxUserUid.Size = new System.Drawing.Size(562, 37);
            this.textBoxUserUid.TabIndex = 18;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1151, 1297);
            this.Controls.Add(this.textBoxUserUid);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.textBoxUserId);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBoxUserInfo);
            this.Controls.Add(this.labelUserInfo);
            this.Controls.Add(this.container);
            this.Controls.Add(this.textBoxRefreshToken);
            this.Controls.Add(this.labelRefreshToken);
            this.Controls.Add(this.labelAccessToken);
            this.Controls.Add(this.btnSign);
            this.Controls.Add(this.textBoxAccessToken);
            this.Controls.Add(this.btnGetProfile);
            this.Controls.Add(this.btnLogout);
            this.Controls.Add(this.btnLogin);
            this.Name = "MainForm";
            this.Text = "Keycloak SSO Sample";
            ((System.ComponentModel.ISupportInitialize)(this.fWebView)).EndInit();
            this.container.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 fWebView;
        private System.Windows.Forms.Button btnLogin;
        private System.Windows.Forms.Button btnLogout;
        private System.Windows.Forms.Button btnGetProfile;
        private System.Windows.Forms.TextBox textBoxAccessToken;
        private System.Windows.Forms.Button btnSign;
        private System.Windows.Forms.Label labelAccessToken;
        private System.Windows.Forms.Label labelRefreshToken;
        private System.Windows.Forms.TextBox textBoxRefreshToken;
        private System.Windows.Forms.Panel container;
        private System.Windows.Forms.Label labelUserInfo;
        private System.Windows.Forms.TextBox textBoxUserInfo;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textBoxUserId;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox textBoxUserUid;
    }
}


using System;
using System.Drawing;
using System.Management.Automation;
using System.Security;
using System.Windows.Forms;
using WindowsUpdateManager.Theme;

namespace WindowsUpdateManager.Forms
{
    public class LoginForm : Form
    {
        private TextBox   _txtUser;
        private TextBox   _txtPassword;
        private Button    _btnLogin;
        private Label     _lblStatus;

        public PSCredential Credential { get; private set; }

        public LoginForm()
        {
            InitializeComponent();
            DarkTheme.Apply(this);
        }

        private void InitializeComponent()
        {
            Text            = "Windows Update Manager – Anmeldung";
            Size            = new Size(420, 300);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            BackColor       = DarkTheme.Background;
            ForeColor       = DarkTheme.Text;
            Font            = new Font("Segoe UI", 9.5f);

            // ---- Logo / Header label ----
            var lblLogo = new Label();
            lblLogo.Text      = "Windows Update Manager";
            lblLogo.Font      = new Font("Segoe UI", 14f, FontStyle.Bold);
            lblLogo.ForeColor = DarkTheme.Accent;
            lblLogo.BackColor = Color.Transparent;
            lblLogo.AutoSize  = false;
            lblLogo.TextAlign = ContentAlignment.MiddleCenter;
            lblLogo.Bounds    = new Rectangle(0, 18, 420, 42);

            var lblSubtitle = new Label();
            lblSubtitle.Text      = "Remote-Verwaltung von Windows Updates";
            lblSubtitle.Font      = new Font("Segoe UI", 8.5f);
            lblSubtitle.ForeColor = DarkTheme.TextMuted;
            lblSubtitle.BackColor = Color.Transparent;
            lblSubtitle.AutoSize  = false;
            lblSubtitle.TextAlign = ContentAlignment.MiddleCenter;
            lblSubtitle.Bounds    = new Rectangle(0, 58, 420, 22);

            // ---- User label + textbox ----
            var lblUser = new Label();
            lblUser.Text      = "Benutzer (Domäne\\Benutzer):";
            lblUser.ForeColor = DarkTheme.Text;
            lblUser.BackColor = Color.Transparent;
            lblUser.AutoSize  = true;
            lblUser.Location  = new Point(36, 98);

            _txtUser = new TextBox();
            _txtUser.BackColor   = DarkTheme.Surface;
            _txtUser.ForeColor   = DarkTheme.TextMuted;
            _txtUser.BorderStyle = BorderStyle.FixedSingle;
            _txtUser.Bounds      = new Rectangle(36, 118, 348, 26);
            _txtUser.Font        = new Font("Segoe UI", 9.5f);
            _txtUser.Text        = "DOMAIN\\Administrator";

            // Simulate placeholder behaviour
            _txtUser.GotFocus += (s, e) =>
            {
                if (_txtUser.Text == "DOMAIN\\Administrator")
                {
                    _txtUser.Text      = "";
                    _txtUser.ForeColor = DarkTheme.Text;
                }
            };
            _txtUser.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(_txtUser.Text))
                {
                    _txtUser.Text      = "DOMAIN\\Administrator";
                    _txtUser.ForeColor = DarkTheme.TextMuted;
                }
            };

            // ---- Password label + textbox ----
            var lblPassword = new Label();
            lblPassword.Text      = "Passwort:";
            lblPassword.ForeColor = DarkTheme.Text;
            lblPassword.BackColor = Color.Transparent;
            lblPassword.AutoSize  = true;
            lblPassword.Location  = new Point(36, 154);

            _txtPassword = new TextBox();
            _txtPassword.PasswordChar = '●';  // ●
            _txtPassword.BackColor    = DarkTheme.Surface;
            _txtPassword.ForeColor    = DarkTheme.Text;
            _txtPassword.BorderStyle  = BorderStyle.FixedSingle;
            _txtPassword.Bounds       = new Rectangle(36, 174, 348, 26);
            _txtPassword.Font         = new Font("Segoe UI", 9.5f);

            // ---- Login button ----
            _btnLogin = new Button();
            _btnLogin.Text      = "Anmelden";
            _btnLogin.BackColor = DarkTheme.Accent;
            _btnLogin.ForeColor = Color.White;
            _btnLogin.FlatStyle = FlatStyle.Flat;
            _btnLogin.Bounds    = new Rectangle(36, 215, 348, 36);
            _btnLogin.Font      = new Font("Segoe UI", 10f, FontStyle.Bold);
            _btnLogin.FlatAppearance.BorderColor        = DarkTheme.Accent;
            _btnLogin.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 0, 100, 190);
            _btnLogin.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 0, 60, 130);
            _btnLogin.Click += new EventHandler(BtnLogin_Click);

            // ---- Status label ----
            _lblStatus = new Label();
            _lblStatus.Text      = "";
            _lblStatus.ForeColor = DarkTheme.Error;
            _lblStatus.BackColor = Color.Transparent;
            _lblStatus.AutoSize  = false;
            _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatus.Bounds    = new Rectangle(36, 256, 348, 20);
            _lblStatus.Visible   = false;

            AcceptButton = _btnLogin;

            Controls.AddRange(new Control[]
            {
                lblLogo, lblSubtitle,
                lblUser, _txtUser,
                lblPassword, _txtPassword,
                _btnLogin, _lblStatus
            });

            _txtPassword.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) BtnLogin_Click(s, e);
            };
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            _lblStatus.Visible = false;

            string userText = _txtUser.Text != null ? _txtUser.Text.Trim() : "";
            string passText = _txtPassword.Text;

            // Treat placeholder text as empty
            if (userText == "DOMAIN\\Administrator") userText = "";

            if (string.IsNullOrEmpty(userText))
            {
                ShowError("Bitte Benutzernamen eingeben.");
                _txtUser.Focus();
                return;
            }

            if (string.IsNullOrEmpty(passText))
            {
                ShowError("Bitte Passwort eingeben.");
                _txtPassword.Focus();
                return;
            }

            try
            {
                string userName = userText;

                var securePass = new SecureString();
                foreach (char c in passText)
                    securePass.AppendChar(c);
                securePass.MakeReadOnly();

                Credential   = new PSCredential(userName, securePass);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                ShowError("Fehler: " + ex.Message);
            }
        }

        private void ShowError(string message)
        {
            _lblStatus.Text    = message;
            _lblStatus.Visible = true;
        }
    }
}

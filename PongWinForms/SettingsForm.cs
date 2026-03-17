
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PongWinForms
{
    public class SettingsForm : Form
    {
        // Controls
        private RadioButton _rbSingle;
        private RadioButton _rbTwo;
        private NumericUpDown _numMaxScore;
        private NumericUpDown _numPaddleSpeed;
        private NumericUpDown _numBallSpeed;
        private NumericUpDown _numBounceAngle;
        private NumericUpDown _numMaxBallSpeed;
        private NumericUpDown _numWidth;
        private NumericUpDown _numHeight;
        private NumericUpDown _numPaddleHeight;

        private CheckBox _chkSound;
        private CheckBox _chkPowerUps;
        private NumericUpDown _numPowerSpawn;
        private NumericUpDown _numPowerDuration;
        private CheckBox _chkStartFullscreen;

        private Button _btnOk;
        private Button _btnCancel;

        private RadioButton _rbNetOffline;
        private RadioButton _rbNetHost;
        private RadioButton _rbNetClient;
        private TextBox _txtHostIp;
        private NumericUpDown _numPort;


        public GameSettings Result { get; private set; }

        public SettingsForm(GameSettings defaults)
        {
            // In SettingsForm constructor, very top:
            this.AutoScaleMode = AutoScaleMode.Dpi;   // good for high-DPI

            // Window look
            Text = "Pong – Instellingen";
            BackColor = Color.FromArgb(38, 38, 38);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 760);

            Label L(string t, int x, int y, int w = 220) => new Label
            {
                Text = t,
                Left = x,
                Top = y,
                Width = w,
                ForeColor = Color.White
            };

            // Mode
            var grpMode = new GroupBox { Text = "Mode", Left = 20, Top = 15, Width = 250, Height = 90, ForeColor = Color.White };
            _rbSingle = new RadioButton { Text = "1 speler (AI rechts)", Left = 15, Top = 25, Width = 200, ForeColor = Color.White };
            _rbTwo = new RadioButton { Text = "2 spelers", Left = 15, Top = 50, Width = 200, ForeColor = Color.White };
            grpMode.Controls.AddRange(new Control[] { _rbSingle, _rbTwo });

            // Score
            var grpScore = new GroupBox { Text = "Score", Left = 290, Top = 15, Width = 250, Height = 90, ForeColor = Color.White };
            grpScore.Controls.Add(L("Max score:", 15, 30));
            _numMaxScore = new NumericUpDown { Left = 140, Top = 25, Width = 80, Minimum = 1, Maximum = 99 };
            grpScore.Controls.Add(_numMaxScore);

            // Speeds
            var grpSpeed = new GroupBox { Text = "Snelheid & Physics", Left = 20, Top = 115, Width = 520, Height = 150, ForeColor = Color.White };
            grpSpeed.Controls.Add(L("Paddle snelheid:", 15, 30));
            _numPaddleSpeed = new NumericUpDown { Left = 160, Top = 25, Width = 80, DecimalPlaces = 1, Increment = 0.5M, Minimum = 2, Maximum = 30 };
            grpSpeed.Controls.Add(_numPaddleSpeed);

            grpSpeed.Controls.Add(L("Bal snelheid (start):", 260, 30));
            _numBallSpeed = new NumericUpDown { Left = 415, Top = 25, Width = 80, DecimalPlaces = 1, Increment = 0.5M, Minimum = 2, Maximum = 30 };
            grpSpeed.Controls.Add(_numBallSpeed);

            grpSpeed.Controls.Add(L("Max bounce hoek (°):", 15, 70));
            _numBounceAngle = new NumericUpDown { Left = 160, Top = 65, Width = 80, DecimalPlaces = 0, Increment = 1, Minimum = 10, Maximum = 85 };
            grpSpeed.Controls.Add(_numBounceAngle);

            grpSpeed.Controls.Add(L("Max bal snelheid:", 260, 70));
            _numMaxBallSpeed = new NumericUpDown { Left = 415, Top = 65, Width = 80, DecimalPlaces = 1, Increment = 0.5M, Minimum = 4, Maximum = 60 };
            grpSpeed.Controls.Add(_numMaxBallSpeed);

            grpSpeed.Controls.Add(L("Paddle hoogte:", 15, 110));
            _numPaddleHeight = new NumericUpDown { Left = 160, Top = 105, Width = 80, DecimalPlaces = 0, Increment = 5, Minimum = 40, Maximum = 200 };
            grpSpeed.Controls.Add(_numPaddleHeight);

            // Power-ups
            var grpPower = new GroupBox { Text = "Power-ups", Left = 20, Top = 275, Width = 520, Height = 110, ForeColor = Color.White };
            _chkPowerUps = new CheckBox { Text = "Power-ups inschakelen", Left = 15, Top = 25, Width = 200, ForeColor = Color.White, Checked = true };
            grpPower.Controls.Add(_chkPowerUps);

            grpPower.Controls.Add(L("Spawn interval (s):", 15, 60));
            _numPowerSpawn = new NumericUpDown { Left = 160, Top = 55, Width = 80, Minimum = 5, Maximum = 120, Value = 12 };
            grpPower.Controls.Add(_numPowerSpawn);

            grpPower.Controls.Add(L("Duur effect (s):", 260, 60));
            _numPowerDuration = new NumericUpDown { Left = 415, Top = 55, Width = 80, Minimum = 3, Maximum = 60, Value = 8 };
            grpPower.Controls.Add(_numPowerDuration);

            // Audio & Window
            var grpMisc = new GroupBox { Text = "Audio & Venster", Left = 20, Top = 395, Width = 520, Height = 90, ForeColor = Color.White };
            _chkSound = new CheckBox { Text = "Geluidseffecten", Left = 15, Top = 25, Width = 160, ForeColor = Color.White, Checked = true };
            grpMisc.Controls.Add(_chkSound);

            _chkStartFullscreen = new CheckBox { Text = "Start in fullscreen (F11 toggle in game)", Left = 200, Top = 25, Width = 300, ForeColor = Color.White };
            grpMisc.Controls.Add(_chkStartFullscreen);

            grpMisc.Controls.Add(L("Breedte:", 15, 60));
            _numWidth = new NumericUpDown { Left = 80, Top = 55, Width = 80, Minimum = 600, Maximum = 1920, Increment = 20, Value = 900 };
            grpMisc.Controls.Add(_numWidth);

            grpMisc.Controls.Add(L("Hoogte:", 200, 60));
            _numHeight = new NumericUpDown { Left = 260, Top = 55, Width = 80, Minimum = 400, Maximum = 1200, Increment = 20, Value = 550 };
            grpMisc.Controls.Add(_numHeight);

            // Buttons
            _btnOk = new Button { Text = "Start", Left = 345, Top = 500, Width = 90, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(70, 70, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnOk.FlatAppearance.BorderColor = Color.FromArgb(95, 95, 95);
            _btnCancel = new Button { Text = "Annuleren", Left = 450, Top = 500, Width = 90, DialogResult = DialogResult.Cancel, BackColor = Color.FromArgb(70, 70, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(95, 95, 95);



            // --- Network group ---
            var grpNet = new GroupBox { Text = "Netwerk", Left = 20, Top = 495, Width = 520, Height = 110, ForeColor = Color.White };

            _rbNetOffline = new RadioButton { Text = "Offline (lokale modus)", Left = 15, Top = 25, Width = 180, ForeColor = Color.White, Checked = true };
            _rbNetHost = new RadioButton { Text = "Host", Left = 210, Top = 25, Width = 80, ForeColor = Color.White };
            _rbNetClient = new RadioButton { Text = "Client (verbind met host)", Left = 300, Top = 25, Width = 200, ForeColor = Color.White };

            grpNet.Controls.AddRange(new Control[] { _rbNetOffline, _rbNetHost, _rbNetClient });

            grpNet.Controls.Add(new Label { Text = "Host IP:", Left = 15, Top = 60, Width = 60, ForeColor = Color.White });
            _txtHostIp = new TextBox { Left = 80, Top = 56, Width = 170, Enabled = false };
            grpNet.Controls.Add(_txtHostIp);

            grpNet.Controls.Add(new Label { Text = "Port:", Left = 270, Top = 60, Width = 40, ForeColor = Color.White });
            _numPort = new NumericUpDown { Left = 315, Top = 56, Width = 80, Minimum = 1024, Maximum = 65535, Value = 51337 };
            grpNet.Controls.Add(_numPort);

            _rbNetClient.CheckedChanged += (s, e) => _txtHostIp.Enabled = _rbNetClient.Checked;

            Controls.Add(grpNet);


            Controls.AddRange(new Control[] { grpMode, grpScore, grpSpeed, grpPower, grpMisc, _btnOk, _btnCancel });
            AcceptButton = _btnOk; CancelButton = _btnCancel;

            LoadDefaults(defaults);

            _btnOk.Click += (s, e) =>
            {
                var sgs = new GameSettings
                {
                    SinglePlayer = _rbSingle.Checked,
                    MaxScore = (int)_numMaxScore.Value,
                    PaddleSpeed = (float)_numPaddleSpeed.Value,
                    BallSpeed = (float)_numBallSpeed.Value,
                    MaxBounceAngleDeg = (float)_numBounceAngle.Value,
                    MaxBallSpeed = (float)_numMaxBallSpeed.Value,
                    PaddleHeight = (float)_numPaddleHeight.Value,
                    PowerUpsEnabled = _chkPowerUps.Checked,
                    PowerUpSpawnIntervalSec = (int)_numPowerSpawn.Value,
                    PowerUpDurationSec = (int)_numPowerDuration.Value,
                    SoundEnabled = _chkSound.Checked,
                    WindowWidth = (int)_numWidth.Value,
                    WindowHeight = (int)_numHeight.Value,
                    StartFullscreen = _chkStartFullscreen.Checked,
                    NetworkMode = _rbNetHost.Checked ? NetMode.Host : _rbNetClient.Checked ? NetMode.Client : NetMode.Offline,
                    HostIp = _txtHostIp.Text.Trim(),
                    NetPort = (int)_numPort.Value
                };


                sgs.Normalize();
                if (sgs.MaxBallSpeed < sgs.BallSpeed)
                {
                    MessageBox.Show("Max bal snelheid mag niet lager zijn dan startsnelheid.",
                        "Ongeldige instelling", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                Result = sgs;
            };


            // At the end of the SettingsForm constructor:
            ApplyDarkThemeToNumericUpDowns(this);

            CompactLabels(this);

        }

        private void LoadDefaults(GameSettings d)
        {
            _rbSingle.Checked = d.SinglePlayer;
            _rbTwo.Checked = !d.SinglePlayer;

            _numMaxScore.Value = Math.Clamp(d.MaxScore, 1, 99);
            _numPaddleSpeed.Value = (decimal)Math.Clamp(d.PaddleSpeed, 2f, 30f);
            _numBallSpeed.Value = (decimal)Math.Clamp(d.BallSpeed, 2f, 30f);
            _numBounceAngle.Value = (decimal)Math.Clamp(d.MaxBounceAngleDeg, 10f, 85f);
            _numMaxBallSpeed.Value = (decimal)Math.Clamp(d.MaxBallSpeed, 4f, 60f);
            _numPaddleHeight.Value = (decimal)Math.Clamp(d.PaddleHeight, 40f, 200f);

            _chkPowerUps.Checked = d.PowerUpsEnabled;
            _numPowerSpawn.Value = Math.Clamp(d.PowerUpSpawnIntervalSec, 5, 120);
            _numPowerDuration.Value = Math.Clamp(d.PowerUpDurationSec, 3, 60);

            _chkSound.Checked = d.SoundEnabled;
            _chkStartFullscreen.Checked = d.StartFullscreen;

            _numWidth.Value = Math.Clamp(d.WindowWidth, 600, 1920);
            _numHeight.Value = Math.Clamp(d.WindowHeight, 400, 1200);

            
            _rbNetOffline.Checked = d.NetworkMode == NetMode.Offline;
            _rbNetHost.Checked    = d.NetworkMode == NetMode.Host;
            _rbNetClient.Checked  = d.NetworkMode == NetMode.Client;
            _txtHostIp.Text       = d.HostIp ?? "";
            _numPort.Value        = Math.Clamp(d.NetPort, 1024, 65535);

        }


        // Call this once after you've created all controls on the form.
        // It walks the control tree and styles every NumericUpDown for dark theme readability.
        private void ApplyDarkThemeToNumericUpDowns(Control root)
        {
            Color textColor = Color.White;
            Color boxColor = Color.FromArgb(60, 60, 60);   // textbox background inside numeric
            Color btnColor = Color.FromArgb(70, 70, 70);   // up/down buttons background
            var defaultFont = new Font("Segoe UI", 10.0f, FontStyle.Regular);

            void StyleOne(NumericUpDown nud)
            {
                // Outer control
                nud.BackColor = boxColor;   // affects the "host"
                nud.ForeColor = textColor;  // sometimes ignored by inner TextBox
                nud.BorderStyle = BorderStyle.FixedSingle;
                if (nud.Font == null || Math.Abs(nud.Font.Size - defaultFont.Size) > 0.1f)
                    nud.Font = defaultFont;

                // Inner controls: [0] = UpDownButtons, [1] = TextBox (order is consistent in WinForms)
                if (nud.Controls.Count > 0)
                {
                    // Up/Down buttons
                    Control btns = nud.Controls[0];
                    btns.BackColor = btnColor;
                    btns.ForeColor = textColor;

                    // Text box where numbers are drawn
                    if (nud.Controls.Count > 1 && nud.Controls[1] is TextBox tb)
                    {
                        tb.BackColor = boxColor;
                        tb.ForeColor = textColor;
                        tb.BorderStyle = BorderStyle.None; // host already has border
                        if (tb.Font == null || Math.Abs(tb.Font.Size - defaultFont.Size) > 0.1f)
                            tb.Font = defaultFont;
                    }
                }
            }

            // Recurse through the control tree
            void Walk(Control c)
            {
                if (c is NumericUpDown nud) StyleOne(nud);
                foreach (Control child in c.Controls) Walk(child);
            }

            Walk(root);
        }


        // Call this once in the SettingsForm constructor AFTER all labels/inputs are created.
        private void CompactLabels(Control root)
        {
            // Compact, readable on dark theme
            var font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            void Walk(Control c)
            {
                foreach (Control child in c.Controls)
                {
                    if (child is Label lbl)
                    {
                        // Make labels autosize and wrap within a max width
                        lbl.AutoSize = true;
                        lbl.Font = font;

                        // For wrapping: set Max width; height 0 means "no limit"
                        // Adjust width per group area so text wraps instead of overlapping inputs
                        lbl.MaximumSize = new Size(200, 0);     // tune 160–220 px per kolombreedte
                        lbl.AutoEllipsis = false;               // show full text (wrap) not "..."
                        lbl.Margin = new Padding(0, 2, 8, 2);   // little gap before the input
                    }
                    Walk(child);
                }
            }

            Walk(root);
        }


    }


}


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
        private NumericUpDown _numWidth;
        private NumericUpDown _numHeight;
        private NumericUpDown _numMaxBallSpeed;
        private Button _btnOk;
        private Button _btnCancel;

        public GameSettings Result { get; private set; }

        public SettingsForm(GameSettings defaults)
        {
            // Window look
            Text = "Pong – Instellingen";
            BackColor = Color.FromArgb(38, 38, 38);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 440);

            // Labels factory
            Label L(string t, int x, int y, int w = 220)
            {
                return new Label
                {
                    Text = t,
                    Left = x,
                    Top = y,
                    Width = w,
                    ForeColor = Color.White
                };
            }

            // Group: Mode
            var grpMode = new GroupBox
            {
                Text = "Mode",
                Left = 20, Top = 15, Width = 220, Height = 90,
                ForeColor = Color.White
            };
            _rbSingle = new RadioButton { Text = "1 speler (AI rechts)", Left = 15, Top = 25, Width = 180, ForeColor = Color.White };
            _rbTwo = new RadioButton { Text = "2 spelers", Left = 15, Top = 50, Width = 180, ForeColor = Color.White };
            grpMode.Controls.AddRange(new Control[] { _rbSingle, _rbTwo });

            // Group: Scores
            var grpScore = new GroupBox
            {
                Text = "Score",
                Left = 260, Top = 15, Width = 240, Height = 90,
                ForeColor = Color.White
            };
            grpScore.Controls.Add(L("Max score:", 15, 30));
            _numMaxScore = new NumericUpDown
            {
                Left = 140, Top = 25, Width = 70,
                Minimum = 1, Maximum = 99
            };
            grpScore.Controls.Add(_numMaxScore);

            // Group: Speeds
            var grpSpeed = new GroupBox
            {
                Text = "Snelheid",
                Left = 20, Top = 115, Width = 480, Height = 140,
                ForeColor = Color.White
            };
            grpSpeed.Controls.Add(L("Paddle snelheid:", 15, 30));
            _numPaddleSpeed = new NumericUpDown
            {
                Left = 160, Top = 25, Width = 80,
                DecimalPlaces = 1, Increment = 0.5M, Minimum = 2, Maximum = 30
            };
            grpSpeed.Controls.Add(_numPaddleSpeed);

            grpSpeed.Controls.Add(L("Bal snelheid (start):", 255, 30, 200));
            _numBallSpeed = new NumericUpDown
            {
                Left = 400, Top = 25, Width = 80,
                DecimalPlaces = 1, Increment = 0.5M, Minimum = 2, Maximum = 30
            };
            grpSpeed.Controls.Add(_numBallSpeed);

            grpSpeed.Controls.Add(L("Max bounce hoek (°):", 15, 70));
            _numBounceAngle = new NumericUpDown
            {
                Left = 160, Top = 65, Width = 80,
                DecimalPlaces = 0, Increment = 1, Minimum = 10, Maximum = 85
            };
            grpSpeed.Controls.Add(_numBounceAngle);

            grpSpeed.Controls.Add(L("Max bal snelheid:", 255, 70, 200));
            _numMaxBallSpeed = new NumericUpDown
            {
                Left = 400, Top = 65, Width = 80,
                DecimalPlaces = 1, Increment = 0.5M, Minimum = 4, Maximum = 60
            };
            grpSpeed.Controls.Add(_numMaxBallSpeed);

            // Group: Afmeting
            var grpSize = new GroupBox
            {
                Text = "Venstergrootte",
                Left = 20, Top = 265, Width = 480, Height = 90,
                ForeColor = Color.White
            };
            grpSize.Controls.Add(L("Breedte:", 15, 30));
            _numWidth = new NumericUpDown
            {
                Left = 80, Top = 25, Width = 80,
                Minimum = 600, Maximum = 1920, Increment = 20, Value = 900
            };
            grpSize.Controls.Add(_numWidth);

            grpSize.Controls.Add(L("Hoogte:", 200, 30));
            _numHeight = new NumericUpDown
            {
                Left = 260, Top = 25, Width = 80,
                Minimum = 400, Maximum = 1200, Increment = 20, Value = 550
            };
            grpSize.Controls.Add(_numHeight);

            // Buttons
            _btnOk = new Button
            {
                Text = "Start",
                Left = 300, Top = 375, Width = 90,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnOk.FlatAppearance.BorderColor = Color.FromArgb(95, 95, 95);

            _btnCancel = new Button
            {
                Text = "Annuleren",
                Left = 410, Top = 375, Width = 90,
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(95, 95, 95);

            Controls.AddRange(new Control[]
            {
                grpMode, grpScore, grpSpeed, grpSize, _btnOk, _btnCancel
            });

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            // Load defaults into controls
            LoadDefaults(defaults);

            // Build result on OK
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
                    WindowWidth = (int)_numWidth.Value,
                    WindowHeight = (int)_numHeight.Value
                };
                sgs.Normalize();

                // Additional sanity check: MaxBallSpeed >= BallSpeed
                if (sgs.MaxBallSpeed < sgs.BallSpeed)
                {
                    MessageBox.Show("Max bal snelheid mag niet lager zijn dan startsnelheid.",
                        "Ongeldige instelling", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }

                Result = sgs;
            };
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

            _numWidth.Value = Math.Clamp(d.WindowWidth, 600, 1920);
            _numHeight.Value = Math.Clamp(d.WindowHeight, 400, 1200);
        }
    }
}


using System;
using System.Drawing;
using System.Windows.Forms;

namespace PongWinForms
{
    public class MainForm : Form
    {
        // --- Rendering & Timing ---
        private readonly Timer _timer = new Timer(); // ~60 FPS
        private const int FPS = 60;

        // --- Game objects ---
        private RectangleF _leftPaddle;
        private RectangleF _rightPaddle;
        private RectangleF _ball;

        // --- Sizes & speeds (initialized from settings) ---
        private const float PaddleWidth = 12f;
        private const float PaddleHeight = 90f;
        private const float BallSize = 14f;
        private float _paddleSpeed;
        private float _ballSpeed;
        private float _maxBounceAngleDeg;
        private float _maxBallSpeed;

        private PointF _ballVelocity;

        // --- Input state ---
        private bool _wPressed, _sPressed, _upPressed, _downPressed;
        private bool _paused = false;

        // --- Modes & scoring ---
        private bool _singlePlayer;
        private int _leftScore = 0, _rightScore = 0;
        private int _maxScore;

        // --- Random ---
        private readonly Random _rng = new Random();

        // --- Menu ---
        private MenuStrip _menu;
        private ToolStripMenuItem _menuGame;
        private ToolStripMenuItem _menuSettings;
        private ToolStripMenuItem _menuPause;
        private ToolStripMenuItem _menuReset;

        // --- Keep original settings to reopen dialog ---
        private GameSettings _currentSettings;

        public MainForm(GameSettings settings)
        {
            _currentSettings = settings ?? new GameSettings();
            _currentSettings.Normalize();

            // Apply from settings
            _singlePlayer = _currentSettings.SinglePlayer;
            _maxScore = _currentSettings.MaxScore;
            _paddleSpeed = _currentSettings.PaddleSpeed;
            _ballSpeed = _currentSettings.BallSpeed;
            _maxBounceAngleDeg = _currentSettings.MaxBounceAngleDeg;
            _maxBallSpeed = _currentSettings.MaxBallSpeed;

            // Window styling
            Text = "Pong – WinForms";
            ClientSize = new Size(_currentSettings.WindowWidth, _currentSettings.WindowHeight);
            BackColor = Color.Black;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Double buffering
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            UpdateStyles();

            CreateMenu();

            // Timer setup
            _timer.Interval = 1000 / FPS;
            _timer.Tick += GameLoop;

            // Keyboard events
            KeyPreview = true;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;

            // Initialize game objects
            ResetPaddles();
            StartNewRound(serveToRight: _rng.Next(2) == 0);

            _timer.Start();
        }

        private void CreateMenu()
        {
            _menu = new MenuStrip { Dock = DockStyle.Top };
            _menuGame = new ToolStripMenuItem("Game");
            _menuSettings = new ToolStripMenuItem("Instellingen...");
            _menuPause = new ToolStripMenuItem("Pauze/Hervat") { ShortcutKeys = Keys.Space };
            _menuReset = new ToolStripMenuItem("Reset") { ShortcutKeys = Keys.Enter };

            _menuSettings.Click += (s, e) => OpenSettingsDialog();
            _menuPause.Click += (s, e) => _paused = !_paused;
            _menuReset.Click += (s, e) => ResetMatch();

            _menuGame.DropDownItems.AddRange(new ToolStripItem[] { _menuSettings, _menuPause, _menuReset });
            _menu.Items.Add(_menuGame);
            Controls.Add(_menu);
        }

        private void OpenSettingsDialog()
        {
            // Pause while editing
            bool prevPaused = _paused;
            _paused = true;

            using var dlg = new SettingsForm(_currentSettings);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
            {
                // Apply new settings
                _currentSettings = dlg.Result;
                _currentSettings.Normalize();

                _singlePlayer = _currentSettings.SinglePlayer;
                _maxScore = _currentSettings.MaxScore;
                _paddleSpeed = _currentSettings.PaddleSpeed;
                _ballSpeed = _currentSettings.BallSpeed;
                _maxBounceAngleDeg = _currentSettings.MaxBounceAngleDeg;
                _maxBallSpeed = _currentSettings.MaxBallSpeed;

                ClientSize = new Size(_currentSettings.WindowWidth, _currentSettings.WindowHeight);

                // Reset match with new parameters
                ResetMatch();
            }

            _paused = prevPaused;
        }

        private void ResetMatch()
        {
            _leftScore = _rightScore = 0;
            ResetPaddles();
            StartNewRound(serveToRight: _rng.Next(2) == 0);
            _paused = false;
        }

        // --- Init helpers ---
        private void ResetPaddles()
        {
            float margin = 30f;
            _leftPaddle = new RectangleF(
                margin,
                (ClientSize.Height - PaddleHeight) / 2f,
                PaddleWidth,
                PaddleHeight);

            _rightPaddle = new RectangleF(
                ClientSize.Width - margin - PaddleWidth,
                (ClientSize.Height - PaddleHeight) / 2f,
                PaddleWidth,
                PaddleHeight);
        }

        private void StartNewRound(bool serveToRight)
        {
            // Place ball at center
            _ball = new RectangleF(
                (ClientSize.Width - BallSize) / 2f,
                (ClientSize.Height - BallSize) / 2f,
                BallSize,
                BallSize);

            // Initial direction with random vertical variation
            float angleDeg = (float)(_rng.NextDouble() * 40 - 20); // -20..+20 degrees
            float angleRad = (float)(Math.PI / 180.0 * angleDeg);

            float speed = _ballSpeed;
            float vx = (float)(Math.Cos(angleRad) * speed) * (serveToRight ? 1f : -1f);
            float vy = (float)(Math.Sin(angleRad) * speed);

            _ballVelocity = new PointF(vx, vy);
        }

        // --- Main loop ---
        private void GameLoop(object? sender, EventArgs e)
        {
            if (_paused) { Invalidate(); return; }

            UpdatePaddles();
            UpdateBall();
            Invalidate();
        }

        // --- Input handling ---
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Left player: W/S
            if (e.KeyCode == Keys.W) _wPressed = true;
            if (e.KeyCode == Keys.S) _sPressed = true;

            // Right player: Up/Down arrows
            if (e.KeyCode == Keys.Up) _upPressed = true;
            if (e.KeyCode == Keys.Down) _downPressed = true;

            // Pause/resume
            if (e.KeyCode == Keys.Space) _paused = !_paused;

            // Toggle 1P / 2P mode (quick toggle)
            if (e.KeyCode == Keys.Tab) _singlePlayer = !_singlePlayer;

            // Reset match
            if (e.KeyCode == Keys.Enter) ResetMatch();
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W) _wPressed = false;
            if (e.KeyCode == Keys.S) _sPressed = false;
            if (e.KeyCode == Keys.Up) _upPressed = false;
            if (e.KeyCode == Keys.Down) _downPressed = false;
        }

        // --- Game update ---
        private void UpdatePaddles()
        {
            // Left paddle (human)
            float dyLeft = 0f;
            if (_wPressed) dyLeft -= _paddleSpeed;
            if (_sPressed) dyLeft += _paddleSpeed;
            MovePaddle(ref _leftPaddle, dyLeft);

            // Right paddle: AI or human
            float dyRight = 0f;
            if (_singlePlayer)
            {
                // Simple AI: follow ball with slight smoothing
                float paddleCenter = _rightPaddle.Top + _rightPaddle.Height / 2f;
                float ballCenter = _ball.Top + _ball.Height / 2f;
                float diff = ballCenter - paddleCenter;

                // Limit AI reaction speed to make it beatable
                float maxStep = _paddleSpeed * 0.9f;
                dyRight = Math.Clamp(diff * 0.12f, -maxStep, maxStep);
            }
            else
            {
                if (_upPressed) dyRight -= _paddleSpeed;
                if (_downPressed) dyRight += _paddleSpeed;
            }
            MovePaddle(ref _rightPaddle, dyRight);
        }

        private void MovePaddle(ref RectangleF paddle, float dy)
        {
            paddle.Y += dy;
            if (paddle.Y < 0) paddle.Y = 0;
            if (paddle.Bottom > ClientSize.Height) paddle.Y = ClientSize.Height - paddle.Height;
        }

        private void UpdateBall()
        {
            // Move
            _ball.X += _ballVelocity.X;
            _ball.Y += _ballVelocity.Y;

            // Top/bottom walls
            if (_ball.Top <= 0)
            {
                _ball.Y = 0;
                _ballVelocity.Y = -_ballVelocity.Y;
            }
            else if (_ball.Bottom >= ClientSize.Height)
            {
                _ball.Y = ClientSize.Height - _ball.Height;
                _ballVelocity.Y = -_ballVelocity.Y;
            }

            // Paddle collisions
            if (_ball.IntersectsWith(_leftPaddle))
            {
                ResolvePaddleBounce(_leftPaddle, isLeftPaddle: true);
            }
            else if (_ball.IntersectsWith(_rightPaddle))
            {
                ResolvePaddleBounce(_rightPaddle, isLeftPaddle: false);
            }

            // Scoring (left/right walls)
            if (_ball.Right < 0)
            {
                _rightScore++;
                CheckWinOrServe(serveToRight: false); // serve back to left
            }
            else if (_ball.Left > ClientSize.Width)
            {
                _leftScore++;
                CheckWinOrServe(serveToRight: true); // serve back to right
            }
        }

        private void ResolvePaddleBounce(RectangleF paddle, bool isLeftPaddle)
        {
            // Put ball just outside paddle to prevent sticking
            if (isLeftPaddle)
                _ball.X = paddle.Right;
            else
                _ball.X = paddle.Left - _ball.Width;

            // Compute contact point relative to paddle center (-1..+1)
            float paddleCenterY = paddle.Top + paddle.Height / 2f;
            float ballCenterY = _ball.Top + _ball.Height / 2f;
            float relative = (ballCenterY - paddleCenterY) / (paddle.Height / 2f);
            relative = Math.Clamp(relative, -1f, 1f);

            // Convert to bounce angle
            float angleDeg = relative * _maxBounceAngleDeg;
            float angleRad = (float)(Math.PI / 180.0 * angleDeg);

            // Slightly accelerate the ball after each paddle hit
            float speed = Length(_ballVelocity) * 1.03f;
            speed = Math.Min(speed, _maxBallSpeed); // clamp by setting

            // For left paddle, ball must go to the right; for right paddle, to the left.
            float dirX = isLeftPaddle ? 1f : -1f;
            _ballVelocity = new PointF(
                (float)(Math.Cos(angleRad) * speed) * dirX,
                (float)(Math.Sin(angleRad) * speed)
            );
        }

        private void CheckWinOrServe(bool serveToRight)
        {
            // Check if someone reached MaxScore
            if (_leftScore >= _maxScore || _rightScore >= _maxScore)
            {
                _paused = true;
                return;
            }

            // Reset paddles and start a new rally
            ResetPaddles();
            StartNewRound(serveToRight);
        }

        private static float Length(PointF v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);

        // --- Rendering ---
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; // crisp retro look

            // Background is already black; draw middle dashed line
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 4))
            {
                pen.DashPattern = new float[] { 6, 10 };
                g.DrawLine(pen, ClientSize.Width / 2f, 0, ClientSize.Width / 2f, ClientSize.Height);
            }

            // Draw paddles & ball
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, _leftPaddle);
                g.FillRectangle(white, _rightPaddle);
                g.FillEllipse(white, _ball);
            }

            // Draw score
            using (var scoreFont = new Font("Segoe UI", 28, FontStyle.Bold))
            using (var gray = new SolidBrush(Color.FromArgb(230, 230, 230)))
            {
                string left = _leftScore.ToString();
                string right = _rightScore.ToString();
                var leftSize = g.MeasureString(left, scoreFont);
                var rightSize = g.MeasureString(right, scoreFont);

                g.DrawString(left, scoreFont, gray,
                    ClientSize.Width / 2f - 40 - leftSize.Width, 20f);
                g.DrawString(right, scoreFont, gray,
                    ClientSize.Width / 2f + 40, 20f);
            }

            // HUD / instructions
            using (var small = new Font("Segoe UI", 10, FontStyle.Regular))
            using (var hudBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (var winBrush = new SolidBrush(Color.FromArgb(255, 220, 90)))
            {
                string mode = _singlePlayer ? "1P (AI right)" : "2P";
                g.DrawString($"Mode: {mode}  |  Tab = toggle  |  Space = pause  |  Enter = reset  |  W/S & ↑/↓",
                    small, hudBrush, 16f, ClientSize.Height - 28f);

                if (_paused)
                {
                    using var big = new Font("Segoe UI", 22, FontStyle.Bold);
                    var text = (_leftScore >= _maxScore || _rightScore >= _maxScore)
                        ? $"Game over – {(_leftScore > _rightScore ? "Left" : "Right")} wins!  Press Enter to reset."
                        : "Paused – press Space to resume.";
                    var size = g.MeasureString(text, big);
                    g.DrawString(text, big, winBrush,
                        (ClientSize.Width - size.Width) / 2f,
                        (ClientSize.Height - size.Height) / 2f); //dit is een voorbeeld
                }
            }
        }
    }
}

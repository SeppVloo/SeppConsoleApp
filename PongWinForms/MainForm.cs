
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace PongWinForms
{
    // --- Power-ups ---
    // Add this to your power-up enum
    public enum PowerUpType
    {
        EnlargeSelf,
        ShrinkOpponent,
        BallSpeedBoost,
        InvertOpponentControls   // NEW
    }

    public class MainForm : Form
    {
        private bool _networkInitialized = false;

        // --- Rendering & Timing ---
        private readonly Timer _timer = new Timer();
        private const int FPS = 60;

        // --- Game objects ---
        private RectangleF _leftPaddle;
        private RectangleF _rightPaddle;
        private RectangleF _ball;

        // --- Sizes & speeds (from settings) ---
        private float _paddleWidth = 12f;
        private float _paddleHeight;                 // from settings
        private float _ballSize = 14f;

        private float _paddleSpeed;
        private float _ballSpeed;                    // base speed
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


        private class PowerUp
        {
            public RectangleF Rect;
            public PowerUpType Type;
            public Color Color;
        }
        private PowerUp? _spawnedPowerUp = null;
        private readonly List<(PowerUpType type, bool forLeft, int framesLeft)> _activeEffects = new();
        private int _framesUntilNextPowerUp = 0;
        private int _powerUpDurationFrames = 0;
        private int _powerUpIntervalFrames = 0;
        private float _leftPaddleBuffFactor = 1f;
        private float _rightPaddleBuffFactor = 1f;
        private float _ballSpeedBoostFactor = 1f;
        private bool _lastHitLeft = true;

        // --- Sound ---
        private SoundManager _sound;

        // --- Random ---
        private readonly Random _rng = new Random();

        // --- Menu ---
        private MenuStrip _menu;
        private ToolStripMenuItem _menuGame;
        private ToolStripMenuItem _menuSettings;
        private ToolStripMenuItem _menuPause;
        private ToolStripMenuItem _menuReset;
        private ToolStripMenuItem _menuFullscreen;

        // --- Settings persistence ---
        private GameSettings _currentSettings;

        // --- Fullscreen support ---
        private bool _isFullscreen = false;
        private Rectangle _windowedBounds;
        private FormBorderStyle _windowedBorderStyle;
        private bool _windowedTopMost;

        // --- Networking ---
        private NetRole _netRole = NetRole.Offline;
        private PongNet? _net;
        private volatile bool _remoteUp, _remoteDown;       // client input consumed by host
        private StateMsg? _latestState;    // latest state snapshot on client
        private int _netTick = 0;                           // simple input tick
        private int _netSendDiv = 2;                        // host: send state every 2nd frame (~30 Hz)
        private bool _netStarted;


        // --- Controls inversion state (host authoritative; client mirrors via StateMsg) ---
        private bool _leftControlsInverted = false;
        private bool _rightControlsInverted = false;

        // Keep your paddle scale fields if you already have them:
        private float _leftPaddleScale = 1f;
        private float _rightPaddleScale = 1f;
        private float _normalPaddleHeight; // set from _paddleHeight at init

        // --- Power-up event sequencing for clients to play one-shot SFX exactly once ---
        private int _powerEventSeq = 0;          // host increments on each pickup
        private int _clientLastSeenPowerEventSeq = -1; // client tracks last seen
        private PowerUpType _lastSpawnedOrPickedType;


        public MainForm(GameSettings settings)
        {
            _currentSettings = settings ?? new GameSettings();
            _currentSettings.Normalize();

            // Apply settings
            _singlePlayer = _currentSettings.SinglePlayer;
            _maxScore = _currentSettings.MaxScore;
            _paddleSpeed = _currentSettings.PaddleSpeed;
            _ballSpeed = _currentSettings.BallSpeed;
            _maxBounceAngleDeg = _currentSettings.MaxBounceAngleDeg;
            _maxBallSpeed = _currentSettings.MaxBallSpeed;
            _paddleHeight = _currentSettings.PaddleHeight;
            _normalPaddleHeight = _paddleHeight; // base height used for scaling

            // Power-ups config
            _powerUpDurationFrames = _currentSettings.PowerUpDurationSec * FPS;
            _powerUpIntervalFrames = _currentSettings.PowerUpSpawnIntervalSec * FPS;
            _framesUntilNextPowerUp = _powerUpIntervalFrames;

            // Window styling & DPI
            Text = "Pong – WinForms";
            BackColor = Color.Black;
            AutoScaleMode = AutoScaleMode.Dpi; // High-DPI scaling
            ClientSize = new Size(_currentSettings.WindowWidth, _currentSettings.WindowHeight);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Double buffering
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            UpdateStyles();

            // Menu
            CreateMenu();

            // Timer
            _timer.Interval = 1000 / FPS;
            _timer.Tick += GameLoop;

            // Keyboard
            KeyPreview = true;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;

            // Sound
            _sound = new SoundManager(_currentSettings.SoundEnabled);

            // Determine network role from settings
            _netRole = _currentSettings.NetworkMode switch
            {
                NetMode.Host => NetRole.Host,
                NetMode.Client => NetRole.Client,
                _ => NetRole.Offline
            };

            // If networked, force two-player (host authoritative, no AI)
            if (_netRole != NetRole.Offline) _singlePlayer = false;

            if (_netRole != NetRole.Offline)
            {
                string hostIp = _currentSettings.HostIp;
                int port = _currentSettings.NetPort;

                _net = new PongNet(_netRole, hostIp, port);
                _net.OnInput += m => { _remoteUp = m.Up; _remoteDown = m.Down; };
                _net.OnState += s => { _latestState = s; };
                _net.OnInfo += msg => SafeUI(() => Text = $"Pong – {msg}");

                _net.OnError += err => SafeUI(() => MessageBox.Show(err, "Network", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                _net.OnPeerDisconnected += () => SafeUI(() =>
                {
                    MessageBox.Show("Verbinding verbroken.", "Network", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _net?.Dispose();
                    _netRole = NetRole.Offline;
                    Text = "Pong – WinForms";
                });

                //_net.Start();

                // Host: show local IPv4 to share with client
                if (_netRole == NetRole.Host)
                {
                    var ips = PongNet.GetLocalIPv4();
                    if (ips.Length > 0)
                    {
                        SafeUI(() =>
                            MessageBox.Show($"Host actief op poort {port}\nJouw IP-adres(sen):\n - " + string.Join("\n - ", ips),
                                "Host info", MessageBoxButtons.OK, MessageBoxIcon.Information));
                    }
                }

            }

            // Init objects
            ResetPaddles();
            StartNewRound(serveToRight: _rng.Next(2) == 0);

            // Start fullscreen if requested
            if (_currentSettings.StartFullscreen) ToggleFullscreen(force: true);

            _timer.Start();
        }

        private void CreateMenu()
        {
            _menu = new MenuStrip { Dock = DockStyle.Top };
            _menuGame = new ToolStripMenuItem("Game");
            _menuSettings = new ToolStripMenuItem("Instellingen...");
            _menuPause = new ToolStripMenuItem("Pauze/Hervat") { };
            _menuReset = new ToolStripMenuItem("Reset") { };
            _menuFullscreen = new ToolStripMenuItem("Fullscreen (F11)") { };

            _menuSettings.Click += (s, e) => OpenSettingsDialog();
            _menuPause.Click += (s, e) => _paused = !_paused;
            _menuReset.Click += (s, e) => ResetMatch();
            _menuFullscreen.Click += (s, e) => ToggleFullscreen();

            _menuGame.DropDownItems.AddRange(new ToolStripItem[] { _menuSettings, _menuPause, _menuReset, _menuFullscreen });
            _menu.Items.Add(_menuGame);
            Controls.Add(_menu);
        }

        private void ToggleFullscreen(bool force = false)
        {
            if (!_isFullscreen || force)
            {
                _isFullscreen = true;
                _windowedBounds = Bounds;
                _windowedBorderStyle = FormBorderStyle;
                _windowedTopMost = TopMost;

                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
                TopMost = true;
            }
            else
            {
                _isFullscreen = false;
                TopMost = _windowedTopMost;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = _windowedBorderStyle;
                Bounds = _windowedBounds;
            }
        }

        private void OpenSettingsDialog()
        {
            bool prevPaused = _paused;
            _paused = true;


            using var dlg = new SettingsForm(_currentSettings);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
            {
                _currentSettings = dlg.Result;
                _currentSettings.Normalize();
                SettingsIO.Save(_currentSettings);

                // Apply
                _singlePlayer = _currentSettings.SinglePlayer;
                _maxScore = _currentSettings.MaxScore;
                _paddleSpeed = _currentSettings.PaddleSpeed;
                _ballSpeed = _currentSettings.BallSpeed;
                _maxBounceAngleDeg = _currentSettings.MaxBounceAngleDeg;
                _maxBallSpeed = _currentSettings.MaxBallSpeed;
                _paddleHeight = _currentSettings.PaddleHeight;

                _powerUpDurationFrames = _currentSettings.PowerUpDurationSec * FPS;
                _powerUpIntervalFrames = _currentSettings.PowerUpSpawnIntervalSec * FPS;
                _framesUntilNextPowerUp = _powerUpIntervalFrames;

                _sound?.Dispose();
                _sound = new SoundManager(_currentSettings.SoundEnabled);

                if (!_isFullscreen) // don't resize when fullscreen
                {
                    ClientSize = new Size(_currentSettings.WindowWidth, _currentSettings.WindowHeight);
                }
                if (_currentSettings.StartFullscreen != _isFullscreen)
                {
                    ToggleFullscreen(); // align with setting
                }

                ResetMatch();
            }

            _paused = prevPaused;
        }

        private void ResetMatch()
        {
            _leftScore = _rightScore = 0;
            _spawnedPowerUp = null;
            _activeEffects.Clear();
            _leftPaddleBuffFactor = 1f;
            _rightPaddleBuffFactor = 1f;
            _ballSpeedBoostFactor = 1f;
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
                (ClientSize.Height - _paddleHeight) / 2f,
                _paddleWidth,
                _paddleHeight);

            _rightPaddle = new RectangleF(
                ClientSize.Width - margin - _paddleWidth,
                (ClientSize.Height - _paddleHeight) / 2f,
                _paddleWidth,
                _paddleHeight);
        }

        private void StartNewRound(bool serveToRight)
        {
            // Center ball
            _ball = new RectangleF(
                (ClientSize.Width - _ballSize) / 2f,
                (ClientSize.Height - _ballSize) / 2f,
                _ballSize,
                _ballSize);

            // Initial direction with small random vertical angle
            float angleDeg = (float)(_rng.NextDouble() * 40 - 20);
            float angleRad = (float)(Math.PI / 180.0 * angleDeg);

            float speed = _ballSpeed * _ballSpeedBoostFactor;
            float vx = (float)(Math.Cos(angleRad) * speed) * (serveToRight ? 1f : -1f);
            float vy = (float)(Math.Sin(angleRad) * speed);

            _ballVelocity = new PointF(vx, vy);
        }

        // --- Main loop ---

        private void GameLoop(object? sender, EventArgs e)
        {
            if (_paused) { Invalidate(); return; }

            if (_netRole == NetRole.Client)
            {
                // Client: send input; render latest state from host
                if (_net?.Connected == true)
                    _net.SendInput(new InputMsg { Up = _upPressed, Down = _downPressed, Tick = _netTick++ });

                var s = _latestState;
                if (s.HasValue)
                {
                    var st = s.Value;
                    _ball.X = st.BallX; _ball.Y = st.BallY;
                    _ballVelocity = new System.Drawing.PointF(st.BallVX, st.BallVY);
                    _leftPaddle.Y = st.LeftY; _rightPaddle.Y = st.RightY;
                    _leftPaddle.Height = st.PaddleH; _rightPaddle.Height = st.PaddleH;
                    _leftScore = st.LeftScore; _rightScore = st.RightScore;
                    _paused = st.Paused;
                }

                Invalidate();
                return;
            }

            // Host or Offline: full simulation
            UpdatePaddles();
            UpdateBall();

            // Host: broadcast state at ~30 FPS
            if (_netRole == NetRole.Host && _net?.Connected == true)
            {
                if ((_netTick++ % _netSendDiv) == 0)
                {
                    _net.SendState(new StateMsg
                    {
                        BallX = _ball.X,
                        BallY = _ball.Y,
                        BallVX = _ballVelocity.X,
                        BallVY = _ballVelocity.Y,
                        LeftY = _leftPaddle.Y,
                        RightY = _rightPaddle.Y,
                        PaddleH = _leftPaddle.Height,
                        LeftScore = _leftScore,
                        RightScore = _rightScore,
                        Paused = _paused,


                        // NEW:
                        LeftPaddleScale = _leftPaddleScale,
                        RightPaddleScale = _rightPaddleScale,
                        LeftControlsInverted = _leftControlsInverted,
                        RightControlsInverted = _rightControlsInverted,

                        // Let client play correct pickup SFX once:
                        PowerEventSeq = _powerEventSeq,
                        PowerEventType = (byte)_lastSpawnedOrPickedType // <== set this when a pickup happens




                    });
                    
                    //// Play power-up SFX once per pickup:
                    //if (st.PowerEventSeq > _clientLastSeenPowerEventSeq)
                    //{
                    //    _clientLastSeenPowerEventSeq = st.PowerEventSeq;
                    //    var t = (PowerUpType)st.PowerEventType;
                    //    _sound.PlayPowerUp(t);
                    //}
                }
            }

            Invalidate();
        }


        // --- Input handling ---

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W) _wPressed = true;
            if (e.KeyCode == Keys.S) _sPressed = true;

            if (_netRole == NetRole.Client)
            {
                if (e.KeyCode == Keys.Up) _upPressed = true;
                if (e.KeyCode == Keys.Down) _downPressed = true;
            }
            else if (_netRole == NetRole.Offline)
            {
                if (e.KeyCode == Keys.Up) _upPressed = true;
                if (e.KeyCode == Keys.Down) _downPressed = true;
            }

            if (e.KeyCode == Keys.Space) _paused = !_paused;
            if (e.KeyCode == Keys.Enter) ResetMatch();
            if (e.KeyCode == Keys.F11) ToggleFullscreen();
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W) _wPressed = false;
            if (e.KeyCode == Keys.S) _sPressed = false;

            if (_netRole == NetRole.Client)
            {
                if (e.KeyCode == Keys.Up) _upPressed = false;
                if (e.KeyCode == Keys.Down) _downPressed = false;
            }
            else if (_netRole == NetRole.Offline)
            {
                if (e.KeyCode == Keys.Up) _upPressed = false;
                if (e.KeyCode == Keys.Down) _downPressed = false;
            }
        }


        // --- Game update ---
        private void UpdatePaddles()
        {
            // ----- LEFT paddle (altijd lokaal W/S) -----
            bool lUp = _wPressed, lDown = _sPressed;
            if (_leftControlsInverted) { (lUp, lDown) = (lDown, lUp); }

            float dyLeft = 0f;
            if (lUp) dyLeft -= _paddleSpeed;
            if (lDown) dyLeft += _paddleSpeed;

            MovePaddle(ref _leftPaddle, dyLeft, _leftPaddleScale);

            // ----- RIGHT paddle -----
            float dyRight = 0f;

            if (_netRole == NetRole.Host)
            {
                // In host mode gebruikt de rechter paddle de remote input
                bool rUp = _remoteUp, rDown = _remoteDown;
                if (_rightControlsInverted) { (rUp, rDown) = (rDown, rUp); }

                if (rUp) dyRight -= _paddleSpeed;
                if (rDown) dyRight += _paddleSpeed;
            }
            else if (_netRole == NetRole.Offline)
            {
                // Offline: AI of lokale 2-spelers
                if (_singlePlayer)
                {
                    float paddleCenter = _rightPaddle.Top + _rightPaddle.Height / 2f;
                    float ballCenter = _ball.Top + _ball.Height / 2f;
                    float diff = ballCenter - paddleCenter;
                    float maxStep = _paddleSpeed * 0.9f;
                    dyRight = Math.Clamp(diff * 0.12f, -maxStep, maxStep);
                }
                else
                {
                    bool rUp = _upPressed, rDown = _downPressed;
                    if (_rightControlsInverted) { (rUp, rDown) = (rDown, rUp); }

                    if (rUp) dyRight -= _paddleSpeed;
                    if (rDown) dyRight += _paddleSpeed;
                }
            }
            // Client: geen lokale physics; state komt van de host

            MovePaddle(ref _rightPaddle, dyRight, _rightPaddleScale);
        }

        private void MovePaddle(ref RectangleF paddle, float dy, float buffFactor)
        {
            // Base paddle height (unchanged by power-ups)
            float baseH = _paddleHeight;

            // Compute scaled height and clamp to safe bounds
            //  - lower bound avoids a 0 px paddle
            //  - upper bound avoids exceeding the field height
            float targetHeight = Math.Clamp(baseH * buffFactor, 20f, ClientSize.Height);

            // Keep the paddle centered vertically while changing its height
            float center = paddle.Top + paddle.Height / 2f;
            paddle.Height = targetHeight;
            paddle.Y = center - targetHeight / 2f;

            // Apply movement (dy already signed: negative = up, positive = down)
            paddle.Y += dy;

            // Clamp inside playfield
            paddle.Y = Math.Clamp(paddle.Y, 0f, ClientSize.Height - paddle.Height);
        }

        // Small helper for timed buffs/debuffs
        private async void ActivateTimedEffect(Action start, Action end, int durationMs)
        {
            start();
            await Task.Delay(durationMs);
            end();
        }

        private void ApplyPowerUpShrinkOpponent(bool pickedByLeft, int durationMs = 6000)
        {
            if (pickedByLeft)
                ActivateTimedEffect(() => _rightPaddleScale = 0.5f,
                    () => _rightPaddleScale = 1f,
                    durationMs);
            else
                ActivateTimedEffect(() => _leftPaddleScale = 0.5f,
                    () => _leftPaddleScale = 1f,
                    durationMs);
        }

        private void ApplyPowerUpInvertOpponent(bool pickedByLeft, int durationMs = 6000)
        {
            if (pickedByLeft)
                ActivateTimedEffect(() => _rightControlsInverted = true,
                    () => _rightControlsInverted = false,
                    durationMs);
            else
                ActivateTimedEffect(() => _leftControlsInverted = true,
                    () => _leftControlsInverted = false,
                    durationMs);
        }

        private void UpdateBall()
        {
            _ball.X += _ballVelocity.X;
            _ball.Y += _ballVelocity.Y;

            // Walls
            if (_ball.Top <= 0)
            {
                _ball.Y = 0;
                _ballVelocity.Y = -_ballVelocity.Y;
                // optional: wall sound
            }
            else if (_ball.Bottom >= ClientSize.Height)
            {
                _ball.Y = ClientSize.Height - _ball.Height;
                _ballVelocity.Y = -_ballVelocity.Y;
            }

            // Paddles
            if (_ball.IntersectsWith(_leftPaddle))
            {
                _lastHitLeft = true;
                ResolvePaddleBounce(_leftPaddle, isLeftPaddle: true);
                _sound.PlayHit();
            }
            else if (_ball.IntersectsWith(_rightPaddle))
            {
                _lastHitLeft = false;
                ResolvePaddleBounce(_rightPaddle, isLeftPaddle: false);
                _sound.PlayHit();
            }

            // Power-up pickup (ball intersects item)
            if (_spawnedPowerUp != null && _ball.IntersectsWith(_spawnedPowerUp.Rect))
            {
                // When ball intersects power-up:
                var puType = _spawnedPowerUp.Type;
                // ... clear item on field, etc.
                _lastSpawnedOrPickedType = puType; // field: private PowerUpType _lastSpawnedOrPickedType;
                ApplyPowerUp(puType, pickedByLeft: _lastHitLeft);
                // Host will increment _powerEventSeq inside ApplyPowerUp()
                _spawnedPowerUp = null;
                _framesUntilNextPowerUp = _powerUpIntervalFrames;
                _sound.PlayPowerUp(PowerUpType.BallSpeedBoost);
            }

            // Scoring
            if (_ball.Right < 0)
            {
                _rightScore++;
                _sound.PlayScore();
                CheckWinOrServe(serveToRight: false);
            }
            else if (_ball.Left > ClientSize.Width)
            {
                _leftScore++;
                _sound.PlayScore();
                CheckWinOrServe(serveToRight: true);
            }
        }

        private void ResolvePaddleBounce(RectangleF paddle, bool isLeftPaddle)
        {
            // Put the ball just outside the paddle to prevent sticking
            if (isLeftPaddle) _ball.X = paddle.Right; else _ball.X = paddle.Left - _ball.Width;

            // Where did we hit the paddle? (-1..+1 relative)
            float paddleCenterY = paddle.Top + paddle.Height / 2f;
            float ballCenterY = _ball.Top + _ball.Height / 2f;
            float relative = (ballCenterY - paddleCenterY) / (paddle.Height / 2f);
            relative = Math.Clamp(relative, -1f, 1f);

            // Convert to outgoing angle
            float angleDeg = relative * _maxBounceAngleDeg;
            float angleRad = (float)(Math.PI / 180.0 * angleDeg);

            // Slight acceleration after each hit (respect max ball speed + boosts)
            float speed = Length(_ballVelocity) * 1.03f;
            speed = Math.Min(speed, _maxBallSpeed) * _ballSpeedBoostFactor;
            speed = Math.Min(speed, _maxBallSpeed); // clamp again

            float dirX = isLeftPaddle ? 1f : -1f;
            _ballVelocity = new PointF(
                (float)(Math.Cos(angleRad) * speed) * dirX,
                (float)(Math.Sin(angleRad) * speed)
            );
        }

        private void CheckWinOrServe(bool serveToRight)
        {
            if (_leftScore >= _maxScore || _rightScore >= _maxScore)
            {
                _paused = true;
                return;
            }

            ResetPaddles();
            StartNewRound(serveToRight);
        }

        // --- Power-ups ---
        private void UpdatePowerUps()
        {
            if (!_currentSettings.PowerUpsEnabled) return;

            // Spawn logic
            if (_spawnedPowerUp == null)
            {
                _framesUntilNextPowerUp--;
                if (_framesUntilNextPowerUp <= 0)
                {
                    SpawnPowerUp();
                }
            }

            // Active effects countdown
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var (type, forLeft, framesLeft) = _activeEffects[i];
                framesLeft--;
                if (framesLeft <= 0)
                {
                    // effect ends
                    if (type == PowerUpType.EnlargeSelf)
                    {
                        if (forLeft) _leftPaddleBuffFactor = 1f;
                        else _rightPaddleBuffFactor = 1f;
                    }
                    else if (type == PowerUpType.BallSpeedBoost)
                    {
                        _ballSpeedBoostFactor = 1f;
                    }
                    _activeEffects.RemoveAt(i);
                }
                else
                {
                    _activeEffects[i] = (type, forLeft, framesLeft);
                }
            }
        }

        private void SpawnPowerUp()
        {
            // Randomly choose type
            PowerUpType type = (_rng.Next(2) == 0) ? PowerUpType.EnlargeSelf : PowerUpType.BallSpeedBoost;
            Color color = (type == PowerUpType.EnlargeSelf) ? Color.MediumSeaGreen : Color.Orange;

            float size = 18f;
            // Spawn near the middle region
            float x = ClientSize.Width * 0.35f + (float)_rng.NextDouble() * ClientSize.Width * 0.3f;
            float y = (float)_rng.NextDouble() * (ClientSize.Height - size);

            _spawnedPowerUp = new PowerUp
            {
                Rect = new RectangleF(x, y, size, size),
                Type = type,
                Color = color
            };
        }


        private void ApplyPowerUp(PowerUpType type, bool pickedByLeft)
        {
            const int durationMs = 6000; // or use your settings duration

            // Host: increment event seq so clients can play the correct SFX once
            void NotifyPickupForClients()
            {
                if (_netRole == NetRole.Host) _powerEventSeq++;
            }

            switch (type)
            {
                case PowerUpType.EnlargeSelf:
                    if (pickedByLeft)
                        ActivateTimedEffect(() => _leftPaddleScale = 1.5f,
                                            () => _leftPaddleScale = 1f,
                                            durationMs);
                    else
                        ActivateTimedEffect(() => _rightPaddleScale = 1.5f,
                                            () => _rightPaddleScale = 1f,
                                            durationMs);

                    _sound.PlayPowerUp(PowerUpType.EnlargeSelf);
                    NotifyPickupForClients();
                    break;

                case PowerUpType.ShrinkOpponent:
                    if (pickedByLeft)
                        ActivateTimedEffect(() => _rightPaddleScale = 0.5f,
                                            () => _rightPaddleScale = 1f,
                                            durationMs);
                    else
                        ActivateTimedEffect(() => _leftPaddleScale = 0.5f,
                                            () => _leftPaddleScale = 1f,
                                            durationMs);

                    _sound.PlayPowerUp(PowerUpType.ShrinkOpponent);
                    NotifyPickupForClients();
                    break;

                case PowerUpType.BallSpeedBoost:
                    ActivateTimedEffect(() => _ballSpeedBoostFactor = 1.3f,
                                        () => _ballSpeedBoostFactor = 1f,
                                        durationMs);

                    _sound.PlayPowerUp(PowerUpType.BallSpeedBoost);
                    NotifyPickupForClients();
                    break;

                case PowerUpType.InvertOpponentControls:   // NEW
                    if (pickedByLeft)
                        ActivateTimedEffect(() => _rightControlsInverted = true,
                                            () => _rightControlsInverted = false,
                                            durationMs);
                    else
                        ActivateTimedEffect(() => _leftControlsInverted = true,
                                            () => _leftControlsInverted = false,
                                            durationMs);

                    _sound.PlayPowerUp(PowerUpType.InvertOpponentControls);
                    NotifyPickupForClients();
                    break;
            }
        }


        private static float Length(PointF v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);

        // --- Rendering ---
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            // Middle dashed line
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 4)) { pen.DashPattern = new float[] { 6, 10 }; g.DrawLine(pen, ClientSize.Width / 2f, 0, ClientSize.Width / 2f, ClientSize.Height); }

            // Paddles & ball
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, _leftPaddle);
                g.FillRectangle(white, _rightPaddle);
                g.FillEllipse(white, _ball);
            }

            // Power-up draw
            if (_spawnedPowerUp != null)
            {
                using var b = new SolidBrush(_spawnedPowerUp.Color);
                g.FillEllipse(b, _spawnedPowerUp.Rect);
            }

            // Score
            using (var scoreFont = new Font("Segoe UI", 28, FontStyle.Bold))
            using (var gray = new SolidBrush(Color.FromArgb(230, 230, 230)))
            {
                string left = _leftScore.ToString();
                string right = _rightScore.ToString();
                var leftSize = g.MeasureString(left, scoreFont);
                g.DrawString(left, scoreFont, gray, ClientSize.Width / 2f - 40 - leftSize.Width, 20f);
                g.DrawString(right, scoreFont, gray, ClientSize.Width / 2f + 40, 20f);
            }

            // HUD
            using (var small = new Font("Segoe UI", 10, FontStyle.Regular))
            using (var hudBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (var winBrush = new SolidBrush(Color.FromArgb(255, 220, 90)))
            {
                string mode = _singlePlayer ? "1P (AI right)" : "2P";
                string power = _currentSettings.PowerUpsEnabled
                    ? $"Power-ups on (spawn ~{_currentSettings.PowerUpSpawnIntervalSec}s, dur {_currentSettings.PowerUpDurationSec}s)"
                    : "Power-ups off";
                g.DrawString($"Mode: {mode} | Tab=toggle | Space=pause | Enter=reset | F11=fullscreen | W/S & ↑/↓ | {power}",
                    small, hudBrush, 16f, ClientSize.Height - 28f);

                if (_paused)
                {
                    using var big = new Font("Segoe UI", 22, FontStyle.Bold);
                    var text = (_leftScore >= _maxScore || _rightScore >= _maxScore)
                        ? $"Game over – {(_leftScore > _rightScore ? "Left" : "Right")} wins! Press Enter to reset."
                        : "Paused – press Space to resume.";
                    var size = g.MeasureString(text, big);
                    g.DrawString(text, big, winBrush, (ClientSize.Width - size.Width) / 2f, (ClientSize.Height - size.Height) / 2f);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _sound?.Dispose();
            _net?.Dispose();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (!_networkInitialized)
            {
                _networkInitialized = true;
                InitNetworkingFromSettings();
            }
        }


        // Safely invoke UI actions even if the handle isn't created yet.
        // - If handle not created: defer the action to HandleCreated
        // - If invoke required: BeginInvoke
        // - Else: run inline
        private void SafeUI(Action ui)
        {
            if (IsDisposed) return;

            if (!IsHandleCreated)
            {
                void whenCreated(object? s, EventArgs e)
                {
                    try
                    {
                        HandleCreated -= whenCreated;
                        if (!IsDisposed) ui();
                    }
                    catch { /* ignore */ }
                }
                HandleCreated += whenCreated;
                return;
            }

            if (InvokeRequired)
            {
                try { BeginInvoke(ui); } catch { /* ignore */ }
            }
            else
            {
                ui();
            }
        }

        private void InitNetworkingFromSettings()
        {
            _netRole = _currentSettings.NetworkMode switch
            {
                NetMode.Host => NetRole.Host,
                NetMode.Client => NetRole.Client,
                _ => NetRole.Offline
            };

            // In network mode we force two-player (no AI on the right)
            if (_netRole != NetRole.Offline) _singlePlayer = false;

            if (_netRole == NetRole.Offline) return;

            string hostIp = _currentSettings.HostIp;
            int port = _currentSettings.NetPort;

            _net = new PongNet(_netRole, hostIp, port);
            _net.OnInput += m => { _remoteUp = m.Up; _remoteDown = m.Down; };
            _net.OnState += s => { _latestState = s; };
            _net.OnInfo += msg => SafeUI(() => Text = $"Pong – {msg}");
            _net.OnError += err => SafeUI(() => MessageBox.Show(err, "Network", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            _net.OnPeerDisconnected += () => SafeUI(() =>
            {
                MessageBox.Show("Verbinding verbroken.", "Network", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _net?.Dispose();
                _netRole = NetRole.Offline;
                Text = "Pong – WinForms";
            });

            _net.Start();

            if (_netRole == NetRole.Host)
            {
                var ips = PongNet.GetLocalIPv4();
                if (ips.Length > 0)
                {
                    SafeUI(() =>
                        MessageBox.Show($"Host actief op poort {port}\nJouw IP-adres(sen):\n - " + string.Join("\n - ", ips),
                            "Host info", MessageBoxButtons.OK, MessageBoxIcon.Information));
                }
            }
        }
    }

}

namespace PongWeb.Game;

/// <summary>Authoritative game simulation (runs on host or in practice mode).</summary>
public sealed class PongEngine
{
    private const float W = GameState.Width;
    private const float H = GameState.Height;
    private const float BasePaddleH = 90f;
    private const float PaddleSpeed = 600f;
    private const float StartSpeed = 340f;
    private const float MaxSpeed = 950f;
    private const float MaxBounceDeg = 55f;
    private const float SpeedUpPerHit = 1.05f;
    private const float SpawnInterval = 7f;
    private const float ServeDelay = 1f;

    public int MaxScore { get; set; } = 7;
    public bool AiRight { get; set; }

    /// <summary>Indices into <see cref="PowerUpRegistry.All"/> that may spawn. Empty = no power-ups.</summary>
    public IReadOnlyList<int> EnabledPowerUps { get; set; } = Enumerable.Range(0, PowerUpRegistry.All.Count).ToArray();

    /// <summary>If true, power-ups on the field, active effects and walls survive a goal.</summary>
    public bool KeepPowerUpsOnGoal { get; set; }

    public const int MaxPickups = 3;
    public const float WallWidth = 12f;
    public const float WallHeight = 110f;
    public const int WallHits = 3;
    private readonly List<WallInfo> _walls = new();
    private readonly List<PickupInfo> _pickups = new();
    private int _wallSeq;
    private float _prevBx, _prevBy;

    private readonly Random _rng = new();
    private readonly Modifiers _mods = new();
    private readonly List<(PowerUp Def, Side Picker, float Remaining)> _effects = new();

    private float _bx, _by, _vx, _vy;
    private readonly float[] _py = { H / 2, H / 2 };
    private readonly float[] _ph = { BasePaddleH, BasePaddleH };
    private readonly float?[] _target = new float?[2];
    private readonly int[] _score = new int[2];
    private Side _lastHit;
    private float _serveTimer;
    private int _winner = -1;

    private float _spawnTimer = SpawnInterval;
    private int _hitSeq, _scoreSeq, _powerSeq;
    private string _powerText = "";

    public PongEngine() => Reset();

    public void Reset()
    {
        _score[0] = _score[1] = 0;
        _effects.Clear();
        _mods.Reset();
        _winner = -1;
        _pickups.Clear();
        _walls.Clear();
        _spawnTimer = SpawnInterval;
        _py[0] = _py[1] = H / 2;
        Serve(toRight: _rng.Next(2) == 0);
    }

    public void SetTarget(Side side, float? y) => _target[(int)side] = y;

    public void Tick(float dt)
    {
        if (_winner >= 0) return;

        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var e = _effects[i];
            e.Remaining -= dt;
            if (e.Remaining <= 0) _effects.RemoveAt(i);
            else _effects[i] = e;
        }
        _mods.Reset();
        foreach (var e in _effects) e.Def.Modify(_mods, e.Picker);

        if (AiRight) _target[1] = AiTarget();
        UpdatePaddle(0, dt);
        UpdatePaddle(1, dt);

        if (_serveTimer > 0)
        {
            _serveTimer -= dt;
            return;
        }

        float f = _mods.BallSpeedFactor;
        _prevBx = _bx; _prevBy = _by;
        _bx += _vx * f * dt;
        _by += _vy * f * dt;

        if (_by < 0) { _by = 0; _vy = Math.Abs(_vy); }
        else if (_by + GameState.BallSize > H) { _by = H - GameState.BallSize; _vy = -Math.Abs(_vy); }

        for (int i = _walls.Count - 1; i >= 0; i--) HitWall(i);

        float lx = GameState.PaddleMargin;
        if (_vx < 0 && _bx <= lx + GameState.PaddleWidth && _bx + GameState.BallSize >= lx && OverlapsPaddle(0))
            Bounce(Side.Left);

        float rx = W - GameState.PaddleMargin - GameState.PaddleWidth;
        if (_vx > 0 && _bx + GameState.BallSize >= rx && _bx <= rx + GameState.PaddleWidth && OverlapsPaddle(1))
            Bounce(Side.Right);

        UpdatePowerUps(dt);

        if (_bx + GameState.BallSize < 0) Score(Side.Right);
        else if (_bx > W) Score(Side.Left);
    }

    public GameState Snapshot() => new()
    {
        BallX = _bx, BallY = _by,
        LeftY = _py[0], RightY = _py[1],
        LeftH = _ph[0], RightH = _ph[1],
        LeftInverted = _mods.Inverted[0], RightInverted = _mods.Inverted[1],
        BallSpeedFactor = _mods.BallSpeedFactor,
        LeftScore = _score[0], RightScore = _score[1], MaxScore = MaxScore,
        PowerUps = _pickups.ToArray(),
        Winner = _winner, Serving = _serveTimer > 0,
        HitSeq = _hitSeq, ScoreSeq = _scoreSeq, PowerSeq = _powerSeq,
        PowerText = _powerText,
        Walls = _walls.ToArray(), WallSeq = _wallSeq,
    };

    /// <summary>Places a wall in front of the picker's goal (one per side). Picking another one refreshes it.</summary>
    public void SpawnWall(Side picker)
    {
        _walls.RemoveAll(w => w.Side == (int)picker);
        _walls.Add(new WallInfo
        {
            Side = (int)picker,
            X = picker == Side.Left ? W * 0.22f : W * 0.78f - WallWidth,
            Y = (float)(40 + _rng.NextDouble() * (H - 80 - WallHeight)),
            H = WallHeight, Hp = WallHits, MaxHp = WallHits,
        });
    }

    // Swept test against the previous position, so a fast ball can never pass through.
    // Every hit bounces the ball and costs one hit point; the wall is removed only after that bounce.
    private void HitWall(int index)
    {
        var w = _walls[index];
        const float b = GameState.BallSize;
        bool hit = false;

        if (_vx > 0 && _prevBx + b <= w.X && _bx + b >= w.X)
        {
            float t = (w.X - (_prevBx + b)) / (_bx - _prevBx);
            float y = _prevBy + (_by - _prevBy) * t;
            if (y + b > w.Y && y < w.Y + w.H) { _bx = w.X - b; _by = y; _vx = -Math.Abs(_vx); hit = true; }
        }
        else if (_vx < 0 && _prevBx >= w.X + WallWidth && _bx <= w.X + WallWidth)
        {
            float t = (_prevBx - (w.X + WallWidth)) / (_prevBx - _bx);
            float y = _prevBy + (_by - _prevBy) * t;
            if (y + b > w.Y && y < w.Y + w.H) { _bx = w.X + WallWidth; _by = y; _vx = Math.Abs(_vx); hit = true; }
        }

        // Top/bottom edge or any remaining overlap: push out vertically.
        if (!hit && _bx + b > w.X && _bx < w.X + WallWidth && _by + b > w.Y && _by < w.Y + w.H)
        {
            if (_by + b / 2 < w.Y + w.H / 2) { _by = w.Y - b; _vy = -Math.Abs(_vy); }
            else { _by = w.Y + w.H; _vy = Math.Abs(_vy); }
            hit = true;
        }

        if (!hit) return;
        _hitSeq++;
        _wallSeq++;
        w.Hp--;
        if (w.Hp <= 0) _walls.RemoveAt(index);
        else _walls[index] = w;
    }

    private void Serve(bool toRight)
    {
        _bx = W / 2 - GameState.BallSize / 2;
        _by = H / 2 - GameState.BallSize / 2;
        float angle = (float)((_rng.NextDouble() * 2 - 1) * 25 * Math.PI / 180);
        float dir = toRight ? 1 : -1;
        _vx = MathF.Cos(angle) * StartSpeed * dir;
        _vy = MathF.Sin(angle) * StartSpeed;
        _serveTimer = ServeDelay;
        _lastHit = toRight ? Side.Left : Side.Right;
    }

    private bool OverlapsPaddle(int i)
    {
        float top = _py[i] - _ph[i] / 2, bottom = _py[i] + _ph[i] / 2;
        return _by + GameState.BallSize >= top && _by <= bottom;
    }

    private void Bounce(Side side)
    {
        int i = (int)side;
        float rel = Math.Clamp((_by + GameState.BallSize / 2 - _py[i]) / (_ph[i] / 2), -1f, 1f);
        float angle = rel * MaxBounceDeg * MathF.PI / 180f;
        float speed = MathF.Min(MathF.Sqrt(_vx * _vx + _vy * _vy) * SpeedUpPerHit, MaxSpeed);
        float dir = side == Side.Left ? 1 : -1;
        _vx = MathF.Cos(angle) * speed * dir;
        _vy = MathF.Sin(angle) * speed;
        _bx = side == Side.Left
            ? GameState.PaddleMargin + GameState.PaddleWidth
            : W - GameState.PaddleMargin - GameState.PaddleWidth - GameState.BallSize;
        _lastHit = side;
        _hitSeq++;
    }

    private void UpdatePaddle(int i, float dt)
    {
        float h = Math.Clamp(BasePaddleH * _mods.PaddleScale[i], 30f, H * 0.9f);
        _ph[i] = h;
        if (_target[i] is float y)
        {
            if (_mods.Inverted[i]) y = H - y;
            float speed = PaddleSpeed * (AiRight && i == 1 ? 0.7f : 1f);
            float max = speed * dt;
            _py[i] += Math.Clamp(y - _py[i], -max, max);
        }
        _py[i] = Math.Clamp(_py[i], h / 2, H - h / 2);
    }

    private float AiTarget() =>
        _vx > 0 ? _by + GameState.BallSize / 2 : H / 2;

    private void UpdatePowerUps(float dt)
    {
        if (_pickups.Count < MaxPickups && EnabledPowerUps.Count > 0)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0)
            {
                _spawnTimer = SpawnInterval;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    float x = (float)(W * 0.3 + _rng.NextDouble() * (W * 0.4 - GameState.PowerUpSize));
                    float y = (float)(40 + _rng.NextDouble() * (H - 80 - GameState.PowerUpSize));
                    if (_pickups.Any(p => MathF.Abs(p.X - x) < 50 && MathF.Abs(p.Y - y) < 50)) continue;
                    _pickups.Add(new PickupInfo { X = x, Y = y, Type = EnabledPowerUps[_rng.Next(EnabledPowerUps.Count)] });
                    break;
                }
            }
        }

        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            var p = _pickups[i];
            bool hit = _bx < p.X + GameState.PowerUpSize && _bx + GameState.BallSize > p.X &&
                       _by < p.Y + GameState.PowerUpSize && _by + GameState.BallSize > p.Y;
            if (!hit) continue;

            _pickups.RemoveAt(i);
            var def = PowerUpRegistry.All[p.Type];
            if (def.DurationSec > 0) _effects.Add((def, _lastHit, def.DurationSec));
            def.Activate(this, _lastHit);
            _powerSeq++;
            _powerText = $"{(_lastHit == Side.Left ? "Links" : "Rechts")}: {def.Name}";
        }
    }

    private void Score(Side scorer)
    {
        _score[(int)scorer]++;
        _scoreSeq++;
        if (!KeepPowerUpsOnGoal)
        {
            _effects.Clear();
            _pickups.Clear();
            _walls.Clear();
            _spawnTimer = SpawnInterval;
        }

        if (_score[(int)scorer] >= MaxScore)
        {
            _winner = (int)scorer;
            return;
        }
        Serve(toRight: scorer == Side.Left);
    }
}

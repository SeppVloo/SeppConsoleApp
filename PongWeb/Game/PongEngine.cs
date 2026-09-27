namespace PongWeb.Game;

/// <summary>Authoritative game simulation (runs on host or in practice mode).</summary>
/// <remarks>
/// Player slots: team = slot % 2 (0 = left, 1 = right), lane = slot / 2.
/// 1v1 uses slots 0 and 1; 2v2 uses slots 0..3 where each team splits the height in a top and bottom lane.
/// </remarks>
public sealed class PongEngine
{
    private const float W = GameState.Width;
    private const float H = GameState.Height;
    private const float PaddleSpeed = 600f;
    private const float StartSpeed = 340f;
    private const float MaxSpeed = 950f;
    private const float MaxBounceDeg = 55f;
    private const float SpeedUpPerHit = 1.05f;
    private const float SpawnInterval = 7f;
    private const float ServeDelay = 1f;
    public const int MaxSlots = 4;

    public int MaxScore { get; set; } = 7;

    /// <summary>1 = 1 tegen 1, 2 = 2 tegen 2.</summary>
    public int TeamSize { get; set; } = 1;

    /// <summary>Which slots are played by the computer.</summary>
    public bool[] Ai { get; } = new bool[MaxSlots];

    public string[] TeamNames { get; set; } = ["Links", "Rechts"];

    /// <summary>Indices into <see cref="PowerUpRegistry.All"/> that may spawn. Empty = no power-ups.</summary>
    public IReadOnlyList<int> EnabledPowerUps { get; set; } = Enumerable.Range(0, PowerUpRegistry.All.Count).ToArray();

    /// <summary>If true, power-ups on the field, active effects and walls survive a goal.</summary>
    public bool KeepPowerUpsOnGoal { get; set; }

    /// <summary>A new wall gets a random number of hit points between these values (inclusive).</summary>
    public int WallMinHits { get; set; } = 3;
    public int WallMaxHits { get; set; } = 3;

    public const int MaxPickups = 3;
    public const float WallWidth = 12f;
    public const float WallHeight = 110f;
    private readonly List<WallInfo> _walls = new();
    private readonly List<PickupInfo> _pickups = new();
    private int _wallSeq;
    private float _prevBx, _prevBy;

    private readonly Random _rng = new();
    private readonly Modifiers _mods = new();
    private readonly List<(PowerUp Def, Side Picker, float Remaining)> _effects = new();

    private float _bx, _by, _vx, _vy;
    private readonly float[] _py = new float[MaxSlots];
    private readonly float[] _ph = new float[MaxSlots];
    private readonly float?[] _target = new float?[MaxSlots];
    private readonly int[] _score = new int[2];
    private Side _lastHit;
    private float _serveTimer;
    private int _winner = -1;

    private float _spawnTimer = SpawnInterval;
    private int _hitSeq, _scoreSeq, _powerSeq;
    private string _powerText = "";

    public PongEngine() => Reset();

    private int PaddleCount => TeamSize * 2;
    private float BasePaddleH => TeamSize == 1 ? 90f : 70f;
    private float LaneTop(int p) => TeamSize == 1 ? 0 : p / 2 * (H / 2);
    private float LaneBottom(int p) => TeamSize == 1 ? H : LaneTop(p) + H / 2;
    private static float PaddleX(int p) => p % 2 == 0 ? GameState.PaddleMargin : W - GameState.PaddleMargin - GameState.PaddleWidth;

    public void Reset()
    {
        _score[0] = _score[1] = 0;
        _effects.Clear();
        _mods.Reset();
        _winner = -1;
        _pickups.Clear();
        _walls.Clear();
        _spawnTimer = SpawnInterval;
        for (int p = 0; p < MaxSlots; p++) _py[p] = (LaneTop(p) + LaneBottom(p)) / 2;
        Serve(toRight: _rng.Next(2) == 0);
    }

    public void SetTarget(int slot, float? y)
    {
        if (slot < 0 || slot >= MaxSlots) return;
        // In 2v2 the whole screen height maps onto the player's own lane.
        if (y is float v && TeamSize > 1) y = LaneTop(slot) + v / 2;
        _target[slot] = y;
    }

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

        for (int p = 0; p < PaddleCount; p++)
        {
            if (Ai[p]) _target[p] = AiTarget(p);
            UpdatePaddle(p, dt);
        }

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

        for (int p = 0; p < PaddleCount; p++)
        {
            float x = PaddleX(p);
            bool hit = p % 2 == 0
                ? _vx < 0 && _bx <= x + GameState.PaddleWidth && _bx + GameState.BallSize >= x
                : _vx > 0 && _bx + GameState.BallSize >= x && _bx <= x + GameState.PaddleWidth;
            if (hit && OverlapsPaddle(p)) { Bounce(p); break; }
        }

        UpdatePowerUps(dt);

        if (_bx + GameState.BallSize < 0) Score(Side.Right);
        else if (_bx > W) Score(Side.Left);
    }

    public GameState Snapshot()
    {
        var paddles = new PaddleInfo[PaddleCount];
        for (int p = 0; p < paddles.Length; p++)
            paddles[p] = new PaddleInfo { X = PaddleX(p), Y = _py[p], H = _ph[p], Team = p % 2, Inverted = _mods.Inverted[p % 2] };

        return new()
        {
            BallX = _bx, BallY = _by,
            Paddles = paddles, TeamSize = TeamSize,
            BallSpeedFactor = _mods.BallSpeedFactor,
            LeftScore = _score[0], RightScore = _score[1], MaxScore = MaxScore,
            PowerUps = _pickups.ToArray(),
            Winner = _winner, Serving = _serveTimer > 0,
            HitSeq = _hitSeq, ScoreSeq = _scoreSeq, PowerSeq = _powerSeq,
            PowerText = _powerText,
            Walls = _walls.ToArray(), WallSeq = _wallSeq,
        };
    }

    /// <summary>Places a wall in front of the picker's goal (one per side). Picking another one refreshes it.</summary>
    public void SpawnWall(Side picker)
    {
        int min = Math.Max(1, Math.Min(WallMinHits, WallMaxHits));
        int max = Math.Max(min, WallMaxHits);
        int hp = _rng.Next(min, max + 1);
        _walls.RemoveAll(w => w.Side == (int)picker);
        _walls.Add(new WallInfo
        {
            Side = (int)picker,
            X = picker == Side.Left ? W * 0.22f : W * 0.78f - WallWidth,
            Y = (float)(40 + _rng.NextDouble() * (H - 80 - WallHeight)),
            H = WallHeight, Hp = hp, MaxHp = hp,
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

    private bool OverlapsPaddle(int p)
    {
        float top = _py[p] - _ph[p] / 2, bottom = _py[p] + _ph[p] / 2;
        return _by + GameState.BallSize >= top && _by <= bottom;
    }

    private void Bounce(int p)
    {
        var side = (Side)(p % 2);
        float rel = Math.Clamp((_by + GameState.BallSize / 2 - _py[p]) / (_ph[p] / 2), -1f, 1f);
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

    private void UpdatePaddle(int p, float dt)
    {
        int team = p % 2;
        float top = LaneTop(p), bottom = LaneBottom(p);
        float h = Math.Clamp(BasePaddleH * _mods.PaddleScale[team], 30f, (bottom - top) * 0.9f);
        _ph[p] = h;
        if (_target[p] is float y)
        {
            if (_mods.Inverted[team]) y = top + bottom - y;
            float speed = PaddleSpeed * (Ai[p] ? 0.7f : 1f);
            float max = speed * dt;
            _py[p] += Math.Clamp(y - _py[p], -max, max);
        }
        _py[p] = Math.Clamp(_py[p], top + h / 2, bottom - h / 2);
    }

    private float AiTarget(int p)
    {
        bool incoming = p % 2 == 0 ? _vx < 0 : _vx > 0;
        return incoming ? _by + GameState.BallSize / 2 : (LaneTop(p) + LaneBottom(p)) / 2;
    }

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
            _powerText = $"{TeamNames[(int)_lastHit]}: {def.Name}";
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

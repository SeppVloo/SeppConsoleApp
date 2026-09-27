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

    /// <summary>A new wall gets a random height between these values (game units, field is 450 high).</summary>
    public float WallMinSize { get; set; } = WallHeight;
    public float WallMaxSize { get; set; } = WallHeight;

    public const int MaxPickups = 3;
    public const float WallWidth = 12f;
    public const float WallHeight = 110f;
    public const int MaxWallsPerSide = 3;
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

    // Teammate collisions: knockback velocity that fades out, and a short moment of reduced control.
    private readonly float[] _kick = new float[MaxSlots];
    private readonly float[] _stun = new float[MaxSlots];
    private const float BumpSpeed = 700f;
    private const float StunTime = 0.35f;
    public int BumpSeq { get; private set; }

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
        for (int p = 0; p < MaxSlots; p++) { _py[p] = (LaneTop(p) + LaneBottom(p)) / 2; _kick[p] = _stun[p] = 0; }
        Serve(toRight: _rng.Next(2) == 0);
    }

    public void SetTarget(int slot, float? y)
    {
        if (slot < 0 || slot >= MaxSlots) return;
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
        if (TeamSize > 1) { CollideTeammates(0, 2); CollideTeammates(1, 3); }

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
            Walls = _walls.ToArray(), WallSeq = _wallSeq, BumpSeq = BumpSeq,
        };
    }

    /// <summary>Places a wall in front of the picker's goal (one per side). Picking another one refreshes it.</summary>
    public void SpawnWall(Side picker)
    {
        int min = Math.Max(1, Math.Min(WallMinHits, WallMaxHits));
        int max = Math.Max(min, WallMaxHits);
        int hp = _rng.Next(min, max + 1);
        float lo = Math.Clamp(MathF.Min(WallMinSize, WallMaxSize), 20f, H - 80);
        float wh = Math.Clamp(lo + (float)_rng.NextDouble() * (MathF.Max(WallMinSize, WallMaxSize) - lo), lo, H - 80);

        // Max walls per side: the oldest one makes room for the new one.
        var own = _walls.Where(w => w.Side == (int)picker).ToList();
        if (own.Count >= MaxWallsPerSide) _walls.Remove(own[0]);

        // Spread walls over a few columns so they don't stack on top of each other.
        float x = 0, y = 0;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float col = W * (0.18f + 0.06f * _rng.Next(3));
            x = picker == Side.Left ? col : W - col - WallWidth;
            y = (float)(40 + _rng.NextDouble() * (H - 80 - wh));
            if (!_walls.Any(w => MathF.Abs(w.X - x) < WallWidth * 2 && y < w.Y + w.H && y + wh > w.Y)) break;
        }
        _walls.Add(new WallInfo { Side = (int)picker, X = x, Y = y, H = wh, Hp = hp, MaxHp = hp });
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
        float h = Math.Clamp(BasePaddleH * _mods.PaddleScale[team], 30f, H * (TeamSize == 1 ? 0.9f : 0.45f));
        _ph[p] = h;
        _stun[p] = MathF.Max(0, _stun[p] - dt);
        if (_target[p] is float y)
        {
            if (_mods.Inverted[team]) y = H - y;
            float speed = PaddleSpeed * (Ai[p] ? 0.7f : 1f) * (_stun[p] > 0 ? 0.3f : 1f);
            float max = speed * dt;
            _py[p] += Math.Clamp(y - _py[p], -max, max);
        }
        _py[p] += _kick[p] * dt;
        _kick[p] *= MathF.Pow(0.004f, dt);
        float lo = h / 2, hi = H - h / 2;
        if (_py[p] < lo) { _py[p] = lo; _kick[p] = MathF.Abs(_kick[p]) * 0.5f; }
        else if (_py[p] > hi) { _py[p] = hi; _kick[p] = -MathF.Abs(_kick[p]) * 0.5f; }
    }

    // Teammates are solid: overlapping paddles are pushed apart and both get knocked back.
    private void CollideTeammates(int a, int b)
    {
        float minDist = (_ph[a] + _ph[b]) / 2;
        float d = _py[b] - _py[a];
        if (MathF.Abs(d) >= minDist) return;
        float dir = d >= 0 ? 1 : -1;
        float push = (minDist - MathF.Abs(d)) / 2;
        _py[a] -= push * dir;
        _py[b] += push * dir;
        for (int k = 0; k < 2; k++)
        {
            int p = k == 0 ? a : b;
            float lo = _ph[p] / 2, hi = H - _ph[p] / 2;
            _py[p] = Math.Clamp(_py[p], lo, hi);
        }
        // Still overlapping after clamping at an edge: move the other one fully out.
        if (MathF.Abs(_py[b] - _py[a]) < minDist)
        {
            if (_py[a] <= _ph[a] / 2 + 0.01f || _py[a] >= H - _ph[a] / 2 - 0.01f) _py[b] = _py[a] + minDist * dir;
            else _py[a] = _py[b] - minDist * dir;
        }

        bool fresh = _stun[a] <= 0 && _stun[b] <= 0;
        _kick[a] = -dir * BumpSpeed;
        _kick[b] = dir * BumpSpeed;
        _stun[a] = _stun[b] = StunTime;
        if (fresh) BumpSeq++;
    }

    private float AiTarget(int p)
    {
        bool incoming = p % 2 == 0 ? _vx < 0 : _vx > 0;
        float home = (LaneTop(p) + LaneBottom(p)) / 2;
        if (!incoming) return home;
        float ball = _by + GameState.BallSize / 2;
        if (TeamSize > 1)
        {
            int mate = (p + 2) % 4;
            if (MathF.Abs(_py[mate] - ball) < MathF.Abs(_py[p] - ball)) return home;
        }
        return ball;
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

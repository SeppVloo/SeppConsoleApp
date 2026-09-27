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

    public const float WallWidth = 12f;
    public const float WallHeight = 110f;
    public const int WallHits = 3;
    private bool _hasWall;
    private float _wx, _wy;
    private int _wallHp;
    private int _wallSeq;

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

    private bool _hasPowerUp;
    private float _pux, _puy;
    private int _puType;
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
        _hasPowerUp = false;
        _hasWall = false;
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
        _bx += _vx * f * dt;
        _by += _vy * f * dt;

        if (_by < 0) { _by = 0; _vy = Math.Abs(_vy); }
        else if (_by + GameState.BallSize > H) { _by = H - GameState.BallSize; _vy = -Math.Abs(_vy); }

        if (_hasWall) HitWall();

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
        HasPowerUp = _hasPowerUp, PowerUpX = _pux, PowerUpY = _puy, PowerUpType = (byte)_puType,
        Winner = _winner, Serving = _serveTimer > 0,
        HitSeq = _hitSeq, ScoreSeq = _scoreSeq, PowerSeq = _powerSeq,
        PowerText = _powerText,
        HasWall = _hasWall, WallX = _wx, WallY = _wy, WallH = WallHeight,
        WallHp = _wallHp, WallMaxHp = WallHits, WallSeq = _wallSeq,
    };

    /// <summary>Places a wall in front of the picker's goal. Picking another one refreshes it.</summary>
    public void SpawnWall(Side picker)
    {
        _wx = picker == Side.Left ? W * 0.22f : W * 0.78f - WallWidth;
        _wy = (float)(40 + _rng.NextDouble() * (H - 80 - WallHeight));
        _wallHp = WallHits;
        _hasWall = true;
    }

    // Every contact always costs one hit point; at 0 the wall is gone. Ball is pushed out so it can't hit twice.
    private void HitWall()
    {
        const float b = GameState.BallSize;
        if (_bx + b <= _wx || _bx >= _wx + WallWidth || _by + b <= _wy || _by >= _wy + WallHeight) return;

        float penLeft = _bx + b - _wx, penRight = _wx + WallWidth - _bx;
        float penTop = _by + b - _wy, penBottom = _wy + WallHeight - _by;
        float minX = MathF.Min(penLeft, penRight), minY = MathF.Min(penTop, penBottom);
        if (minX <= minY)
        {
            if (penLeft < penRight) { _bx = _wx - b; _vx = -Math.Abs(_vx); }
            else { _bx = _wx + WallWidth; _vx = Math.Abs(_vx); }
        }
        else
        {
            if (penTop < penBottom) { _by = _wy - b; _vy = -Math.Abs(_vy); }
            else { _by = _wy + WallHeight; _vy = Math.Abs(_vy); }
        }

        _hitSeq++;
        _wallSeq++;
        if (--_wallHp <= 0) _hasWall = false;
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
        if (!_hasPowerUp)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0 && EnabledPowerUps.Count > 0)
            {
                _puType = EnabledPowerUps[_rng.Next(EnabledPowerUps.Count)];
                _pux = (float)(W * 0.3 + _rng.NextDouble() * (W * 0.4 - GameState.PowerUpSize));
                _puy = (float)(40 + _rng.NextDouble() * (H - 80 - GameState.PowerUpSize));
                _hasPowerUp = true;
            }
            return;
        }

        bool hit = _bx < _pux + GameState.PowerUpSize && _bx + GameState.BallSize > _pux &&
                   _by < _puy + GameState.PowerUpSize && _by + GameState.BallSize > _puy;
        if (!hit) return;

        var def = PowerUpRegistry.All[_puType];
        if (def.DurationSec > 0) _effects.Add((def, _lastHit, def.DurationSec));
        def.Activate(this, _lastHit);
        _hasPowerUp = false;
        _spawnTimer = SpawnInterval;
        _powerSeq++;
        _powerText = $"{(_lastHit == Side.Left ? "Links" : "Rechts")}: {def.Name}";
    }

    private void Score(Side scorer)
    {
        _score[(int)scorer]++;
        _scoreSeq++;
        _effects.Clear();
        _hasPowerUp = false;
        _hasWall = false;
        _spawnTimer = SpawnInterval;

        if (_score[(int)scorer] >= MaxScore)
        {
            _winner = (int)scorer;
            return;
        }
        Serve(toRight: scorer == Side.Left);
    }
}

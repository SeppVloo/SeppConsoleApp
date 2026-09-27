namespace PongWeb.Game;

/// <summary>Snapshot of the game, sent host → client and drawn by the canvas.</summary>
public sealed class GameState
{
    public const float Width = 800f;
    public const float Height = 450f;
    public const float PaddleWidth = 12f;
    public const float PaddleMargin = 20f;
    public const float BallSize = 14f;
    public const float PowerUpSize = 30f;

    public float BallX { get; set; }
    public float BallY { get; set; }
    public PaddleInfo[] Paddles { get; set; } = [];
    public int TeamSize { get; set; } = 1;
    public float BallSpeedFactor { get; set; } = 1f;
    public int LeftScore { get; set; }
    public int RightScore { get; set; }
    public int MaxScore { get; set; }
    public PickupInfo[] PowerUps { get; set; } = [];
    public int Winner { get; set; } = -1;
    public bool Serving { get; set; }
    public int HitSeq { get; set; }
    public int ScoreSeq { get; set; }
    public int PowerSeq { get; set; }
    public string PowerText { get; set; } = "";
    public WallInfo[] Walls { get; set; } = [];
    public int WallSeq { get; set; }
    public int BumpSeq { get; set; }
    public bool Paused { get; set; }
    public string WinnerName { get; set; } = "";
}

public struct PaddleInfo
{
    public float X { get; set; }
    public float Y { get; set; }
    public float H { get; set; }
    public int Team { get; set; }
    public bool Inverted { get; set; }
}

public struct PickupInfo
{
    public float X { get; set; }
    public float Y { get; set; }
    public int Type { get; set; }
}

public struct WallInfo
{
    public int Side { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float H { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    /// <summary>Rotation in radians (0 = upright).</summary>
    public float Angle { get; set; }
    public float Spin { get; set; }
}

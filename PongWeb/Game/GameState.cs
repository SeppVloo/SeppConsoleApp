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
    public float LeftY { get; set; }
    public float RightY { get; set; }
    public float LeftH { get; set; }
    public float RightH { get; set; }
    public bool LeftInverted { get; set; }
    public bool RightInverted { get; set; }
    public float BallSpeedFactor { get; set; } = 1f;
    public int LeftScore { get; set; }
    public int RightScore { get; set; }
    public int MaxScore { get; set; }
    public bool HasPowerUp { get; set; }
    public float PowerUpX { get; set; }
    public float PowerUpY { get; set; }
    public byte PowerUpType { get; set; }
    public int Winner { get; set; } = -1;
    public bool Serving { get; set; }
    public int HitSeq { get; set; }
    public int ScoreSeq { get; set; }
    public int PowerSeq { get; set; }
    public string PowerText { get; set; } = "";
    public bool HasWall { get; set; }
    public float WallX { get; set; }
    public float WallY { get; set; }
    public float WallH { get; set; }
    public int WallHp { get; set; }
    public int WallMaxHp { get; set; }
    public int WallSeq { get; set; }
}

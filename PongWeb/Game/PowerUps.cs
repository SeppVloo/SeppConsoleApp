namespace PongWeb.Game;

public enum Side : byte { Left = 0, Right = 1 }

public static class SideExtensions
{
    public static Side Opponent(this Side side) => side == Side.Left ? Side.Right : Side.Left;
}

/// <summary>
/// Modifiers are recomputed every tick from all active power-up effects,
/// so effects stack and end cleanly without manual "revert" logic.
/// </summary>
public sealed class Modifiers
{
    public float[] PaddleScale { get; } = { 1f, 1f };
    public bool[] Inverted { get; } = { false, false };
    public float BallSpeedFactor { get; set; } = 1f;

    public void Reset()
    {
        PaddleScale[0] = PaddleScale[1] = 1f;
        Inverted[0] = Inverted[1] = false;
        BallSpeedFactor = 1f;
    }
}

/// <summary>
/// Base class for power-ups. To add a new power-up: create a subclass and add it to <see cref="PowerUpRegistry.All"/>.
/// </summary>
public abstract class PowerUp
{
    public abstract string Name { get; }
    public abstract string Color { get; }
    public abstract string Symbol { get; }
    public virtual float DurationSec => 6f;

    public virtual void Modify(Modifiers mods, Side picker) { }

    /// <summary>One-shot action when picked up (e.g. spawn an object).</summary>
    public virtual void Activate(PongEngine engine, Side picker) { }
}

public sealed class WallPowerUp : PowerUp
{
    public override string Name => "Muurtje";
    public override string Color => "#38bdf8";
    public override string Symbol => "▮";
    public override float DurationSec => 0f;
    public override void Activate(PongEngine engine, Side picker) => engine.SpawnWall(picker);
}

public sealed class EnlargeSelfPowerUp : PowerUp
{
    public override string Name => "Groter batje";
    public override string Color => "#3cb371";
    public override string Symbol => "+";
    public override void Modify(Modifiers mods, Side picker) => mods.PaddleScale[(int)picker] *= 1.5f;
}

public sealed class ShrinkOpponentPowerUp : PowerUp
{
    public override string Name => "Tegenstander kleiner";
    public override string Color => "#e74c3c";
    public override string Symbol => "−";
    public override void Modify(Modifiers mods, Side picker) => mods.PaddleScale[(int)picker.Opponent()] *= 0.5f;
}

public sealed class BallSpeedBoostPowerUp : PowerUp
{
    public override string Name => "Snelle bal";
    public override string Color => "#ffa500";
    public override string Symbol => "»";
    public override float DurationSec => 5f;
    public override void Modify(Modifiers mods, Side picker) => mods.BallSpeedFactor *= 1.4f;
}

public sealed class InvertOpponentControlsPowerUp : PowerUp
{
    public override string Name => "Besturing omgedraaid";
    public override string Color => "#9b59b6";
    public override string Symbol => "⇅";
    public override void Modify(Modifiers mods, Side picker) => mods.Inverted[(int)picker.Opponent()] = true;
}

public static class PowerUpRegistry
{
    // The index in this list is the id sent over the network.
    public static IReadOnlyList<PowerUp> All { get; } =
    [
        new EnlargeSelfPowerUp(),
        new ShrinkOpponentPowerUp(),
        new BallSpeedBoostPowerUp(),
        new InvertOpponentControlsPowerUp(),
        new WallPowerUp(),
    ];
}

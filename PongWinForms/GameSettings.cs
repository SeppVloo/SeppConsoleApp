namespace PongWinForms
{
    public class GameSettings
    {
        public bool SinglePlayer { get; set; } = true; // right paddle AI
        public int MaxScore { get; set; } = 10;

        // Speeds
        public float PaddleSpeed { get; set; } = 8f;
        public float BallSpeed { get; set; } = 7f;
        public float MaxBounceAngleDeg { get; set; } = 60f;

        // Window size
        public int WindowWidth { get; set; } = 900;
        public int WindowHeight { get; set; } = 550;

        // Optional: cap max ball speed
        public float MaxBallSpeed { get; set; } = 18f;

        // Validate and clamp values to acceptable ranges
        public void Normalize()
        {
            if (MaxScore < 1) MaxScore = 1;
            if (MaxScore > 99) MaxScore = 99;

            if (PaddleSpeed < 2f) PaddleSpeed = 2f;
            if (PaddleSpeed > 30f) PaddleSpeed = 30f;

            if (BallSpeed < 2f) BallSpeed = 2f;
            if (BallSpeed > 30f) BallSpeed = 30f;

            if (MaxBounceAngleDeg < 10f) MaxBounceAngleDeg = 10f;
            if (MaxBounceAngleDeg > 85f) MaxBounceAngleDeg = 85f;

            if (MaxBallSpeed < BallSpeed) MaxBallSpeed = BallSpeed;
            if (MaxBallSpeed > 60f) MaxBallSpeed = 60f;

            if (WindowWidth < 600) WindowWidth = 600;
            if (WindowWidth > 1920) WindowWidth = 1920;

            if (WindowHeight < 400) WindowHeight = 400;
            if (WindowHeight > 1200) WindowHeight = 1200;
        }
    }
}
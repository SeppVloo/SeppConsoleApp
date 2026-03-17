namespace PongWinForms
{
    public class GameSettings
    {
        public bool SinglePlayer { get; set; } = true;      // right paddle AI
        public int MaxScore { get; set; } = 10;

        // Speeds & physics
        public float PaddleSpeed { get; set; } = 8f;
        public float BallSpeed { get; set; } = 7f;
        public float MaxBounceAngleDeg { get; set; } = 60f;
        public float MaxBallSpeed { get; set; } = 18f;
        public float PaddleHeight { get; set; } = 90f;       // NEW: adjustable paddle height

        // Window
        public int WindowWidth { get; set; } = 900;
        public int WindowHeight { get; set; } = 550;
        public bool StartFullscreen { get; set; } = false;   // NEW: start in fullscreen

        // Sound
        public bool SoundEnabled { get; set; } = true;       // NEW

        // Power-ups
        public bool PowerUpsEnabled { get; set; } = true;    // NEW
        public int PowerUpSpawnIntervalSec { get; set; } = 12; // NEW
        public int PowerUpDurationSec { get; set; } = 8;       // NEW

        
        public NetMode NetworkMode { get; set; } = NetMode.Offline;
        public string HostIp { get; set; } = "";   // used in Client mode
        public int NetPort { get; set; } = 51337;  // default LAN port

        public void Normalize()
        {

            // ... your existing clamps ...
            if (NetPort < 1024 || NetPort > 65535) NetPort = 51337;

            // Clamp common ranges to safe values
            if (MaxScore < 1) MaxScore = 1; if (MaxScore > 99) MaxScore = 99;

            PaddleSpeed = Math.Clamp(PaddleSpeed, 2f, 30f);
            BallSpeed = Math.Clamp(BallSpeed, 2f, 30f);
            MaxBounceAngleDeg = Math.Clamp(MaxBounceAngleDeg, 10f, 85f);
            MaxBallSpeed = Math.Clamp(MaxBallSpeed, BallSpeed, 60f);

            PaddleHeight = Math.Clamp(PaddleHeight, 40f, 200f);

            WindowWidth = Math.Clamp(WindowWidth, 600, 1920);
            WindowHeight = Math.Clamp(WindowHeight, 400, 1200);

            PowerUpSpawnIntervalSec = Math.Clamp(PowerUpSpawnIntervalSec, 5, 120);
            PowerUpDurationSec = Math.Clamp(PowerUpDurationSec, 3, 60);
        }
    }
    

    public enum NetMode { Offline, Host, Client }

}
namespace PongWinForms
{
    public static class SettingsIO
    {
        public static GameSettings Load()
        {
            var s = Properties.Settings.Default;
            var gs = new GameSettings
            {
                SinglePlayer = s.SinglePlayer,
                MaxScore = s.MaxScore,
                PaddleSpeed = s.PaddleSpeed,
                BallSpeed = s.BallSpeed,
                MaxBounceAngleDeg = s.MaxBounceAngleDeg,
                MaxBallSpeed = s.MaxBallSpeed,
                PaddleHeight = s.PaddleHeight,
                WindowWidth = s.WindowWidth,
                WindowHeight = s.WindowHeight,
                StartFullscreen = s.StartFullscreen,
                SoundEnabled = s.SoundEnabled,
                PowerUpsEnabled = s.PowerUpsEnabled,
                PowerUpSpawnIntervalSec = s.PowerUpSpawnIntervalSec,
                PowerUpDurationSec = s.PowerUpDurationSec,
                NetworkMode = Enum.TryParse<NetMode>(s.NetworkMode, out var nm) ? nm : NetMode.Offline,
                HostIp = s.HostIp,
                NetPort = s.NetPort
            };


            gs.Normalize();
            return gs;
        }

        public static void Save(GameSettings gs)
        {
            gs.Normalize();
            var s = Properties.Settings.Default;

            s.SinglePlayer = gs.SinglePlayer;
            s.MaxScore = gs.MaxScore;
            s.PaddleSpeed = gs.PaddleSpeed;
            s.BallSpeed = gs.BallSpeed;
            s.MaxBounceAngleDeg = gs.MaxBounceAngleDeg;
            s.MaxBallSpeed = gs.MaxBallSpeed;
            s.PaddleHeight = gs.PaddleHeight;
            s.WindowWidth = gs.WindowWidth;
            s.WindowHeight = gs.WindowHeight;
            s.StartFullscreen = gs.StartFullscreen;
            s.SoundEnabled = gs.SoundEnabled;
            s.PowerUpsEnabled = gs.PowerUpsEnabled;
            s.PowerUpSpawnIntervalSec = gs.PowerUpSpawnIntervalSec;
            s.PowerUpDurationSec = gs.PowerUpDurationSec;
            s.NetworkMode = gs.NetworkMode.ToString();
            s.HostIp = gs.HostIp ?? "";
            s.NetPort = gs.NetPort;

            s.Save();
        }
    }
}
using System;
using System.IO;
using System.Media;

namespace PongWinForms
{
    public class SoundManager : IDisposable
    {
        private readonly bool _enabled;

        private SoundPlayer? _hit;
        private SoundPlayer? _score;

        // Generic power
        private SoundPlayer? _power;

        // Specific per power-up (optional files)
        private SoundPlayer? _pEnlarge;
        private SoundPlayer? _pShrink;
        private SoundPlayer? _pSpeed;
        private SoundPlayer? _pInvert;

        public SoundManager(bool enabled)
        {
            _enabled = enabled;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assets  = Path.Combine(baseDir, "Assets");

                string Hit(string n)   => Path.Combine(assets, n);

                // Core
                TryLoad(Hit("hit.wav"),   ref _hit);
                TryLoad(Hit("score.wav"), ref _score);
                TryLoad(Hit("power.wav"), ref _power);   // generic fallback

                // Specific power-ups (if present)
                TryLoad(Hit("power_enlarge.wav"), ref _pEnlarge);
                TryLoad(Hit("power_shrink.wav"),  ref _pShrink);
                TryLoad(Hit("power_speed.wav"),   ref _pSpeed);
                TryLoad(Hit("power_invert.wav"),  ref _pInvert);
            }
            catch { /* ignore */ }
        }

        private static void TryLoad(string path, ref SoundPlayer? sp)
        {
            if (File.Exists(path))
            {
                sp = new SoundPlayer(path);
                sp.LoadAsync();
            }
        }

        public void PlayHit()
        {
            if (!_enabled) return;
            if (_hit != null) Try(() => _hit.Play());
            else SystemSounds.Asterisk.Play();
        }

        public void PlayScore()
        {
            if (!_enabled) return;
            if (_score != null) Try(() => _score.Play());
            else SystemSounds.Hand.Play();
        }

        public void PlayPowerUp(PowerUpType type)
        {
            if (!_enabled) return;

            SoundPlayer? sp = type switch
            {
                PowerUpType.EnlargeSelf           => _pEnlarge ?? _power,
                PowerUpType.ShrinkOpponent        => _pShrink  ?? _power,
                PowerUpType.BallSpeedBoost        => _pSpeed   ?? _power,
                PowerUpType.InvertOpponentControls=> _pInvert  ?? _power,
                _ => _power
            };

            if (sp != null) Try(() => sp.Play());
            else SystemSounds.Exclamation.Play();
        }

        private static void Try(Action a) { try { a(); } catch { } }

        public void Dispose()
        {
            _hit?.Dispose();
            _score?.Dispose();
            _power?.Dispose();
            _pEnlarge?.Dispose();
            _pShrink?.Dispose();
            _pSpeed?.Dispose();
            _pInvert?.Dispose();
        }
    }
}
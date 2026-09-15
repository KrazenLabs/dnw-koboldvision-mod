using System;
using System.Collections.Generic;
using DnWModLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KoboldVision
{
    public sealed class KoboldVisionMod : Mod
    {
        private const float VisibleSeconds = 4f;
        private const float FadeInSeconds = 0.25f;
        private const float FadeOutSeconds = 0.5f;
        private const float FirstScanWait = 0.3f;
        private const float Glow = 1f;
        private const float SpotCoverage = 0.9f;
        private const float XRay = 0.25f;
        private const float ScreenDim = 0.6f;
        private const float PulseHz = 0.8f;
        private const float PulseDepth = 0.15f;
        private const float PingStrength = 1.5f;
        private const float PingSeconds = 0.4f;

        private readonly HashSet<string> _reportedErrors = new HashSet<string>();
        private VisionSettings _settings;
        private DirtScanner _scanner;
        private DragonOverlay _overlay;
        private ScreenTint _tint;
        private bool _unavailable;
        private float _visibleUntil = -1f;
        private bool _showing;
        private bool _reportedScan;
        private bool _pingPending;
        private float _shownAt;
        private float _amount;
        private float _ping;

        public override void OnInitialize()
        {
            _settings = new VisionSettings(Config);
            Logger.Info("Initialized.");
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            DropOverlay();
            _visibleUntil = -1f;
            _showing = false;
            _pingPending = false;
            _amount = 0f;
            _ping = 0f;
            _tint?.ForgetScene();
        }

        public override void OnUpdate()
        {
            try
            {
                UpdateVision(Time.unscaledTime, Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Report(e, "Kobold Vision");
            }
        }

        // Late update to copy current pose
        public override void OnLateUpdate()
        {
            try
            {
                Draw(Time.unscaledTime);
            }
            catch (Exception e)
            {
                Report(e, "Drawing Kobold Vision");
            }
        }

        public override void OnApplicationQuit()
        {
            _scanner?.Dispose();
            _tint?.Dispose();
        }

        private void UpdateVision(float now, float deltaTime)
        {
            var body = Washing.FindDragonBody();
            ReadInput(now, body != null);
            if (_overlay != null && (_overlay.IsStale || _overlay.Body != body)) DropOverlay();

            bool show = now < _visibleUntil && body != null;
            if (show != _showing) Logger.Debug(show ? "Kobold Vision on." : "Kobold Vision off.");
            if (show && !_showing)
            {
                _shownAt = now;

                if (_amount <= 0f)
                {
                    _reportedScan = false;
                    _scanner?.Restart();
                }
            }
            _showing = show;

            if ((show || _amount > 0f) && body != null && EnsureOverlay(body)) _scanner.Scan(body, now);
            if (show && _scanner != null && _scanner.HasResult && !_reportedScan)
            {
                _reportedScan = true;
                Logger.Debug("Kobold Vision found " + _scanner.DirtTexels + " texels with dirt and " + _scanner.SoapTexels + " with soap left on " + body.name + ".");
            }

            bool ready = _scanner != null && (_scanner.HasResult || now - _shownAt > FirstScanWait);
            float target = show && ready ? 1f : 0f;
            if (target > 0f && _pingPending)
            {
                _pingPending = false;
                _ping = 1f;
            }
            _amount = Mathf.MoveTowards(_amount, target, deltaTime / (target > _amount ? FadeInSeconds : FadeOutSeconds));
            _ping = Mathf.MoveTowards(_ping, 0f, deltaTime / PingSeconds);
        }

        private void ReadInput(float now, bool dragonPresent)
        {
            if (!dragonPresent || _unavailable)
            {
                _visibleUntil = -1f;
                return;
            }
            if (!VisionInput.WasPressed(_settings.Key.Value, _settings.GamepadButton.Value) || !VisionInput.GameTakesInput()) return;
            _visibleUntil = now + VisibleSeconds;
            _pingPending = true;
        }

        private void Draw(float now)
        {
            float amount = _amount * _amount * (3f - 2f * _amount);
            float dim = amount * ScreenDim;
            _tint?.Apply(dim);
            if (_overlay == null) return;
            if (amount <= 0f)
            {
                _overlay.Hide();
                return;
            }
            float pulse = 1f + PulseDepth * Mathf.Sin(now * PulseHz * 2f * Mathf.PI);
            float ping = 1f + PingStrength * _ping * _ping;
            float glow = Glow * pulse * ping * Mathf.Pow(2f, ScreenTint.Darkening(dim));
            _overlay.Show(SpotCoverage * amount, glow * amount, XRay * amount);
        }

        private bool EnsureOverlay(SkinnedMeshRenderer body)
        {
            if (_overlay != null) return true;
            if (_scanner == null) _scanner = DirtScanner.Create();
            if (_scanner != null) _overlay = DragonOverlay.Create(body, _scanner);
            if (_overlay == null)
            {
                _unavailable = true;
                Logger.Warning("Kobold Vision can't work in this version of the game: the shaders it needs weren't found.");
                return false;
            }
            if (_tint == null) _tint = new ScreenTint();
            Logger.Debug("Kobold Vision is watching " + body.name + ".");
            return true;
        }

        private void DropOverlay()
        {
            if (_overlay == null) return;
            _overlay.Dispose();
            _overlay = null;
            _scanner?.Clear();
        }

        private void Report(Exception e, string what)
        {
            if (_reportedErrors.Add(what + ": " + e.GetType().FullName)) Logger.Exception(e, what + " failed");
        }
    }
}

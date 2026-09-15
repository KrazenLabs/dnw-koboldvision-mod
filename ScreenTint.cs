using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KoboldVision
{
    // Vignette
    internal sealed class ScreenTint : IDisposable
    {
        private const float Priority = 1100f;
        private const float DarkeningStops = 1f;
        private const float Saturation = -25f;
        private const float VignetteIntensity = 0.4f;
        private const float VignetteSmoothness = 0.45f;

        private readonly GameObject _root;
        private readonly VolumeProfile _profile;
        private readonly Volume _volume;
        private readonly ColorAdjustments _grading;
        private ColorAdjustments _gameGrading;
        private bool _searchedGameGrading;

        public ScreenTint()
        {
            _root = new GameObject("Kobold Vision Tint");
            Object.DontDestroyOnLoad(_root);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _grading = _profile.Add<ColorAdjustments>();
            _grading.postExposure.overrideState = true;
            _grading.saturation.overrideState = true;
            _grading.saturation.value = Saturation;
            var vignette = _profile.Add<Vignette>();
            vignette.intensity.overrideState = true;
            vignette.intensity.value = VignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = VignetteSmoothness;
            vignette.color.overrideState = true;
            vignette.color.value = Color.black;
            _volume = _root.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = Priority;
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
            _volume.enabled = false;
        }

        public static float Darkening(float weight)
        {
            return DarkeningStops * weight;
        }

        public void Apply(float weight)
        {
            _volume.enabled = weight > 0f;
            _volume.weight = weight;
            if (weight <= 0f) return;
            if (!_searchedGameGrading)
            {
                _gameGrading = FindGameGrading();
                _searchedGameGrading = true;
            }

            float gameExposure = _gameGrading != null && _gameGrading.postExposure.overrideState ? _gameGrading.postExposure.value : 0f;
            _grading.postExposure.value = gameExposure - DarkeningStops;
        }

        public void ForgetScene()
        {
            _gameGrading = null;
            _searchedGameGrading = false;
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            Object.Destroy(_profile);
        }

        private static ColorAdjustments FindGameGrading()
        {
            foreach (var listener in Object.FindObjectsByType<VolumeSettingListener>(FindObjectsSortMode.None))
            {
                var volume = listener.GetComponent<Volume>();
                if (volume == null) continue;
                var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile != null && profile.TryGet<ColorAdjustments>(out var grading)) return grading;
            }
            return null;
        }
    }
}

using System;
using System.Collections;
using DG.Tweening;
using System.Collections.Generic;
using GamePlay.Effects;
using UnityEngine;
using Pools;

namespace GamePlay.ComponentSystems
{
    public class EffectComponent : BaseComponent, IEffector
    {
        [Serializable]
        private class EffectEntry
        {
            public EffectType Type = EffectType.None;

            [Header("VFX")]
            public GameObject VfxPrefab;
            public bool ParentToTarget = true;

            [Header("SFX (Optional)")]
            public AudioClip SfxClip;
            public bool LoopSfx;
            [Range(0f, 1f)] public float SfxVolume = 1f;

            [Header("Timing")]
            [Tooltip("If > 0 then onComplete is invoked after this delay.")]
            public float WaitForAction = 0.5f;
        }

        [Header("Effects List (Serializable, Luna-safe)")]
        [SerializeField] private List<EffectEntry> effects = new List<EffectEntry>();

        private EffectEntry[] _runtime;
        private static readonly Dictionary<int, ParticleSystem[]> s_particleSystemsCache = new Dictionary<int, ParticleSystem[]>(128);
        private static readonly Dictionary<int, bool> s_uiVfxPrefabCache = new Dictionary<int, bool>(64);
        private bool _cacheBuilt;
        private EffectType _activeLoopingEffectType = EffectType.None;
        private AudioClip _activeLoopingClip;
        private readonly Dictionary<EffectType, float> _lastPlayTimes = new Dictionary<EffectType, float>();

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
#if UNITY_EDITOR
        [SerializeField] private bool warnIfNoAudioRouteInEditor = false;
        private bool _warnedMissingAudioRoute;
#endif

        protected override void Awake()
        {
            base.Awake();
            ResolveAudioSource(logIfMissingInEditor: false);
            BuildCache();
        }

        private void OnDisable()
        {
            StopActiveLoopingSfx();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            ResolveAudioSource(logIfMissingInEditor: true);
            _cacheBuilt = false;
            BuildCache();
        }
#endif

        public override void Initialize()
        {
            base.Initialize();
            if (audioSource == null)
                ResolveAudioSource(logIfMissingInEditor: false);

            if (!_cacheBuilt)
                BuildCache();
        }

        public override void Dispose()
        {
            base.Dispose();

            DOVirtual.DelayedCall(0.01f, () => { }).SetId(this).Kill();

            StopActiveLoopingSfx();
        }

        private void ResolveAudioSource(bool logIfMissingInEditor)
        {
            if (audioSource != null) return;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = GetComponentInChildren<AudioSource>(true);

#if UNITY_EDITOR
            if (!logIfMissingInEditor) return;
            if (Application.isPlaying) return;
            if (!warnIfNoAudioRouteInEditor) return;
            if (_warnedMissingAudioRoute) return;

            if (audioSource == null)
            {
                _warnedMissingAudioRoute = true;
                Debug.LogWarning($"[EffectComponent] {name} has no AudioSource for SFX playback.");
            }
#endif
        }

        private AudioSource ResolveOrCreateAudioSource()
        {
            if (audioSource != null)
            {
                return audioSource;
            }

            // Fallback: Thử tìm component AudioSource gắn trên object hoặc các child
            ResolveAudioSource(logIfMissingInEditor: false);
            if (audioSource != null)
            {
                return audioSource;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            return audioSource;
        }

        private void BuildCache()
        {
            if (_cacheBuilt)
                return;

            _runtime = new EffectEntry[32]; // Max enum size + buffer
            _cacheBuilt = true;
            if (effects == null) return;

            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null) continue;
                if (effect.Type == EffectType.None) continue;

                int index = (int)effect.Type;
                if (index >= 0 && index < _runtime.Length)
                {
                    _runtime[index] = effect;
                }
            }
        }

        public void PlayEffect(
            EffectType effectType,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null,
            float waitForAction = 0.5f,
            Action onComplete = null)
        {
            try
            {
                if (!_cacheBuilt)
                    BuildCache();

                if (_lastPlayTimes.TryGetValue(effectType, out float lastTime))
                {
                    if (Time.time - lastTime < 0.05f)
                    {
                        // Rapid fire block
                        if (onComplete != null)
                        {
                            if (waitForAction <= 0f) onComplete.Invoke();

                        }
                        return;
                    }
                }
                _lastPlayTimes[effectType] = Time.time;

                int typeIndex = (int)effectType;
                bool hasEntry = _runtime != null && typeIndex >= 0 && typeIndex < _runtime.Length && _runtime[typeIndex] != null;
                EffectEntry entry = hasEntry ? _runtime[typeIndex] : null;
                if (hasEntry)
                {
                    ExecuteEffect(effectType, entry, position, rotation, parent);

                    if (waitForAction <= 0f)
                        waitForAction = entry.WaitForAction;
                }

                if (!hasEntry && onComplete == null)
                {
                    return;
                }

                if (onComplete == null)
                {
                    return;
                }

                if (waitForAction <= 0f)
                {
                    onComplete?.Invoke();
                    return;
                }

                DOVirtual.DelayedCall(waitForAction, () => { onComplete?.Invoke(); }, false).SetId(this);
            }
            catch
            {
                // Keep playable flow safe.
            }
        }


        private static GameObject SafePoolGet(GameObject prefab)
        {
            if (prefab == null) return null;

            try
            {
                var obj = prefab.Spawn();
                return obj != null ? obj : Instantiate(prefab);
            }
            catch
            {
                return Instantiate(prefab);
            }
        }

        public void StopEffect(EffectType effectType)
        {
            if (effectType == EffectType.None)
            {
                return;
            }

            if (_activeLoopingEffectType != effectType)
            {
                return;
            }

            StopActiveLoopingSfx();
        }

        private void ExecuteEffect(EffectType effectType, EffectEntry entry, Vector3 position, Quaternion rotation, Transform parent)
        {
            PlayVfx(effectType, entry, position, rotation, parent);

            if (entry.SfxClip == null) return;

            if (entry.LoopSfx)
            {
                var loopAudioSource = ResolveOrCreateAudioSource();
                if (loopAudioSource == null)
                {
                    return;
                }

                if (_activeLoopingEffectType == effectType &&
                    _activeLoopingClip == entry.SfxClip &&
                    loopAudioSource.isPlaying &&
                    loopAudioSource.loop)
                {
                    return;
                }

                StopActiveLoopingSfx();
                loopAudioSource.clip = entry.SfxClip;
                loopAudioSource.loop = true;
                loopAudioSource.volume = Mathf.Clamp01(entry.SfxVolume);
                loopAudioSource.Play();
                _activeLoopingEffectType = effectType;
                _activeLoopingClip = entry.SfxClip;
                return;
            }

            float sfxVolume = Mathf.Clamp01(entry.SfxVolume);
            SoundManager.Instance?.PlayOneShot(entry.SfxClip, sfxVolume);
        }

        private void PlayVfx(EffectType effectType, EffectEntry entry, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (entry == null || entry.VfxPrefab == null)
            {
                return;
            }

            try
            {
                bool isUiVfx = IsUiVfxPrefab(entry.VfxPrefab);
                Transform targetParent = ResolveVfxParent(effectType, entry, parent, isUiVfx);
                GameObject vfx = SafePoolGet(entry.VfxPrefab);
                if (vfx == null)
                {
                    return;
                }

                vfx.transform.SetParent(targetParent, false);
                vfx.transform.position = position;
                vfx.transform.rotation = rotation;
                vfx.SetActive(true);

                var particles = GetCachedParticleSystems(vfx);
                if (particles == null || particles.Length == 0)
                {
                    return;
                }

                float lifeTime = GetParticleLifetime(vfx);
                PlayParticles(particles);

                if (lifeTime > 0f)
                {
                    DOVirtual.DelayedCall(Mathf.Max(0.05f, lifeTime), () =>
                    {
                        if (vfx != null) vfx.SetActive(false);
                    }, false).SetId(vfx);
                }
            }
            catch
            {
                // VFX setup is non-critical; keep SFX/gameplay flow alive.
            }
        }

        private Transform ResolveVfxParent(EffectType effectType, EffectEntry entry, Transform parent, bool isUiVfx)
        {
            if (entry.ParentToTarget)
            {
                // [FIX] Force Hit, Break, and Die VFX to world space so they don't disappear if target dies.
                if (effectType == EffectType.Hit || effectType == EffectType.Break || effectType == EffectType.Die)
                {
                    if (!isUiVfx)
                    {
                        return null; // Force world space
                    }
                }

                return parent != null ? parent : CacheTransform;
            }

            if (!isUiVfx)
            {
                return null;
            }

            return ResolveCanvasTransform(parent) ?? ResolveCanvasTransform(CacheTransform);
        }

        private static Transform ResolveCanvasTransform(Transform source)
        {
            if (source == null)
            {
                return null;
            }

            var canvas = source.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.transform : null;
        }

        private static bool IsUiVfxPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            int key = prefab.GetInstanceID();
            if (s_uiVfxPrefabCache.TryGetValue(key, out bool cached))
            {
                return cached;
            }

            cached = prefab.transform is RectTransform ||
                     prefab.GetComponentInChildren<CanvasRenderer>(true) != null;
            s_uiVfxPrefabCache[key] = cached;
            return cached;
        }

        private static void PlayParticles(ParticleSystem[] particles)
        {
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        private void StopActiveLoopingSfx()
        {
            if (audioSource != null)
            {
                if (_activeLoopingEffectType != EffectType.None &&
                    audioSource.isPlaying &&
                    audioSource.loop)
                {
                    audioSource.Stop();
                }

                if (audioSource.loop)
                {
                    audioSource.loop = false;
                }

                if (_activeLoopingClip != null && audioSource.clip == _activeLoopingClip)
                {
                    audioSource.clip = null;
                }
            }

            _activeLoopingEffectType = EffectType.None;
            _activeLoopingClip = null;
        }

        private static ParticleSystem[] GetCachedParticleSystems(GameObject vfxObject)
        {
            if (vfxObject == null) return null;

            int key = vfxObject.GetInstanceID();
            if (s_particleSystemsCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            cached = vfxObject.GetComponentsInChildren<ParticleSystem>(true);
            s_particleSystemsCache[key] = cached;
            return cached;
        }

        private static float GetParticleLifetime(GameObject vfxObject)
        {
            var particleSystems = GetCachedParticleSystems(vfxObject);
            if (particleSystems == null || particleSystems.Length == 0) return 0f;

            float maxLifetime = 0f;
            for (int i = 0; i < particleSystems.Length; i++)
            {
                var ps = particleSystems[i];
                if (ps == null) continue;

                var main = ps.main;
                float duration = main.duration;
                float startLifetime = 0f;

                switch (main.startLifetime.mode)
                {
                    case ParticleSystemCurveMode.Constant:
                        startLifetime = main.startLifetime.constant;
                        break;
                    case ParticleSystemCurveMode.TwoConstants:
                        startLifetime = main.startLifetime.constantMax;
                        break;
                    case ParticleSystemCurveMode.Curve:
                    case ParticleSystemCurveMode.TwoCurves:
                        startLifetime = main.startLifetime.curveMultiplier;
                        break;
                }

                float lifeTime = duration + startLifetime;
                if (lifeTime > maxLifetime)
                    maxLifetime = lifeTime;
            }

            return maxLifetime;
        }
    }
}

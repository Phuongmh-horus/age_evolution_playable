using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.Effects;
using UnityEngine;

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

            [Header("Timing")]
            [Tooltip("If > 0 then onComplete is invoked after this delay.")]
            public float WaitForAction = 0.5f;
        }

        [Header("Effects List (Serializable, Luna-safe)")]
        [SerializeField] private List<EffectEntry> effects = new List<EffectEntry>();

        private readonly Dictionary<EffectType, EffectEntry> _runtime = new Dictionary<EffectType, EffectEntry>();
        private static readonly Dictionary<int, TimedAutoDisable> s_timedAutoDisableCache = new Dictionary<int, TimedAutoDisable>(128);
        private static readonly Dictionary<int, ParticleSystem[]> s_particleSystemsCache = new Dictionary<int, ParticleSystem[]>(128);
        private Coroutine _waitRoutine;
        private bool _cacheBuilt;

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private bool useGlobalSoundManagerFallback = true;
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

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            ResolveAudioSource(logIfMissingInEditor: true);
            BuildCache();
        }
#endif

        public override void Initialize()
        {
            base.Initialize();
            ResolveAudioSource(logIfMissingInEditor: false);
            BuildCache();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (_waitRoutine != null)
            {
                StopCoroutine(_waitRoutine);
                _waitRoutine = null;
            }

            _runtime.Clear();
            _cacheBuilt = false;
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

            if (audioSource == null && !useGlobalSoundManagerFallback)
            {
                _warnedMissingAudioRoute = true;
                Debug.LogWarning($"[EffectComponent] {name} has no AudioSource and global fallback is disabled.");
            }
#endif
        }

        private void BuildCache()
        {
            _runtime.Clear();
            _cacheBuilt = true;
            if (effects == null) return;

            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null) continue;
                if (effect.Type == EffectType.None) continue;

                _runtime[effect.Type] = effect;
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

                bool hasEntry = _runtime.TryGetValue(effectType, out var entry) && entry != null;
                if (hasEntry)
                {
                    ExecuteEffect(entry, position, rotation, parent);

                    if (waitForAction <= 0f)
                        waitForAction = entry.WaitForAction;
                }
                else if (onComplete == null)
                {
                    // No effect and no callback intent -> avoid coroutine churn on hot hit paths.
                    return;
                }

                if (onComplete == null)
                {
                    // Fire-and-forget effect: don't allocate wait coroutine.
                    return;
                }

                if (_waitRoutine != null)
                {
                    StopCoroutine(_waitRoutine);
                    _waitRoutine = null;
                }

                if (waitForAction <= 0f)
                {
                    onComplete?.Invoke();
                    return;
                }

                _waitRoutine = StartCoroutine(WaitThenCallback(waitForAction, onComplete));
            }
            catch
            {
                // Keep playable flow safe.
            }
        }

        private IEnumerator WaitThenCallback(float seconds, Action onComplete)
        {
            yield return new WaitForSeconds(seconds);
            _waitRoutine = null;
            onComplete?.Invoke();
        }

        private void ExecuteEffect(EffectEntry entry, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (entry.VfxPrefab != null)
            {
                Transform targetParent = null;
                if (entry.ParentToTarget)
                    targetParent = parent != null ? parent : CacheTransform;

                bool canPool = PoolManager.Instance != null && HasParticleSystems(entry.VfxPrefab);
                GameObject vfx = canPool ? PoolManager.Instance.Get(entry.VfxPrefab) : Instantiate(entry.VfxPrefab);
                if (vfx != null)
                {
                    vfx.transform.SetParent(targetParent, false);
                    vfx.transform.position = position;
                    vfx.transform.rotation = rotation;
                    vfx.SetActive(true);

                    if (canPool)
                    {
                        float lifeTime = GetParticleLifetime(vfx);
                        if (lifeTime > 0f)
                        {
                            var autoDisable = GetOrAddTimedAutoDisable(vfx);
                            autoDisable?.Play(lifeTime);
                        }
                    }
                }
            }

            if (entry.SfxClip == null) return;

            if (audioSource != null)
            {
                audioSource.PlayOneShot(entry.SfxClip);
                return;
            }

            if (useGlobalSoundManagerFallback && SoundManager.Instance != null)
                SoundManager.Instance.PlayOneShot(entry.SfxClip);
        }

        private static TimedAutoDisable GetOrAddTimedAutoDisable(GameObject vfxObject)
        {
            if (vfxObject == null) return null;

            int key = vfxObject.GetInstanceID();
            if (s_timedAutoDisableCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            if (!vfxObject.TryGetComponent(out cached))
                cached = vfxObject.AddComponent<TimedAutoDisable>();

            s_timedAutoDisableCache[key] = cached;
            return cached;
        }

        private static bool HasParticleSystems(GameObject vfxObject)
        {
            var particleSystems = GetCachedParticleSystems(vfxObject);
            return particleSystems != null && particleSystems.Length > 0;
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



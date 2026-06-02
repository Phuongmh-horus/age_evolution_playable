using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.AnimationSystems
{
    public class AnimationComponent : BaseComponent, IAnimator
    {
        [Serializable]
        private struct AnimationMapping
        {
            public AnimationType Type;

            [Tooltip("Tên state trong Animator (vd: Idle, Run, Attack, Jump...)")]
            public string StateName;

            [Tooltip("CrossFade time (0 = dùng Play ngay)")]
            public float CrossFadeTime;
        }

        [Header("Animator")]
        [SerializeField] private Animator animator;

        [Header("Mappings")]
        [SerializeField] private List<AnimationMapping> mappings = new List<AnimationMapping>();
        [SerializeField, Min(0f)] private float defaultCrossFadeTime = 0.08f;

        private readonly Dictionary<AnimationType, AnimationMapping> _cache = new Dictionary<AnimationType, AnimationMapping>();
        private readonly Dictionary<AnimationType, string> _fallbackStateCache = new Dictionary<AnimationType, string>();
        private readonly Dictionary<AnimationType, int> _stateHashCache = new Dictionary<AnimationType, int>();
        private readonly Dictionary<AnimationType, float> _clipLengthCache = new Dictionary<AnimationType, float>();
        private readonly Dictionary<float, WaitForSeconds> _waitCache = new Dictionary<float, WaitForSeconds>(8);
        private Coroutine _waitRoutine;
        private int _lastPlayedStateHash;
        private int _lastPlayedFrame = -1;
        private static readonly AnimationType[] s_animationTypes = (AnimationType[])Enum.GetValues(typeof(AnimationType));
        private static readonly Dictionary<int, Dictionary<AnimationType, string>> s_controllerFallbackCache =
            new Dictionary<int, Dictionary<AnimationType, string>>(16);

        private static readonly int HASH_MultiplierSpeed = Animator.StringToHash("AnimMultiplierSpeed");

        protected override void Awake()
        {
            base.Awake();
            ValidateAnimator();
            BuildCache();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            ValidateAnimator();
            BuildCache();
        }
#endif

        private void ValidateAnimator()
        {
            if (animator == null)
#if UNITY_EDITOR
            {
                if (!Application.isPlaying)
                    Debug.LogWarning($"[AnimationComponent] Missing Animator on {name}. Assign in Inspector.");
            }
#endif
#if !UNITY_EDITOR
            {
                // Runtime/Luna: skip warning to avoid log spam cost.
            }
#endif
        }

        private void BuildCache()
        {
            _cache.Clear();
            _fallbackStateCache.Clear();
            _stateHashCache.Clear();
            _clipLengthCache.Clear();
            if (mappings == null) return;

            for (int i = 0; i < mappings.Count; i++)
            {
                var m = mappings[i];
                if (string.IsNullOrEmpty(m.StateName)) continue;

                _cache[m.Type] = m;
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            // Playable Fix: Ensure Animator is assigned in Inspector
            if (animator == null) ValidateAnimator();
            WarmupControllerFallbackCache();
            WarmupStateHashCache();
            UpdateMultiplierSpeed(1f);
        }

        public void PlayAnimation(AnimationType animationType, float waitForAction = 0.5f, Action onComplete = null)
        {
            int stateHash = 0;
            if (animator != null)
            {
                if (_cache.TryGetValue(animationType, out var m))
                {
                    stateHash = GetOrCreateStateHash(animationType, m.StateName);
                    // float crossFadeTime = m.CrossFadeTime > 0f ? m.CrossFadeTime : defaultCrossFadeTime;
                    // if (crossFadeTime > 0f)
                    //     animator.CrossFadeInFixedTime(stateHash, crossFadeTime);
                    // else
                    PlayStateIfNeeded(stateHash);
                }
                else
                {
                    string targetState = ResolveFallbackStateName(animationType);
                    stateHash = GetOrCreateStateHash(animationType, targetState);
                    // if (defaultCrossFadeTime > 0f)
                    //     animator.CrossFadeInFixedTime(stateHash, defaultCrossFadeTime);
                    // else
                    PlayStateIfNeeded(stateHash);
                }
            }
            else
            {
                 // Silent fail or warning? Keeping warning for safety.
                 // Debug.LogWarning($"[AnimationComponent] Failed to play {animationType}: Animator is null.");
            }

            if (onComplete == null)
                return;

            if (_waitRoutine != null) StopCoroutine(_waitRoutine);
            if (waitForAction <= 0f)
            {
                onComplete.Invoke();
                _waitRoutine = null;
                return;
            }

            _waitRoutine = StartCoroutine(WaitThen(waitForAction, onComplete));
        }

        public void UpdateMultiplierSpeed(float amount)
        {
            if (animator == null) return;
            animator.SetFloat(HASH_MultiplierSpeed, amount);
        }

        public float GetAnimationClipLength(AnimationType animationType)
        {
            if (_clipLengthCache.TryGetValue(animationType, out float cached))
                return cached;

            float length = ComputeAnimationClipLength(animationType);
            _clipLengthCache[animationType] = length;
            return length;
        }

        private IEnumerator WaitThen(float t, Action onComplete)
        {
            if (t > 0f) yield return GetWaitInstruction(t);
            onComplete?.Invoke();
            _waitRoutine = null;
        }

        private WaitForSeconds GetWaitInstruction(float duration)
        {
            if (_waitCache.TryGetValue(duration, out var wait))
                return wait;

            wait = new WaitForSeconds(duration);
            _waitCache[duration] = wait;
            return wait;
        }

        private void PlayStateIfNeeded(int stateHash)
        {
            int frame = Time.frameCount;
            if (_lastPlayedFrame == frame && _lastPlayedStateHash == stateHash)
                return;

            animator.Play(stateHash, 0, 0f);
            _lastPlayedStateHash = stateHash;
            _lastPlayedFrame = frame;
        }

        private string ResolveFallbackStateName(AnimationType animationType)
        {
            if (_fallbackStateCache.TryGetValue(animationType, out var cachedState) && !string.IsNullOrEmpty(cachedState))
                return cachedState;

            string enumName = animationType.ToString();
            string targetState = enumName;

            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (TryResolveFromControllerCache(controller, animationType, out var cachedControllerState) &&
                !string.IsNullOrEmpty(cachedControllerState))
            {
                targetState = cachedControllerState;
            }
            else if (controller != null)
            {
                var clips = controller.animationClips;
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip == null || string.IsNullOrEmpty(clip.name)) continue;

                    if (clip.name.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetState = clip.name;
                        break;
                    }
                }
            }

            _fallbackStateCache[animationType] = targetState;
            return targetState;
        }

        private void WarmupStateHashCache()
        {
            for (int i = 0; i < s_animationTypes.Length; i++)
            {
                var type = s_animationTypes[i];
                if (_cache.TryGetValue(type, out var mapped) && !string.IsNullOrEmpty(mapped.StateName))
                {
                    GetOrCreateStateHash(type, mapped.StateName);
                    continue;
                }

                string fallback = ResolveFallbackStateName(type);
                if (!string.IsNullOrEmpty(fallback))
                    GetOrCreateStateHash(type, fallback);
            }
        }

        private int GetOrCreateStateHash(AnimationType animationType, string stateName)
        {
            if (_stateHashCache.TryGetValue(animationType, out int cached))
                return cached;

            int hash = Animator.StringToHash(stateName);
            _stateHashCache[animationType] = hash;
            return hash;
        }

        private void WarmupControllerFallbackCache()
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null) return;

            int controllerId = controller.GetInstanceID();
            if (s_controllerFallbackCache.ContainsKey(controllerId)) return;

            var map = new Dictionary<AnimationType, string>(s_animationTypes.Length);
            var clips = controller.animationClips;

            for (int t = 0; t < s_animationTypes.Length; t++)
            {
                var animType = s_animationTypes[t];
                string enumName = animType.ToString();
                string resolved = enumName;

                if (clips != null)
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        var clip = clips[i];
                        if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                        if (clip.name.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        resolved = clip.name;
                        break;
                    }
                }

                map[animType] = resolved;
            }

            s_controllerFallbackCache[controllerId] = map;
        }

        private static bool TryResolveFromControllerCache(
            RuntimeAnimatorController controller,
            AnimationType animationType,
            out string stateName)
        {
            stateName = null;
            if (controller == null) return false;

            int controllerId = controller.GetInstanceID();
            if (!s_controllerFallbackCache.TryGetValue(controllerId, out var map) || map == null)
                return false;

            return map.TryGetValue(animationType, out stateName);
        }

        private float ComputeAnimationClipLength(AnimationType animationType)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null) return 0f;

            string targetState = null;
            if (_cache.TryGetValue(animationType, out var mapped) && !string.IsNullOrEmpty(mapped.StateName))
                targetState = mapped.StateName;
            else
                targetState = ResolveFallbackStateName(animationType);

            string enumName = animationType.ToString();
            float fallbackLength = 0f;
            var clips = controller.animationClips;
            if (clips == null || clips.Length == 0) return 0f;

            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null || string.IsNullOrEmpty(clip.name)) continue;

                if (!string.IsNullOrEmpty(targetState) &&
                    string.Equals(clip.name, targetState, StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Max(0f, clip.length);
                }

                if (fallbackLength <= 0f &&
                    clip.name.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fallbackLength = Mathf.Max(0f, clip.length);
                }
            }

            return fallbackLength;
        }
    }
}

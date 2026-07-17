using System;
using System.Collections;
using DG.Tweening;
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

        [Header("Spawn Priority")]
        [Tooltip("Animation được play ngay và resolve đồng bộ khi Initialize() chạy, để tránh trượt/T-pose lúc vừa spawn.")]
        [SerializeField] private AnimationType initialAnimationType = AnimationType.Idle;
        [Tooltip("Các animation khác cần resolve VÀ evaluate thật sự ngay (vd Move/Run, vì hay dùng ngay sau khi spawn). Idle luôn được ưu tiên mặc định.")]
        [SerializeField] private List<AnimationType> priorityAnimationTypes = new List<AnimationType> { AnimationType.Move, AnimationType.Attack };

        private AnimationMapping[] _cache;




        private int _lastPlayedStateHash;
        private int _lastPlayedFrame = -1;

        private bool _cacheBuilt;
        private bool _animatorSettingsApplied;

        private static readonly AnimationType[] s_animationTypes = (AnimationType[])Enum.GetValues(typeof(AnimationType));
        private static readonly Dictionary<int, string[]> s_controllerFallbackCache = new Dictionary<int, string[]>(16);
        private static readonly Dictionary<int, int[]> s_controllerStateHashCache = new Dictionary<int, int[]>(16);
        private static readonly Dictionary<int, float[]> s_controllerClipLengthCache = new Dictionary<int, float[]>(16);
        private static readonly HashSet<int> s_controllerWarmedUp = new HashSet<int>();

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
            _cacheBuilt = false;
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
            if (_cacheBuilt)
                return;

            _cache = new AnimationMapping[32]; // Max enum buffer
            _cacheBuilt = true;
            if (mappings == null) return;

            for (int i = 0; i < mappings.Count; i++)
            {
                var m = mappings[i];
                if (string.IsNullOrEmpty(m.StateName)) continue;

                int index = (int)m.Type;
                if (index >= 0 && index < _cache.Length)
                {
                    _cache[index] = m;
                }
            }
        }

        public override void Dispose()
        {
            DOTween.Kill(this);
            base.Dispose();
        }

        public override void Initialize()
        {
            base.Initialize();
            // Playable Fix: Ensure Animator is assigned in Inspector
            if (animator == null) ValidateAnimator();

            if (animator != null && !_animatorSettingsApplied)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animatorSettingsApplied = true;
            }

            WarmupControllerFallbackCache();
            UpdateMultiplierSpeed(1f);
            PlayAnimation(initialAnimationType, 0f);
        }



        public void PlayAnimation(AnimationType animationType, float waitForAction = 0.5f, Action onComplete = null)
        {
            if (animator != null)
            {
                int typeIndex = (int)animationType;
                int stateHash = 0;

                if (_cache != null && typeIndex >= 0 && typeIndex < _cache.Length && !string.IsNullOrEmpty(_cache[typeIndex].StateName))
                {
                    stateHash = GetOrCreateStateHash(animationType, _cache[typeIndex].StateName);
                }
                else
                {
                    string targetState = ResolveFallbackStateName(animationType);
                    stateHash = GetOrCreateStateHash(animationType, targetState);
                }

                PlayStateIfNeeded(stateHash);
            }

            if (onComplete == null)
                return;

            DOTween.Kill(this);
            if (waitForAction <= 0f)
            {
                onComplete.Invoke();
                return;
            }

            DOVirtual.DelayedCall(waitForAction, () => { onComplete?.Invoke(); }, false).SetId(this);
        }

        public void UpdateMultiplierSpeed(float amount)
        {
            if (animator == null) return;
            animator.SetFloat(HASH_MultiplierSpeed, amount);
        }

        public float GetAnimationClipLength(AnimationType animationType)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                int controllerId = controller.GetInstanceID();
                if (s_controllerClipLengthCache.TryGetValue(controllerId, out var lengths))
                {
                    int index = (int)animationType;
                    if (index >= 0 && index < lengths.Length)
                    {
                        if (lengths[index] > 0f) return lengths[index];

                        float length = ComputeAnimationClipLength(animationType);
                        lengths[index] = length;
                        return length;
                    }
                }
            }
            return ComputeAnimationClipLength(animationType);
        }



        private void PlayStateIfNeeded(int stateHash)
        {
            int frame = Time.frameCount;
            if (_lastPlayedFrame == frame && _lastPlayedStateHash == stateHash)
                return;

            animator.Play(stateHash, 0, 0f);
            animator.Update(0f); // Force immediate evaluation to prevent T-pose or sliding on the first frame
            _lastPlayedStateHash = stateHash;
            _lastPlayedFrame = frame;
        }

        private string ResolveFallbackStateName(AnimationType animationType)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (TryResolveFromControllerCache(controller, animationType, out var cachedControllerState) &&
                !string.IsNullOrEmpty(cachedControllerState))
            {
                return cachedControllerState;
            }
            return animationType.ToString();
        }

        private int GetOrCreateStateHash(AnimationType animationType, string stateName)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                int controllerId = controller.GetInstanceID();
                if (s_controllerStateHashCache.TryGetValue(controllerId, out var hashes))
                {
                    int index = (int)animationType;
                    if (index >= 0 && index < hashes.Length)
                    {
                        if (hashes[index] != 0) return hashes[index];

                        int hash = Animator.StringToHash(stateName);
                        hashes[index] = hash;
                        return hash;
                    }
                }
            }
            return Animator.StringToHash(stateName);
        }

        private void WarmupControllerFallbackCache()
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null) return;

            int controllerId = controller.GetInstanceID();
            if (s_controllerWarmedUp.Contains(controllerId)) return;

            s_controllerWarmedUp.Add(controllerId);

            var map = new string[32];
            s_controllerStateHashCache[controllerId] = new int[32];
            s_controllerClipLengthCache[controllerId] = new float[32];

            for (int t = 0; t < s_animationTypes.Length; t++)
            {
                int index = (int)s_animationTypes[t];
                if (index >= 0 && index < map.Length)
                    map[index] = s_animationTypes[t].ToString();
            }

            var clips = controller.animationClips;
            if (clips != null && clips.Length > 0)
            {
                var exactByName = new Dictionary<string, string>(clips.Length, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                    if (!exactByName.ContainsKey(clip.name))
                        exactByName[clip.name] = clip.name;
                }

                for (int t = 0; t < s_animationTypes.Length; t++)
                {
                    var animType = s_animationTypes[t];
                    string enumName = animType.ToString();
                    int index = (int)animType;

                    if (index >= 0 && index < map.Length)
                    {
                        if (exactByName.TryGetValue(enumName, out var exactMatch))
                        {
                            map[index] = exactMatch;
                            continue;
                        }

                        for (int i = 0; i < clips.Length; i++)
                        {
                            var clip = clips[i];
                            if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                            if (clip.name.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                map[index] = clip.name;
                                break;
                            }
                        }
                    }
                }
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

            int index = (int)animationType;
            if (index >= 0 && index < map.Length)
            {
                stateName = map[index];
                return true;
            }
            return false;
        }

        private float ComputeAnimationClipLength(AnimationType animationType)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null) return 0f;

            string targetState = null;
            int index = (int)animationType;
            if (_cache != null && index >= 0 && index < _cache.Length && !string.IsNullOrEmpty(_cache[index].StateName))
                targetState = _cache[index].StateName;
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

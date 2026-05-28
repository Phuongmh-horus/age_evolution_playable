using System.Collections.Generic;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.CollisionSystems
{
    /// <summary>
    /// Playable-safe CollisionSystem:
    /// - No Unity.Collections, no Jobs, no Mathematics.
    /// - Keeps the same public API surface used by other scripts.
    /// </summary>
    [DisallowMultipleComponent]
    public class CollisionSystem : MonoBehaviour
    {
        public static CollisionSystem Instance { get; private set; }

        // Managed data only
        private readonly List<IHitable> _targets = new List<IHitable>();
        private readonly List<Transform> _transforms = new List<Transform>();
        private readonly List<uint> _masks = new List<uint>();
        private readonly List<ColliderData> _colliders = new List<ColliderData>();

        private bool _isDirty = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================= API PUBLIC =================

        public static void Register(IHitable target, Transform transform)
        {
            EnsureInstance();
            if (Instance == null)
            {
                Debug.LogError($"[CollisionSystem] Register FAILED: Instance is still NULL after EnsureInstance!");
                return;
            }
            Instance.AddTarget(target, transform);
        }

        private static void EnsureInstance()
        {
            if (Instance != null) return;

            // Create new GameObject with CollisionSystem
            var go = new GameObject("CollisionSystem (Auto-Created)");
            Instance = go.AddComponent<CollisionSystem>();
            DontDestroyOnLoad(go);
        }

        public static void Unregister(IHitable target)
        {
            if (Instance == null) return;
            Instance.RemoveTarget(target);
        }

        public static void RegisterBatch(IList<IHitable> targets, IList<Transform> transforms)
        {
            EnsureInstance();
            if (Instance == null) return;
            Instance.AddTargetsBatch(targets, transforms);
        }

        public static void UnregisterAll()
        {
            if (Instance == null) return;
            Instance.RemoveAllTargets();
        }

        /// <summary>
        /// Playable: "sorted index" is the same as insertion order index.
        /// </summary>
        public IHitable GetTargetBySortedIndex(int sortedIndex)
        {
            if (sortedIndex < 0 || sortedIndex >= _targets.Count) return null;
            return _targets[sortedIndex];
        }

        /// <summary>
        /// Optional accessors if you need them later.
        /// </summary>
        public int Count => _targets.Count;

        // ================= INTERNAL LOGIC =================

        private void AddTarget(IHitable target, Transform tr)
        {
            if (IsUnityNull(target))
            {
                Debug.LogWarning("[CollisionSystem] AddTarget failed: target is null");
                return;
            }
            if (tr == null || IsUnityNull(tr))
            {
                Debug.LogWarning("[CollisionSystem] AddTarget failed: transform is null");
                return;
            }

            CompactInvalidEntries();

            // Prevent duplicate registration of the same IHitable (common in pooled re-init paths).
            for (int i = 0; i < _targets.Count; i++)
            {
                if (!ReferenceEquals(_targets[i], target) && !Equals(_targets[i], target)) continue;

                _transforms[i] = tr;
                _masks[i] = 1u << (int)target.EntityType;
                _colliders[i] = target.GetColliderData();
                _isDirty = true;
                return;
            }

            _targets.Add(target);
            _transforms.Add(tr);
            _masks.Add(1u << (int)target.EntityType);

            var colData = target.GetColliderData();
            _colliders.Add(colData);
            _isDirty = true;
        }

        private void AddTargetsBatch(IList<IHitable> targets, IList<Transform> transforms)
        {
            if (targets == null || transforms == null || targets.Count != transforms.Count) return;

            for (int i = 0; i < targets.Count; i++)
            {
                AddTarget(targets[i], transforms[i]);
            }
        }

        private void RemoveTarget(IHitable target)
        {
            if (IsUnityNull(target)) return;

            bool removedAny = false;
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_targets[i], target) && !Equals(_targets[i], target)) continue;
                RemoveAtSwapBack(i);
                removedAny = true;
            }

            if (removedAny) _isDirty = true;
        }

        private void RemoveAllTargets()
        {
            _targets.Clear();
            _transforms.Clear();
            _masks.Clear();
            _colliders.Clear();
            _isDirty = false;
        }

        // ================= UPDATE LOGIC =================

        /// <summary>
        /// In the Jobs version this ensured data sync/sort once per frame.
        /// Here it's kept for compatibility and can be used to "refresh" cached collider data.
        /// </summary>
        public void EnsureDataIsReady()
        {
            if (_isDirty && CompactInvalidEntries())
            {
                _isDirty = true;
            }

            if (!_isDirty) return;
            ManualUpdate();
        }

        /// <summary>
        /// Refresh cached collider data and masks from current targets.
        /// </summary>
        public void ManualUpdate()
        {
            _isDirty = false;

            // refresh collider data (in case size changes at runtime)
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var t = _targets[i];
                var tr = _transforms[i];
                if (IsUnityNull(t) || tr == null || IsUnityNull(tr))
                {
                    RemoveAtSwapBack(i);
                    continue;
                }

                _masks[i] = 1u << (int)t.EntityType;
                _colliders[i] = t.GetColliderData();
            }
        }

        private bool CompactInvalidEntries()
        {
            bool removedAny = false;
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var t = _targets[i];
                var tr = _transforms[i];
                if (!IsUnityNull(t) && tr != null && !IsUnityNull(tr)) continue;

                RemoveAtSwapBack(i);
                removedAny = true;
            }

            return removedAny;
        }

        private void RemoveAtSwapBack(int index)
        {
            int last = _targets.Count - 1;
            if (index < 0 || index > last) return;

            if (index != last)
            {
                _targets[index] = _targets[last];
                _transforms[index] = _transforms[last];
                _masks[index] = _masks[last];
                _colliders[index] = _colliders[last];
            }

            _targets.RemoveAt(last);
            _transforms.RemoveAt(last);
            _masks.RemoveAt(last);
            _colliders.RemoveAt(last);
        }

        private static bool IsUnityNull(object obj)
        {
            if (obj == null) return true;
            if (obj is Object unityObject) return unityObject == null;
            return false;
        }

        // ================= Data query helpers (optional) =================

        public Transform GetTransform(int index)
        {
            if (index < 0 || index >= _transforms.Count) return null;
            return _transforms[index];
        }

        public uint GetMask(int index)
        {
            if (index < 0 || index >= _masks.Count) return 0;
            return _masks[index];
        }

        public ColliderData GetColliderData(int index)
        {
            if (index < 0 || index >= _colliders.Count) return default;
            return _colliders[index];
        }
    }
}

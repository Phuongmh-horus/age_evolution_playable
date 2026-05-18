using System.Collections.Generic;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using Pools; // MonoSingleton
using UnityEngine;

namespace GamePlay.OscillationSystems
{
    public class OscillationSystem : MonoSingleton<OscillationSystem>
    {
        [SerializeField] private float _speed = 1.5f;

        private struct OscEntry
        {
            public Transform Transform;
            public IOscillator Oscillator;

            public Vector3 BaseLocalPos;

            // Range: [ -LeftOffset , RightOffset ] quanh BaseLocalPos.x
            public float MidPoint;
            public float Radius;
            public float Phase;

            public bool IsActive;
        }

        private readonly List<OscEntry> _entries = new List<OscEntry>(128);

        #region PUBLIC METHODS

        // Giữ nguyên signature để không gãy code đang gọi từ CombatSystem/CapabilityPack
        public static void Register(Transform unitTransform, CapabilityPack pack, CapabilityFlags flags)
        {
            if (Instance == null) return;
            if ((flags & CapabilityFlags.Oscillate) == 0) return;

            // CapabilityPack là struct => check field thay vì check pack == null
            if (pack.Oscillator == null) return;

            Instance.RegisterInternal(unitTransform, pack.Oscillator);
        }


        // Thêm overload “thuần” cho playable (nếu bạn muốn dùng trực tiếp)
        public static void Register(Transform unitTransform, IOscillator oscillator)
        {
            if (Instance == null) return;
            Instance.RegisterInternal(unitTransform, oscillator);
        }

        public static void Unregister(IOscillator oscillator)
        {
            if (Instance == null) return;
            Instance.UnregisterInternal(oscillator);
        }

        // Được gọi từ GameplayManager/loop của bạn
        public void ManualUpdate()
        {
            if (_entries.Count == 0) return;

            float now = Time.time;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];

                if (!e.IsActive || e.Transform == null || IsUnityNull(e.Oscillator))
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                // Oscillate theo trục X (local)
                float t = (now + e.Phase) * _speed;
                float offsetX = e.MidPoint + Mathf.Sin(t) * e.Radius;

                Vector3 p = e.BaseLocalPos;
                p.x = e.BaseLocalPos.x + offsetX;
                e.Transform.localPosition = p;

                // write-back (struct)
                _entries[i] = e;
            }

        }

        #endregion

        #region PRIVATE METHODS

        private void RegisterInternal(Transform unitTransform, IOscillator component)
        {
            if (unitTransform == null) return;
            if (component == null || IsUnityNull(component)) return;

            // Tính toán giống logic cũ:
            // Left = 2, Right = 6 => Range [-2, 6]. Radius = 4. Mid = 2.
            float radius = (component.RightOffset + component.LeftOffset) * 0.5f;
            float midPoint = component.RightOffset - radius;

            var entry = new OscEntry
            {
                Transform = unitTransform,
                Oscillator = component,
                BaseLocalPos = unitTransform.localPosition,
                Radius = radius,
                MidPoint = midPoint,
                Phase = Random.Range(0f, 10f),
                IsActive = true
            };

            _entries.Add(entry);

        }

        private void UnregisterInternal(IOscillator oscillator)
        {
            if (oscillator == null) return;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Oscillator == oscillator)
                {
                    _entries.RemoveAt(i);
                    return;
                }
            }
        }

        // Safe check UnityEngine.Object null qua interface
        private static bool IsUnityNull(object obj)
        {
            if (obj == null) return true;

            // Nếu object implement UnityEngine.Object (MonoBehaviour/ScriptableObject), Unity null check chuẩn.
            if (obj is Object uo) return uo == null;

            return false;
        }

        #endregion
    }
}

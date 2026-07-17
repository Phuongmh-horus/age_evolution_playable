using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.CollisionSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.OscillationSystems;
using Pools;
using UnityEngine;

namespace GamePlay.Entities
{
    public class PoolEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] protected Transform _transform;
        [SerializeField] protected EntityType _entityType;
        public EntityType EntityType => _entityType;
        // --------------------------------

        public Transform Transform => _transform != null ? _transform : transform;

        // --- COMMON CAPABILITY PACK ---
        [HideInInspector] public CapabilityPack Pack;
        [HideInInspector] public CapabilityFlags ActiveFlags;

        protected static readonly List<MonoBehaviour> s_mbBuffer = new List<MonoBehaviour>(64);
        private bool _capabilityPackBuilt;

        protected virtual void Awake()
        {
            if (_transform == null) _transform = transform;
        }

        protected void BuildCapabilityPack(bool forceRebuild = false)
        {
            if (_capabilityPackBuilt && !forceRebuild)
                return;

            Pack = default;
            ActiveFlags = CapabilityFlags.None;
            GetComponentsInChildren(true, s_mbBuffer);

            for (int i = 0; i < s_mbBuffer.Count; i++)
            {
                var mb = s_mbBuffer[i];
                if (mb == null) continue;

                if (mb is BaseComponent baseComponent)
                    baseComponent.SetPoolEntity(this);

                if (mb is IMover mover) { Pack.Mover = mover; ActiveFlags |= CapabilityFlags.Move; }
                if (mb is IAttacker attacker) { Pack.Attacker = attacker; ActiveFlags |= CapabilityFlags.Attack; }
                if (mb is IAnimator animator) { Pack.Animator = animator; ActiveFlags |= CapabilityFlags.Animator; }
                if (mb is IHealable healable) { Pack.Healable = healable; ActiveFlags |= CapabilityFlags.Heal; }
                if (mb is IOscillator oscillator) { Pack.Oscillator = oscillator; ActiveFlags |= CapabilityFlags.Oscillate; }
                if (mb is IEffector effector) { Pack.Effector = effector; ActiveFlags |= CapabilityFlags.Effector; }
                if (mb is IHitable hitable) { Pack.Hitable = hitable; ActiveFlags |= CapabilityFlags.Hit; }
                if (mb is IJumper jumper) { Pack.Jumper = jumper; ActiveFlags |= CapabilityFlags.Jump; }
            }
            s_mbBuffer.Clear();
            _capabilityPackBuilt = true;

        }

        public virtual void New()
        {
        }

        public virtual void Free()
        {
        }

        public virtual void Despawn()
        {
            PoolSystem.Despawn(this);
        }
    }
}

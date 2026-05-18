using System.Collections.Generic;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using Pools;
using UnityEngine;

namespace PlayerArmy
{
    [DisallowMultipleComponent]
    public class AttackProjectile : PoolEntity
    {
        [Header("Components")]
        [SerializeField] private List<MonoBehaviour> components = new List<MonoBehaviour>();

        [Header("Target")]
        [SerializeField] private AttackComponent.AttackTargetPreset attackTargetPreset = AttackComponent.AttackTargetPreset.PlayerProjectile;

        [HideInInspector] public CapabilityPack Pack;
        [HideInInspector] public CapabilityFlags ActiveFlags;

        protected override void Awake()
        {
            base.Awake();
            BuildCapabilityPack();
        }

#if UNITY_EDITOR
        protected void OnValidate()
        {
            if (components == null)
            {
                components = new List<MonoBehaviour>();
            }

            if (components.Count > 0)
            {
                return;
            }

            var monos = GetComponents<MonoBehaviour>();
            for (int i = 0; i < monos.Length; i++)
            {
                var mb = monos[i];
                if (mb == null || mb == this)
                {
                    continue;
                }

                if (mb is IComponent)
                {
                    components.Add(mb);
                }
            }
        }
#endif

        public void Launch(Vector3 startPoint, float groundY, Vector3 direction, float distance, float duration, float arcHeight, float rotationSpeed, int damage)
        {
            BuildCapabilityPack();

            if (Pack.Attacker is AttackComponent attackComponent)
            {
                attackComponent.SetTargetPreset(attackTargetPreset);
                attackComponent.Setup(damage);
            }

            if ((ActiveFlags & CapabilityFlags.Attack) != 0 && Pack.Attacker != null)
            {
                Pack.Attacker.Initialize();
            }

            if ((ActiveFlags & CapabilityFlags.Move) != 0 && Pack.Mover != null)
            {
                Pack.Mover.Initialize();
            }

            transform.position = startPoint;
            transform.rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z).normalized)
                : Quaternion.identity;

            EnemyProjectileSystem.RegisterProjectile(
                transform,
                startPoint,
                groundY,
                direction,
                distance,
                duration,
                arcHeight,
                rotationSpeed,
                Pack.Attacker,
                Pack.Mover);
        }

        public void DisposeProjectile()
        {
            Despawn();
        }

        private void BuildCapabilityPack()
        {
            Pack = default;
            ActiveFlags = CapabilityFlags.None;

            if (components == null)
            {
                components = new List<MonoBehaviour>();
            }

            bool hasValid = false;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null)
                {
                    hasValid = true;
                    break;
                }
            }

            if (!hasValid)
            {
                components.Clear();
                var monos = GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < monos.Length; i++)
                {
                    var mb = monos[i];
                    if (mb == null || mb == this)
                    {
                        continue;
                    }

                    if (mb is IComponent)
                    {
                        components.Add(mb);
                    }
                }
            }

            for (int i = 0; i < components.Count; i++)
            {
                var mb = components[i];
                if (mb == null)
                {
                    continue;
                }

                if (mb is IMover mover)
                {
                    Pack.Mover = mover;
                    ActiveFlags |= CapabilityFlags.Move;
                }

                if (mb is IAttacker attacker)
                {
                    Pack.Attacker = attacker;
                    ActiveFlags |= CapabilityFlags.Attack;
                }
            }

            if (Pack.Attacker == null || Pack.Mover == null)
            {
                var monos = GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < monos.Length; i++)
                {
                    var mb = monos[i];
                    if (mb == null || mb == this)
                    {
                        continue;
                    }

                    if (Pack.Attacker == null && mb is IAttacker attacker)
                    {
                        Pack.Attacker = attacker;
                        ActiveFlags |= CapabilityFlags.Attack;
                    }

                    if (Pack.Mover == null && mb is IMover mover)
                    {
                        Pack.Mover = mover;
                        ActiveFlags |= CapabilityFlags.Move;
                    }

                    if (Pack.Attacker != null && Pack.Mover != null)
                    {
                        break;
                    }
                }
            }
        }
    }
}

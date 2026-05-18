using System.Collections.Generic;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.Weapons
{
    public class WeaponUnit : PoolEntity
    {
        [Header("Components References (MonoBehaviours implementing IComponent)")]
        [SerializeField] private List<MonoBehaviour> components = new List<MonoBehaviour>();
        [SerializeField] private Transform childrenRoot;

        [Header("childrenRoot Transform Cache")]
        [SerializeField] private Vector3 renderPosition;
        [SerializeField] private Vector3 renderEulerAngle;

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
            // SỬA: Xóa dòng base.OnValidate() vì PoolEntity không có hàm này để gọi

            // Logic cũ giữ nguyên
            if (components == null) components = new List<MonoBehaviour>();
            if (components.Count > 0) return;

            var mbs = GetComponents<MonoBehaviour>();
            for (int i = 0; i < mbs.Length; i++)
            {
                var mb = mbs[i];
                if (mb == null) continue;
                if (mb is IComponent) components.Add(mb);
            }
        }
#endif

        public void Initialize()
        {
            // Runtime safety: ensure pack is valid even if inspector list was empty.
            if (Pack.Mover == null || Pack.Attacker == null)
            {
                BuildCapabilityPack();
            }

            if ((ActiveFlags & CapabilityFlags.Move) != 0 && Pack.Mover != null) Pack.Mover.Initialize();

            if ((ActiveFlags & CapabilityFlags.Attack) != 0 && Pack.Attacker != null)
            {
                Pack.Attacker.Initialize();

                // [FIX] Force Weapon to target Enemies defaults (Character + Wheel)
                // This ensures it hits the Wheel even if serialization is wrong
                if (Pack.Attacker is AttackComponent attackComp)
                {
                    attackComp.SetTargetPreset(AttackComponent.AttackTargetPreset.Enemy);
                }
            }

            RegisterEvents(true);
        }

        [ContextMenu("Cache childrenRoot location")]
        public void CacheChildrenRootTransform()
        {
            if (childrenRoot == null)
            {
                return;
            }

            renderPosition = childrenRoot.localPosition;
            renderEulerAngle = childrenRoot.localEulerAngles;
        }

        public void SetFly()
        {
            if (childrenRoot == null)
            {
                return;
            }

            childrenRoot.localPosition = renderPosition;
            childrenRoot.localRotation = Quaternion.Euler(renderEulerAngle);
        }

        [ContextMenu("Set Default")]
        public void SetDefault()
        {
            if (childrenRoot == null)
            {
                return;
            }

            childrenRoot.localPosition = Vector3.zero;
            childrenRoot.localRotation = Quaternion.identity;
        }

        public bool Launch(Vector3 startPoint, Vector3 direction, float distance, float duration, float arcHeight, float rotationSpeed, int damage, EnemyProjectileSystem.ProjectileSpinAxis spinAxis = EnemyProjectileSystem.ProjectileSpinAxis.X, EnemyProjectileSystem.ProjectileMotionMode motionMode = EnemyProjectileSystem.ProjectileMotionMode.Arc)
        {
            Initialize();

            if (Pack.Attacker == null)
            {
                return false;
            }

            Pack.Attacker.Setup(damage);
            if (Pack.Attacker is AttackComponent attackComp)
            {
                attackComp.SetTargetPreset(AttackComponent.AttackTargetPreset.PlayerProjectile);
            }

            Vector3 launchDirection = direction;
            if (motionMode == EnemyProjectileSystem.ProjectileMotionMode.Arc)
            {
                launchDirection.y = 0f;
            }

            if (launchDirection.sqrMagnitude < 0.0001f)
            {
                launchDirection = Vector3.forward;
            }

            launchDirection.Normalize();
            transform.SetPositionAndRotation(startPoint, Quaternion.LookRotation(launchDirection));

            EnemyProjectileSystem.RegisterProjectile(
                transform,
                startPoint,
                startPoint.y,
                launchDirection,
                Mathf.Max(0.1f, distance),
                Mathf.Max(0.01f, duration),
                Mathf.Max(0f, arcHeight),
                rotationSpeed,
                Pack.Attacker,
                Pack.Mover,
                spinAxis,
                motionMode);

            return true;
        }

        public void Dispose()
        {
            DespawnInterval();
        }

        private void RegisterEvents(bool register)
        {
            if (register)
            {
                if (Pack.Mover != null) Pack.Mover.OnMovementComplete += HandleMoveComplete;
                if (Pack.Attacker != null) Pack.Attacker.OnAttackComplete += HandleAttackComplete;
            }
            else
            {
                if (Pack.Mover != null) Pack.Mover.OnMovementComplete -= HandleMoveComplete;
                if (Pack.Attacker != null) Pack.Attacker.OnAttackComplete -= HandleAttackComplete;
            }
        }

        private void HandleMoveComplete()
        {
            DespawnInterval();
        }

        private void HandleAttackComplete(IHitable target)
        {
            DespawnInterval();
        }

        private void DespawnInterval()
        {
            if ((ActiveFlags & CapabilityFlags.Move) != 0 && Pack.Mover != null) Pack.Mover.Dispose();
            if ((ActiveFlags & CapabilityFlags.Attack) != 0 && Pack.Attacker != null) Pack.Attacker.Dispose();

            RegisterEvents(false);

            // Trả về Pool - Despawn() từ PoolEntity
            Despawn();
        }

        private void BuildCapabilityPack()
        {
            Pack = default;
            ActiveFlags = CapabilityFlags.None;

            if (components == null) components = new List<MonoBehaviour>();

            bool hasValid = false;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null) { hasValid = true; break; }
            }

            if (!hasValid)
            {
                components.Clear();
                var monos = GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < monos.Length; i++)
                {
                    var mb = monos[i];
                    if (mb == null || mb == this) continue;
                    if (mb is IComponent) components.Add(mb);
                }
            }

            for (int i = 0; i < components.Count; i++)
            {
                var mb = components[i];
                if (mb == null) continue;

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

            // Fallback: ensure Attacker/Mover are found even if inspector list is partial
            if (Pack.Attacker == null || Pack.Mover == null)
            {
                var monos = GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < monos.Length; i++)
                {
                    var mb = monos[i];
                    if (mb == null || mb == this) continue;

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

                    if (Pack.Attacker != null && Pack.Mover != null) break;
                }
            }
        }

    }
}

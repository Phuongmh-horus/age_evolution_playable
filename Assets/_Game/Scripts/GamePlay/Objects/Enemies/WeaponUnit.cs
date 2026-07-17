using System.Collections.Generic;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.Weapons
{
    public class WeaponUnit : PoolEntity
    {
        // [Header("Components References (MonoBehaviours implementing IComponent)")]
        // [SerializeField] private List<MonoBehaviour> components = new List<MonoBehaviour>();
        [SerializeField] private Transform childrenRoot;

        [Header("childrenRoot Transform Cache")]
        [SerializeField] private Vector3 renderPosition;
        [SerializeField] private Vector3 renderEulerAngle;

        protected override void Awake()
        {
            base.Awake();
            BuildCapabilityPack();
        }


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
    }
}

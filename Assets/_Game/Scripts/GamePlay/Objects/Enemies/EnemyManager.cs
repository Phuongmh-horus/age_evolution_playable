using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.Characters;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Weapons;
using Pools;
using UnityEngine;

namespace GamePlay.Enemies
{
    // Lightweight runtime state per enemy.
    public class EnemyData
    {
        public EnemyUnit Causer;
        public bool IsActive;

        // Attack timing
        public float Cooldown;
        public float NextAttackTime;
        public bool IsPreAttacking;
        public float FireActualTime;

        // Weapon timing
        public WeaponUnit CurrentWeapon;
        public float ReloadDuration;
        public float WeaponRespawnTime;

        public bool HasWeapon => CurrentWeapon != null;
    }

    public class EnemyManager : MonoSingleton<EnemyManager>
    {
        private readonly List<EnemyData> _enemies = new List<EnemyData>(64);

        [SerializeField] protected EnemyVariable enemyVariable;

        private EnemyData _currentEnemy;
        private float _currentTime;
        private bool _isGameplayPaused;
        private float _pausedAtTime;
        private readonly List<AttackComponent> _attackComponentsBuffer = new List<AttackComponent>(8);

        protected override void Awake()
        {
            base.Awake();
            enabled = false;
        }

        private void EnsureInitialized()
        {
            if (enemyVariable != null) return;
            if (ConfigHolder.Instance != null && ConfigHolder.Instance.EnemyVariable != null)
            {
                enemyVariable = ConfigHolder.Instance.EnemyVariable;
            }
        }

        public void RegisterEnemy(EnemyUnit causer)
        {
            if (causer == null) return;

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] == null || _enemies[i].Causer != causer) continue;
                _enemies[i].IsActive = true;
                return;
            }

            EnsureInitialized();
            if (enemyVariable == null || enemyVariable.AttackVariable == null)
            {
                Debug.LogWarning("[EnemyManager] Missing EnemyVariable/AttackVariable. Enemy was not registered.");
                return;
            }

            var data = new EnemyData
            {
                Causer = causer,
                Cooldown = enemyVariable.AttackVariable.AttackCooldown,
                NextAttackTime = Time.time + enemyVariable.AttackVariable.AttackCooldown,
                IsActive = true,
                IsPreAttacking = false,
                ReloadDuration = enemyVariable.AttackVariable.WeaponReloadDelay,
                WeaponRespawnTime = Time.time + 9999f,
                CurrentWeapon = null
            };

            // Initial weapon spawn.
            RefillWeapon(data);
            data.WeaponRespawnTime = Time.time + data.ReloadDuration;

            _enemies.Add(data);
            if (!enabled) enabled = true;
        }

        public void UnregisterEnemy(EnemyUnit causer)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (enemy == null || enemy.Causer != causer) continue;

                enemy.IsActive = false;
                ReleaseWeapon(enemy);
                break;
            }

            CompactInactiveEnemies();
        }

        private void Update()
        {
            if (!GameplayManager.IsGameStarted)
            {
                if (!_isGameplayPaused)
                {
                    _isGameplayPaused = true;
                    _pausedAtTime = Time.time;
                }
                return;
            }

            if (_isGameplayPaused)
            {
                float pausedDuration = Mathf.Max(0f, Time.time - _pausedAtTime);
                if (pausedDuration > 0f)
                {
                    ShiftEnemyTimers(pausedDuration);
                }
                _isGameplayPaused = false;
            }

            int count = _enemies.Count;
            if (count == 0) return;

            _currentTime = Time.time;
            bool needsCompact = false;

            for (int i = 0; i < count; i++)
            {
                _currentEnemy = _enemies[i];

                if (_currentEnemy == null || !_currentEnemy.IsActive)
                {
                    needsCompact = true;
                    continue;
                }

                if (_currentEnemy.Causer == null)
                {
                    _currentEnemy.IsActive = false;
                    ReleaseWeapon(_currentEnemy);
                    needsCompact = true;
                    continue;
                }

                // Weapon refill
                if (!_currentEnemy.HasWeapon && _currentTime >= _currentEnemy.WeaponRespawnTime)
                {
                    RefillWeapon(_currentEnemy);
                }

                // Attack sequencing
                if (_currentEnemy.IsPreAttacking)
                {
                    if (_currentTime >= _currentEnemy.FireActualTime)
                    {
                        if (_currentEnemy.HasWeapon)
                        {
                            FireWeapon(_currentEnemy);
                        }
                        else
                        {
                            _currentEnemy.IsPreAttacking = false;
                        }
                    }
                }
                else if (_currentTime >= _currentEnemy.NextAttackTime && _currentEnemy.HasWeapon)
                {
                    StartAttackSequence(_currentEnemy);
                }
            }

            if (needsCompact)
            {
                CompactInactiveEnemies();
            }

            if (_enemies.Count == 0)
            {
                enabled = false;
            }
        }

        private void ShiftEnemyTimers(float pausedDuration)
        {
            int count = _enemies.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                var enemy = _enemies[i];
                if (enemy == null || !enemy.IsActive) continue;

                enemy.NextAttackTime += pausedDuration;
                enemy.FireActualTime += pausedDuration;
                enemy.WeaponRespawnTime += pausedDuration;
            }
        }

        // Spawn weapon from pool and attach to hand.
        private void RefillWeapon(EnemyData enemy)
        {
            if (enemy == null || enemy.HasWeapon) return;
            if (enemy.Causer == null || enemy.Causer.WeaponPrefab == null || enemy.Causer.HandTransform == null) return;

            var weapon = enemy.Causer.WeaponPrefab.Spawn(parent: enemy.Causer.HandTransform);
            if (weapon == null) return;

            weapon.Transform.localScale = Vector3.one;
            enemy.Causer.AttachWeapon(weapon);
            enemy.CurrentWeapon = weapon;
        }

        private void StartAttackSequence(EnemyData enemy)
        {
            if (enemy == null || enemy.Causer == null || enemyVariable == null || enemyVariable.AttackVariable == null) return;

            enemy.Causer.PlayAnimation(AnimationType.Attack);
            enemy.IsPreAttacking = true;
            enemy.FireActualTime = _currentTime + enemyVariable.AttackVariable.AnimDelaySeconds;
        }

        private void FireWeapon(EnemyData enemy)
        {
            if (enemy == null || enemy.Causer == null || enemy.Causer.HandTransform == null)
            {
                if (enemy != null) enemy.IsActive = false;
                return;
            }

            if (enemyVariable == null || enemyVariable.AttackVariable == null)
            {
                enemy.IsPreAttacking = false;
                return;
            }

            var weapon = enemy.CurrentWeapon;
            if (weapon == null || weapon.Transform == null)
            {
                enemy.CurrentWeapon = null;
                enemy.IsPreAttacking = false;
                return;
            }

            // Match original flow: enemy-thrown weapon can hit wheel/characters.
            // Fast path: use already-resolved attacker from capability pack (no array allocation each shot).
            if (weapon.Pack.Attacker is AttackComponent packAttackComponent)
            {
                packAttackComponent.SetTargetPreset(AttackComponent.AttackTargetPreset.Enemy);
            }
            else
            {
                // Fallback for prefabs with multiple/indirect attack components.
                _attackComponentsBuffer.Clear();
                weapon.GetComponentsInChildren<AttackComponent>(true, _attackComponentsBuffer);
                for (int i = 0; i < _attackComponentsBuffer.Count; i++)
                {
                    if (_attackComponentsBuffer[i] == null) continue;
                    _attackComponentsBuffer[i].SetTargetPreset(AttackComponent.AttackTargetPreset.Enemy);
                }
            }

            weapon.Initialize();

            EnemyProjectileSystem.RegisterProjectile(
                weapon.Transform,
                enemy.Causer.HandTransform.position,
                enemy.Causer.Transform.position.y + enemyVariable.AttackVariable.OffsetY,
                enemy.Causer.Transform.forward,
                enemyVariable.AttackVariable.ThrowDistance,
                enemyVariable.AttackVariable.ThrowDuration,
                enemyVariable.AttackVariable.ArcHeight,
                enemyVariable.AttackVariable.RotationSpeed,
                weapon.Pack.Attacker,
                weapon.Pack.Mover
            );

            enemy.Causer.ThrowWeapon();

            enemy.CurrentWeapon = null;
            enemy.IsPreAttacking = false;
            enemy.WeaponRespawnTime = _currentTime + enemy.ReloadDuration;
            enemy.NextAttackTime = _currentTime + enemy.Cooldown;
        }

        public void UnregisterAllEnemies()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (enemy == null) continue;
                enemy.IsActive = false;
                ReleaseWeapon(enemy);
            }

            _enemies.Clear();
            enabled = false;
        }

        private void CompactInactiveEnemies()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var enemy = _enemies[i];
                if (enemy != null && enemy.IsActive && enemy.Causer != null) continue;

                ReleaseWeapon(enemy);
                _enemies.RemoveAt(i);
            }
        }

        private static void ReleaseWeapon(EnemyData enemy)
        {
            if (enemy == null || enemy.CurrentWeapon == null) return;

            if (enemy.CurrentWeapon.Transform != null)
            {
                enemy.CurrentWeapon.Transform.parent = null;
            }

            enemy.CurrentWeapon.Despawn();
            enemy.CurrentWeapon = null;
        }
    }
}

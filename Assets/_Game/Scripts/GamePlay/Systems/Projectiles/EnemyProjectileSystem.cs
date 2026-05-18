using System.Collections.Generic;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using GamePlay.CollisionSystems; // [FIX] Added missing namespace
using UnityEngine;

namespace GamePlay.CombatSystems
{
    public class EnemyProjectileSystem : MonoSingleton<EnemyProjectileSystem>
    {
        [Header("Player Binding (Optional)")]
        [SerializeField] private GamePlay.Crushers.WheelUnit playerWheelRef;
        private static IHitable s_pendingPlayer;

        public static void RegisterPlayer(IHitable player)
        {
            if (player == null) return;
            s_pendingPlayer = player;
            if (Instance == null) return;
            Instance.RegisterPlayerInternal(player);
        }

        public static void UnregisterPlayer()
        {
            s_pendingPlayer = null;
            if (Instance == null) return;
            Instance.UnregisterPlayerInternal();
        }

        public static void RegisterTarget(IHitable target)
        {
            if (Instance == null) return;
            Instance.RegisterTargetInternal(target);
        }

        public static void UnregisterTarget(IHitable target)
        {
            if (Instance == null) return;
            Instance.UnregisterTargetInternal(target);
        }

        public static void ClearAllProjectiles()
        {
            if (Instance == null) return;
            Instance.ClearAllProjectilesInternal();
        }

        /// <summary>
        /// Ném vũ khí với khoảng cách & thời gian cụ thể (Luna-safe: dùng Vector3 thay float3).
        /// </summary>
        public enum ProjectileSpinAxis : byte
        {
            X,
            Y,
            Z
        }

        public enum ProjectileMotionMode : byte
        {
            Arc,
            Straight
        }

        public static void RegisterProjectile(
            Transform projectileTransform,
            Vector3 startPoint,
            float groundY,
            Vector3 direction,
            float distance,
            float duration,
            float arcHeight,
            float rotationSpeed,
            IAttacker attacker,
            IMover mover,
            ProjectileSpinAxis spinAxis = ProjectileSpinAxis.X,
            ProjectileMotionMode motionMode = ProjectileMotionMode.Arc
        )
        {
            if (Instance == null) return;
            Instance.RegisterProjectileInternal(projectileTransform, startPoint, groundY, direction, distance,
                duration, arcHeight, rotationSpeed, attacker, mover, spinAxis, motionMode);
        }

        private const float DESPAWN_DELAY = 0.25f;

        private IHitable _playerHitable;
        private readonly List<IHitable> _extraTargets = new List<IHitable>(32);
        private bool _isGameplayPaused;
        private float _pausedAtTime;

        private enum ProjectileState : byte
        {
            Active,
            Waiting
        }

        private struct ProjectileEntry
        {
            public Transform Transform;

            public Vector3 P0;
            public Vector3 P1;
            public Vector3 P2;
            public Vector3 Direction;

            public float StartTime;
            public float Duration;
            public float Distance;

            public float RotationSpeed;
            public ProjectileSpinAxis SpinAxis;
            public ProjectileMotionMode MotionMode;

            public ProjectileState State;
            public float WaitEndTime;

            public IAttacker Attacker;
            public IMover Mover;

            public float Radius;
        }

        private readonly List<ProjectileEntry> _projectiles = new List<ProjectileEntry>(64);

        protected override void Awake()
        {
            base.Awake();
            TryResolvePlayerReference();
        }

        private void RegisterPlayerInternal(IHitable player)
        {
            _playerHitable = player;
            if (player is GamePlay.Crushers.WheelUnit wheelUnit)
            {
                playerWheelRef = wheelUnit;
            }
        }

        private void UnregisterPlayerInternal()
        {
            _playerHitable = null;
        }

        private void RegisterTargetInternal(IHitable target)
        {
            if (target == null) return;
            if (_extraTargets.Contains(target)) return;
            _extraTargets.Add(target);
        }

        private void UnregisterTargetInternal(IHitable target)
        {
            if (target == null) return;
            _extraTargets.Remove(target);
        }


        private void ClearAllProjectilesInternal()
        {
            if (_projectiles.Count == 0)
            {
                return;
            }

            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                DisposeManaged(ref p);
                TryDespawnProjectile(p.Transform);
            }

            _projectiles.Clear();
        }

        private void RegisterProjectileInternal(
            Transform projectileTransform,
            Vector3 startPoint,
            float groundY,
            Vector3 direction,
            float distance,
            float duration,
            float arcHeight,
            float rotationSpeed,
            IAttacker attacker,
            IMover mover,
            ProjectileSpinAxis spinAxis,
            ProjectileMotionMode motionMode
        )
        {
            if (projectileTransform == null) return;

            projectileTransform.parent = null;

            Vector3 dir = direction;
            if (motionMode == ProjectileMotionMode.Arc)
            {
                dir.y = 0f;
            }

            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            Vector3 p0 = startPoint;
            Vector3 p2 = new Vector3(p0.x, groundY, p0.z) + (dir * distance);

            Vector3 mid = (p0 + p2) * 0.5f;
            float peakY = Mathf.Max(p0.y, p2.y) + arcHeight;
            Vector3 p1 = new Vector3(mid.x, peakY, mid.z);

            // [FIX] Luna serialization issue: size may not deserialize correctly
            // Use a smaller fixed radius for consistent gameplay across Unity/Luna
            const float WEAPON_HIT_RADIUS = 0.25f; // Fixed small radius for fair dodging
            float radius = WEAPON_HIT_RADIUS;

            // Only use attacker.Size if it's reasonable (not default Vector2.one)
            if (attacker != null && attacker.Size.x < 0.9f && attacker.Size.x > 0.01f)
            {
                radius = Mathf.Max(0.05f, attacker.Size.x * 0.5f);
            }

            var entry = new ProjectileEntry
            {
                Transform = projectileTransform,
                P0 = p0,
                P1 = p1,
                P2 = p2,
                Direction = dir,
                StartTime = Time.time,
                Duration = Mathf.Max(0.01f, duration),
                Distance = Mathf.Max(0.1f, distance),
                RotationSpeed = rotationSpeed,
                SpinAxis = spinAxis,
                MotionMode = motionMode,
                State = ProjectileState.Active,
                WaitEndTime = 0f,
                Attacker = attacker,
                Mover = mover,
                Radius = radius
            };

            _projectiles.Add(entry);
        }

        private void Update()
        {
            if (_projectiles.Count == 0) return;

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
                    ShiftProjectileTimes(pausedDuration);
                }
                _isGameplayPaused = false;
            }

            // Player validity check (Unity object might be destroyed)
            if (_playerHitable != null && (_playerHitable as Object) == null)
                _playerHitable = null;

            TryResolvePlayerReference();

            float now = Time.time;

            // Cache player data if available
            bool hasPlayer = _playerHitable != null && _playerHitable.IsActive;
            Vector3 playerPos = Vector3.zero;
            uint playerBit = 0;
            ColliderData playerCol = default;

            if (hasPlayer)
            {
                playerPos = _playerHitable.Position;
                playerBit = (uint)(1 << (int)_playerHitable.EntityType);
                playerCol = _playerHitable.GetColliderData();
            }

            var collisionSystem = CollisionSystem.Instance;
            int collisionCount = collisionSystem != null ? collisionSystem.Count : 0;

            // Iterate backwards for safe remove
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];

                // If projectile transform got destroyed -> cleanup
                if (p.Transform == null)
                {
                    DisposeManaged(ref p);
                    _projectiles.RemoveAt(i);
                    continue;
                }

                if (p.State == ProjectileState.Waiting)
                {
                    if (now >= p.WaitEndTime)
                    {
                        // Delay done -> notify movement finished then remove
                        p.Mover?.OnMovementFinished();
                        DisposeManaged(ref p);
                        TryDespawnProjectile(p.Transform);
                        _projectiles.RemoveAt(i);
                    }
                    continue;
                }

                // Active movement
                float t = Mathf.Clamp01((now - p.StartTime) / p.Duration);

                Vector3 pos = EvaluateProjectilePosition(p, t);
                p.Transform.position = pos;

                if (Mathf.Abs(p.RotationSpeed) > 0.001f)
                {
                     float previousT = Mathf.Clamp01((now - Time.deltaTime - p.StartTime) / p.Duration);
                     Vector3 oldPos = EvaluateProjectilePosition(p, previousT);
                     Vector3 direction = (pos - oldPos).normalized;
                     
                     if (direction.sqrMagnitude > 0.001f)
                     {
                         Quaternion lookRot = Quaternion.LookRotation(direction);
                         float spinAngle = (now - p.StartTime) * p.RotationSpeed * Mathf.Rad2Deg;
                         Quaternion spinRot = Quaternion.AngleAxis(spinAngle, ResolveSpinAxis(p.SpinAxis));
                         p.Transform.rotation = lookRot * spinRot;
                     }
                }

                // Collision check with player
                if (hasPlayer && p.Attacker != null)
                {
                    // Mask check: attacker target mask must include player entity bit
                    if ((p.Attacker.TargetMask & playerBit) != 0)
                    {
                        if (CheckHit(pos, p.Radius, playerPos, playerCol))
                        {
                            // Hit -> process immediately & remove immediately
                            p.Attacker.OnAttackSucceed(_playerHitable);
                            _playerHitable.OnHit(p.Attacker);

                            DisposeManaged(ref p);
                            TryDespawnProjectile(p.Transform);
                            _projectiles.RemoveAt(i);
                            continue;
                        }
                    }
                }
                if (p.Attacker != null && _extraTargets.Count > 0)
                {
                    for (int targetIndex = _extraTargets.Count - 1; targetIndex >= 0; targetIndex--)
                    {
                        var target = _extraTargets[targetIndex];
                        if (target == null)
                        {
                            _extraTargets.RemoveAt(targetIndex);
                            continue;
                        }

                        if (!target.IsActive) continue;
                        if (ReferenceEquals(target, _playerHitable)) continue;

                        uint targetBit = (uint)(1 << (int)target.EntityType);
                        if ((p.Attacker.TargetMask & targetBit) == 0) continue;

                        if (CheckHit(pos, p.Radius, target.Position, target.GetColliderData()))
                        {
                            p.Attacker.OnAttackSucceed(target);
                            target.OnHit(p.Attacker);

                            DisposeManaged(ref p);
                            TryDespawnProjectile(p.Transform);
                            _projectiles.RemoveAt(i);
                            goto NextProjectile;
                        }
                    }
                }

                // Hit any registered collision target that matches the attacker mask.
                // This covers EnemyUnit and CashTowerController, which already live in CollisionSystem.
                if (collisionSystem != null && collisionCount > 0 && p.Attacker != null)
                {
                    for (int k = 0; k < collisionCount; k++)
                    {
                        var target = collisionSystem.GetTargetBySortedIndex(k);
                        if (target == null || !target.IsActive) continue;
                        if (ReferenceEquals(target, _playerHitable)) continue;

                        bool isExtraTarget = false;
                        for (int extraIndex = 0; extraIndex < _extraTargets.Count; extraIndex++)
                        {
                            if (ReferenceEquals(_extraTargets[extraIndex], target))
                            {
                                isExtraTarget = true;
                                break;
                            }
                        }

                        if (isExtraTarget) continue;

                        uint targetBit = collisionSystem.GetMask(k);
                        if ((p.Attacker.TargetMask & targetBit) == 0) continue;

                        var targetTr = collisionSystem.GetTransform(k);
                        if (targetTr == null) continue;

                        Vector3 targetPos = targetTr.position;
                        if (Mathf.Abs(targetPos.x - pos.x) > 6f || Mathf.Abs(targetPos.z - pos.z) > 6f) continue;

                        var colData = collisionSystem.GetColliderData(k);
                        if (CheckHit(pos, p.Radius, targetPos, colData))
                        {
                            p.Attacker.OnAttackSucceed(target);
                            target.OnHit(p.Attacker);

                            DisposeManaged(ref p);
                            TryDespawnProjectile(p.Transform);
                            _projectiles.RemoveAt(i);
                            goto NextProjectile;
                        }
                    }
                }


                // Movement finished -> despawn immediately at the end point.
                if (t >= 1f)
                {
                    p.Mover?.OnMovementFinished();
                    DisposeManaged(ref p);
                    TryDespawnProjectile(p.Transform);
                    _projectiles.RemoveAt(i);
                    continue;
                }

                // write-back
                _projectiles[i] = p;
            NextProjectile:
                ;
            }
        }

        private static Vector3 EvaluateProjectilePosition(ProjectileEntry projectile, float t)
        {
            return projectile.MotionMode == ProjectileMotionMode.Straight
                ? projectile.P0 + projectile.Direction * projectile.Distance * t
                : EvaluateQuadraticBezier(projectile.P0, projectile.P1, projectile.P2, t);
        }

        private static Vector3 ResolveSpinAxis(ProjectileSpinAxis axis)
        {
            switch (axis)
            {
                case ProjectileSpinAxis.Y:
                    return Vector3.up;
                case ProjectileSpinAxis.Z:
                    return Vector3.forward;
                default:
                    return Vector3.right;
            }
        }

        private void ShiftProjectileTimes(float pausedDuration)
        {
            for (int i = 0; i < _projectiles.Count; i++)
            {
                var p = _projectiles[i];
                p.StartTime += pausedDuration;
                if (p.State == ProjectileState.Waiting)
                {
                    p.WaitEndTime += pausedDuration;
                }
                _projectiles[i] = p;
            }
        }

        private void TryResolvePlayerReference()
        {
            if (_playerHitable != null && (_playerHitable as Object) != null)
                return;

            if (playerWheelRef != null && playerWheelRef.gameObject.activeInHierarchy)
            {
                _playerHitable = playerWheelRef;
                return;
            }

            if (GameplayManager.Instance != null && GameplayManager.Instance.Turnable != null)
            {
                playerWheelRef = GameplayManager.Instance.Turnable;
                _playerHitable = playerWheelRef;
                return;
            }

            if (s_pendingPlayer != null && (s_pendingPlayer as Object) != null)
            {
                _playerHitable = s_pendingPlayer;
            }
        }

        private static Vector3 EvaluateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            // (1-t)^2 p0 + 2(1-t)t p1 + t^2 p2
            float u = 1f - t;
            return (u * u) * p0 + (2f * u * t) * p1 + (t * t) * p2;
        }

        private static bool CheckHit(Vector3 projPos, float projRadius, Vector3 playerFeetPos, ColliderData col)
        {
            switch (col.Type)
            {
                case ShapeType.Sphere:
                    {
                        float playerRadius = Mathf.Max(0.01f, col.Size.x);
                        float centerOffsetY = col.Size.y;
                        Vector3 center = playerFeetPos + new Vector3(0f, centerOffsetY, 0f);

                        float r = playerRadius + projRadius;
                        return (projPos - center).sqrMagnitude <= r * r;
                    }

                case ShapeType.Box:
                    {
                        Vector3 half = col.Size;
                        Vector3 center = playerFeetPos + new Vector3(0f, half.y, 0f);

                        // Closest point on AABB
                        Vector3 min = center - half;
                        Vector3 max = center + half;

                        float x = Mathf.Clamp(projPos.x, min.x, max.x);
                        float y = Mathf.Clamp(projPos.y, min.y, max.y);
                        float z = Mathf.Clamp(projPos.z, min.z, max.z);

                        Vector3 closest = new Vector3(x, y, z);
                        return (projPos - closest).sqrMagnitude <= projRadius * projRadius;
                    }

                case ShapeType.Cylinder:
                    {
                        float playerRadius = Mathf.Max(0.01f, col.Size.x);
                        float halfH = Mathf.Max(0.01f, col.Size.y);

                        // cylinder aligned to world up
                        Vector3 center = playerFeetPos + new Vector3(0f, halfH, 0f);

                        // vertical overlap
                        float yMin = center.y - halfH;
                        float yMax = center.y + halfH;
                        if (projPos.y < yMin - projRadius || projPos.y > yMax + projRadius) return false;

                        Vector2 dxz = new Vector2(projPos.x - center.x, projPos.z - center.z);
                        float r = playerRadius + projRadius;
                        return dxz.sqrMagnitude <= r * r;
                    }

                default:
                    return false;
            }
        }

        private static void DisposeManaged(ref ProjectileEntry p)
        {
            // Giữ đúng logic cũ: dispose mover + attacker khi remove
            p.Mover?.Dispose();
            p.Attacker?.Dispose();
            p.Mover = null;
            p.Attacker = null;
        }

        private static void TryDespawnProjectile(Transform projectileTransform)
        {
            if (projectileTransform == null) return;
            if (!projectileTransform.gameObject.activeInHierarchy) return;

            var poolEntity = projectileTransform.GetComponent<GamePlay.Entities.PoolEntity>();
            if (poolEntity != null)
            {
                poolEntity.Despawn();
            }
        }
    }
}

using System.Collections.Generic;
using GamePlay.CollisionSystems;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.CombatSystems
{
    [DisallowMultipleComponent]
    public class CombatSystem : MonoBehaviour
    {
        public static CombatSystem Instance { get; private set; }

        [Header("Broad Phase")]
        [SerializeField, Min(0.5f)] private float broadPhaseRangeX = 8f;
        [SerializeField, Min(1f)] private float broadPhaseRangeZ = 16f;
        [SerializeField, Min(0f)] private float broadPhasePadding = 1f;

        public static void Register(Transform unitTransform, CapabilityPack pack, CapabilityFlags flags)
        {
            if (Instance == null) return;
            Instance.RegisterInternal(unitTransform, pack, flags);
        }

        private class ManagedActorRefs
        {
            public Transform Transform;
            public IMover Mover;
            public IAttacker Attacker;
            public IJumper Jumper;

            public Vector3 StartPosition;
            public float StartTime;
            public float Duration;
            public Vector3 Direction;
            public Vector3 NormalizedDirection;
            public float MaxDistance;
        }

        private readonly List<ManagedActorRefs> _actors = new List<ManagedActorRefs>();

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

        /// <summary>
        /// Call this from a manager (e.g., GameplayManager.Update) in playable.
        /// </summary>
        public void ManualUpdate()
        {
            if (CollisionSystem.Instance == null) return;

            CollisionSystem.Instance.EnsureDataIsReady();

            int i = 0;
            while (i < _actors.Count)
            {
                var actor = _actors[i];

                if (actor == null || actor.Transform == null || !actor.Transform.gameObject.activeInHierarchy)
                {
                    RemoveAtSwapBack(i);
                    continue;
                }

                bool movementFinished = UpdateMovement(actor);

                bool hasHit = false;
                IHitable hitTarget = null;

                if (actor.Attacker != null)
                {
                    hasHit = TryFindHitTarget(actor, out hitTarget);
                }

                if (hasHit && hitTarget != null && hitTarget.IsActive)
                {
                    if (actor.Jumper != null)
                    {
                        var targetCollider = hitTarget.GetColliderData();
                        if ((actor.Jumper.TargetMask & targetCollider.CategoryBits) != 0)
                        {
                            actor.Jumper.OnJumpSucceed(hitTarget);
                            RemoveAtSwapBack(i);
                            continue;
                        }
                    }

                    actor.Attacker?.OnAttackSucceed(hitTarget);
                    hitTarget.OnHit(actor.Attacker);

                    RemoveAtSwapBack(i);
                    continue;
                }

                if (movementFinished)
                {
                    actor.Mover?.OnMovementFinished();
                    RemoveAtSwapBack(i);
                    continue;
                }

                i++;
            }
        }

        // ================== INTERNAL ==================

        private void RegisterInternal(Transform unitTransform, CapabilityPack pack, CapabilityFlags flags)
        {
            if (unitTransform == null) return;

            var refs = new ManagedActorRefs
            {
                Transform = unitTransform
            };

            if ((flags & CapabilityFlags.Move) != 0 && pack.Mover != null)
            {
                refs.Mover = pack.Mover;
                refs.StartPosition = unitTransform.position;
                refs.StartTime = Time.time;
                refs.Duration = Mathf.Max(0.0001f, refs.Mover.Duration);
                refs.Direction = refs.Mover.MoveDirection;
                refs.NormalizedDirection = refs.Direction.sqrMagnitude > 0f ? refs.Direction.normalized : Vector3.forward;
                refs.MaxDistance = refs.Mover.MaxDistance;
            }

            if ((flags & CapabilityFlags.Attack) != 0)
            {
                refs.Attacker = pack.Attacker;
            }

            if ((flags & CapabilityFlags.Jump) != 0)
            {
                refs.Jumper = pack.Jumper;
            }

            _actors.Add(refs);
        }

        private bool UpdateMovement(ManagedActorRefs actor)
        {
            if (actor.Mover == null) return false;

            float elapsed = Time.time - actor.StartTime;
            float t = elapsed / actor.Duration;

            if (t >= 1f)
            {
                actor.Transform.position = actor.StartPosition + actor.NormalizedDirection * actor.MaxDistance;
                return true;
            }

            actor.Transform.position = actor.StartPosition + actor.NormalizedDirection * (actor.MaxDistance * t);
            return false;
        }

        private bool TryFindHitTarget(ManagedActorRefs actor, out IHitable hitTarget)
        {
            hitTarget = null;

            var collisionSystem = CollisionSystem.Instance;
            if (collisionSystem == null) return false;
            if (actor.Attacker == null) return false;

            int targetCount = collisionSystem.Count;
            if (targetCount <= 0) return false;

            Vector3 actorPos = actor.Transform.position;

            // FIX: Size là Vector2, dùng magnitude hoặc max component
            float attackerSize = Mathf.Max(actor.Attacker.Size.x, actor.Attacker.Size.y);

            // FIX: TargetMask thay vì TargetMaskBits
            uint attackerMask = actor.Attacker.TargetMask;

            // [FIX] Warning nếu attackerMask = 0 (sẽ không hit được gì)
            if (attackerMask == 0)
            {
                if (Time.frameCount % 300 == 0)
                {
                    Debug.LogWarning($"[CombatSystem] WARNING: {actor.Transform.name} has attackerMask=0! " +
                                     $"No targets will be hit. Check AttackComponent.targetMask or attackTarget setting.");
                }
                return false;
            }

            for (int idx = 0; idx < targetCount; idx++)
            {
                var target = collisionSystem.GetTargetBySortedIndex(idx);
                if (target == null || !target.IsActive) continue;

                // Fast mask check from cached category bits before touching collider data.
                uint targetMask = collisionSystem.GetMask(idx);
                if ((attackerMask & targetMask) == 0) continue;

                var targetTr = collisionSystem.GetTransform(idx);
                if (targetTr == null) continue;

                // Coarse culling: skip far targets before narrow phase math.
                Vector3 targetPos = targetTr.position;
                float maxDx = broadPhaseRangeX + attackerSize + broadPhasePadding;
                float maxDz = broadPhaseRangeZ + attackerSize + broadPhasePadding;
                if (Mathf.Abs(targetPos.x - actorPos.x) > maxDx) continue;
                if (Mathf.Abs(targetPos.z - actorPos.z) > maxDz) continue;

                var col = collisionSystem.GetColliderData(idx);

                // Improved Collision Logic for Box Shapes (Fixes "Wide but Thin" detection)
                bool isHit = false;
                
                if (col.Type == ShapeType.Box)
                {
                    // AABB Check (Axis-Aligned Bounding Box)
                    // Treat target as AABB, attacker as Sphere
                    // Expand Box by attackerRadius
                    float distX = Mathf.Abs(actorPos.x - targetTr.position.x);
                    float distZ = Mathf.Abs(actorPos.z - targetTr.position.z);

                    // [FIX] col.Size is ALREADY half-extents from GetColliderData()!
                    // DO NOT multiply by 0.5f again!
                    float extentX = Mathf.Abs(col.Size.x);
                    float extentZ = Mathf.Abs(col.Size.z);

                    // Check intersection
                    if (distX <= (extentX + attackerSize) && distZ <= (extentZ + attackerSize))
                    {
                        isHit = true;
                    }

                }
                else if (col.Type == ShapeType.Cylinder)
                {
                    // [FIX] Cylinder Check: XZ plane circle collision (ignore Y axis)
                    // For Cylinder: Size.x = radius, Size.y = half-height
                    float targetRadius = Mathf.Abs(col.Size.x);
                    float combinedRadius = attackerSize + targetRadius;

                    // Calculate XZ plane distance only (ignore Y)
                    float dx = actorPos.x - targetTr.position.x;
                    float dz = actorPos.z - targetTr.position.z;
                    float distXZSq = dx * dx + dz * dz;

                    if (distXZSq <= combinedRadius * combinedRadius)
                    {
                        isHit = true;
                    }

                }
                else
                {
                    // Fallback to Sphere Check for others
                    float targetRadius = ApproxColliderRadius(col);
                    float r = attackerSize + targetRadius;
                    Vector3 d = targetTr.position - actorPos;
                    if (d.sqrMagnitude <= r * r)
                    {
                        isHit = true;
                    }
                }

                if (isHit)
                {
                    hitTarget = target;
                    return true;
                }
            }

            return false;
        }

        private float ApproxColliderRadius(ColliderData col)
        {
            float ex = Mathf.Abs(col.Size.x);
            float ey = Mathf.Abs(col.Size.y);
            float ez = Mathf.Abs(col.Size.z);
            return Mathf.Max(ex, Mathf.Max(ey, ez));
        }

        private void RemoveAtSwapBack(int index)
        {
            int last = _actors.Count - 1;
            if (index < 0 || index > last) return;

            _actors[index] = _actors[last];
            _actors.RemoveAt(last);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.Characters;
using GamePlay.ComponentSystems;
using UnityEngine;
using GamePlay.Crushers;

namespace GamePlay.Items
{
    public class CapacityIncreaseGate : StatModifierItem<CapacityIncreaseGateData>
    {
        [Header("Spawn Settings")]
        [SerializeField] private Transform[] slots;

        [Header("Playable Options")]
        [Tooltip("Nếu gate đã full slot thì có nuốt (despawn) belt không?")]
        [SerializeField] private bool despawnBeltWhenFull = true;

        private readonly Dictionary<int, List<CharacterUnit>> _beltUnits = new Dictionary<int, List<CharacterUnit>>();
        private int _beltUnitCount;
        private bool _hasCollided = false;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            Data.Type = StatType.Character;

            // [FIX] Auto-set EntityType for Gate
            if (_entityType == GamePlay.Entities.EntityType.None)
            {
                _entityType = GamePlay.Entities.EntityType.CapacityGate;
            }
        }
#endif

        private void Awake()
        {
            // [FIX] Ensure EntityType is CapacityGate at runtime
            if (_entityType == GamePlay.Entities.EntityType.None)
            {
                _entityType = GamePlay.Entities.EntityType.CapacityGate;
                Debug.LogWarning($"[CapacityIncreaseGate] {gameObject.name} had EntityType.None! Auto-set to CapacityGate.");
            }
        }

        public override void Initialize()
        {
            _hasCollided = false; // Reset lock on init

            // [FIX] Ensure collider size is large enough for Wheel hit (Gate is tall/wide)
            // Only fallback to default if inspector size is invalid/zero.
            if (colliderSize.x <= 0f || colliderSize.y <= 0f || colliderSize.z <= 0f)
                colliderSize = new Vector3(5f, 5f, 5f);

            // [FIX] Capacity gate TextMesh should respect depth (avoid overlaying front objects).
            ApplyDepthToTexts();

            ClearBelts();

            // --- REDUNDANT COLLIDER REMOVED (Migrated to CollisionSystem) ---
            // Gate detection is now handled by WheelUnit via CollisionSystem iteration.

            /*
            _entityType = GamePlay.Entities.EntityType.CapacityGate;
            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();

            col.size = colliderSize; // Gate size roughly 3x3
            col.isTrigger = true;
            col.enabled = true;
            */

            // Gọi base của ItemUnit (skip StatModifierItem vì nó sẽ gọi AdjustStatModifierValue không cần thiết)
            // Chúng ta override hoàn toàn để kiểm soát flow
            base.Initialize();

            // [FIX] Ensure base value is added to RequestDataList so "Normal Gates" work
            // MUST be done AFTER base.Initialize() because base.Initialize calls ResetStatModifierValue() which clears the list!
            if (Data.RequestDataList == null)
                Data.RequestDataList = new List<CardSpawnRequestData>();
            else
                Data.RequestDataList.Clear(); // [FIX] Ensure clean state from Pool

            // [FIX] Prevent x2 Card Issue:
            // Do NOT pre-fill RequestDataList with base Value.
            // Only 'AddCharacter' should populate the list for collection gates.
            // If this is a static reward gate (no factory), Data.Value is usually sufficient, 
            // but for a dynamic gate, this caused doubling.
            // We assume 'CapacityIncreaseGate' is primarily for collection.

            /*
            // Only add default if list is empty but Value is set
            if (Data.RequestDataList.Count == 0 && Data.Value > 0)
            {
                // Assign base value to Level 1, ensuring the gate has rewards even without factory units
                Data.AdjustValue(1, Data.Value);
            }
            */

            // [REFERENCE CODE FLOW]:
            // Gate trong game gốc KHÔNG tự có Value ban đầu.
            // Flow đúng: Factory spawn characters → ConveyorBelt mang đến Gate → Gate.AddCharacter()
            // → RequestDataList được populate → Wheel hit Gate → Cards được add vào Wheel
            //
            // Nếu wheel hit Gate mà không có Factory phía trước, RequestDataList sẽ trống
            // và không có cards nào được add. Đây là behavior đúng của game gốc.

            // keep single base.Initialize() call above; avoid duplicate event/collision registration.
        }

        private void ApplyDepthToTexts()
        {
            StartCoroutine(FixTextDepthDelayed());
        }

        private IEnumerator FixTextDepthDelayed()
        {
            // [FIX] Wait for TMP/Luna initialization to finish
            yield return null;

            var texts = GetComponentsInChildren<TMPro.TMP_Text>(true);
            if (texts == null || texts.Length == 0) yield break;

            MaterialPropertyBlock mbp = new MaterialPropertyBlock();

            foreach (var t in texts)
            {
                if (t == null) continue;

                t.isOverlay = false;

                // Force update to ensure renderer is live
                t.ForceMeshUpdate();

                var renderer = t.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // 1. Force ZTest via Property Block
                    renderer.GetPropertyBlock(mbp);
                    mbp.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                    renderer.SetPropertyBlock(mbp);

                    // 2. Reset Sorting Order
                    renderer.sortingOrder = 0;

                    // Avoid material instancing via renderer.material (memory growth on repeated init).
                    var shared = renderer.sharedMaterial;
                    if (shared != null && shared.renderQueue != 3000)
                    {
                        shared.renderQueue = 3000;
                    }
                }
            }
        }

        private void ClearBelts()
        {
            _beltUnitCount = 0;

            foreach (var subList in _beltUnits.Values)
            {
                if (subList == null) continue;

                int innerCount = subList.Count;
                for (int j = 0; j < innerCount; j++)
                {
                    var unit = subList[j];
                    if (unit == null) continue;

                    unit.Transform.parent = null;
                    unit.Transform.localScale = Vector3.one;
                    unit.Despawn();
                }

                subList.Clear();
            }

            _beltUnits.Clear();
        }

        protected override void HandleWheelCollision()
        {
            if (_hasCollided) return; // [FIX] Strict Lock
            _hasCollided = true;

            if (Data.RequestDataList != null && Data.RequestDataList.Count > 0)
            {
            }
            else
            {
                Debug.LogWarning($"[Gate] WARNING: RequestDataList is EMPTY or NULL! Cards will NOT be added!");
            }
            GameplayManager.Instance.ChangeStatModifierData(Data);
            DespawnInterval();
        }

        protected override void HandleNonWheelCollision(IAttacker source) { }

        protected override void DespawnInterval()
        {
            ClearBelts();
            base.DespawnInterval();
        }

        public Transform AddCharacter(CharacterUnit belt)
        {
            // 0) Safety checks
            if (belt == null)
            {
                Debug.LogWarning($"[Gate] AddCharacter ABORTED: belt is null!");
                return null;
            }
            if (slots == null || slots.Length == 0)
            {
                Debug.LogWarning($"[Gate] AddCharacter ABORTED: slots is null or empty!");
                return null;
            }

            // 1) Full gate => refuse (avoid crash)
            if (_beltUnitCount >= slots.Length)
            {
                if (despawnBeltWhenFull)
                {
                    belt.Transform.parent = null;
                    belt.Transform.localScale = Vector3.one;
                    belt.Despawn();
                }
                return null;
            }

            // 2) Cache list theo level
            if (!_beltUnits.TryGetValue(belt.Level, out var list))
            {
                list = new List<CharacterUnit>();
                _beltUnits.Add(belt.Level, list);
            }

            list.Add(belt);

            // 3) Increase count + update data
            _beltUnitCount++;
            AdjustStatModifierValue(belt.Level, 1);

            // 4) Return slot for conveyor jump target
            var targetSlot = slots[_beltUnitCount - 1];
            return targetSlot;
        }

        private void AdjustStatModifierValue(int level, int amount)
        {
            Data.AdjustValue(level, amount);
        }
    }
}

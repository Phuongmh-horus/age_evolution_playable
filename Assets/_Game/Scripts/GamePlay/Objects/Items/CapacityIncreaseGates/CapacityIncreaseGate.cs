using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.CardSystem;
using GamePlay.Characters;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.Items
{
    public class CapacityIncreaseGate : StatModifierItem<CapacityIncreaseGateData>
    {
        [Header("Spawn Settings")]
        [SerializeField] private Transform[] slots;

        [Header("Playable Options")]
        [Tooltip("Nếu gate đã full slot thì có nuốt (despawn) belt không?")]
        [SerializeField] private bool despawnBeltWhenFull = true;

        [Header("Gold Gate Settings")]
        [SerializeField] private Transform rootAnimTrans;
        [SerializeField] private List<IncreaseElement> increaseElements;
        [SerializeField] private float goldDrainDuration = 1.5f;
        [SerializeField] private float phase3Duration = 0.5f;

        private readonly Dictionary<int, List<CharacterUnit>> _beltUnits = new Dictionary<int, List<CharacterUnit>>();
        private int _beltUnitCount;
        private bool _hasCollided = false; // [FIX] Prevent Double Collision

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

            // Sync Data.ElementDataList to increaseElements
            if (Data != null && Data.ElementDataList != null && increaseElements != null)
            {
                int count = Mathf.Min(Data.ElementDataList.Count, increaseElements.Count);
                for (int i = 0; i < count; i++)
                {
                    if (increaseElements[i] != null)
                        increaseElements[i].SetElementData(Data.ElementDataList[i]);
                }
            }

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


        [ContextMenu("TEST: Add Dummy Belt (Level 1)")]
        protected override void HandleWheelCollision()
        {
            if (_hasCollided) return;
            _hasCollided = true;
            StartCoroutine(CollisionSequence());
        }

        private IEnumerator CollisionSequence()
        {
            // Phase 1: select element based on gold, assign Data, update visual
            int gold = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
            IncreaseElement selected = GetBestEligibleElement(gold);
            if (selected == null)
            {


                // Phase 3: tip RootAnimTrans 90° then apply config
                yield return StartCoroutine(Phase3());
                EndOfPhase();
                yield break;
            }

            selected.SetActiveVisual();

            // Cache initial distance for Phase 2
            float distanceOffset = 0f;
            if (rootAnimTrans != null)
            {
                Transform playerTrans = GameplayManager.Instance.PlayerTransform;
                if (playerTrans != null)
                    distanceOffset = rootAnimTrans.position.z - playerTrans.position.z;
            }

            // Phase 2: follow player Z + drain gold animation
            yield return StartCoroutine(Phase2(selected, distanceOffset));

            if (selected.LevelCard > 0)
            {
                selected.RefreshByLevelCard();
                GameplayManager.Instance.ChangeStatModifierData(selected.StatData);
                GameplayManager.Instance.RunUpgradeEffect();
                WeaponCardSystem.Instance?.PlayCollectAnimation(
                    selected.ElementData, selected.LevelCard, selected.transform);
            }

            // Phase 3: tip RootAnimTrans 90° then apply config
            yield return StartCoroutine(Phase3());
            EndOfPhase();

            void EndOfPhase()
            {


                ClearBelts();
                // DespawnInterval();
            }
        }

        private IEnumerator Phase2(IncreaseElement element, float distanceOffset)
        {
            int totalGold = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
            if (totalGold <= 0) yield break;

            Transform playerTrans = GameplayManager.Instance.PlayerTransform;
            int spendPerFrame = Mathf.Max(1, Mathf.CeilToInt(totalGold / (goldDrainDuration * 60f)));

            int levelIndex = 0;
            while (GameplayManager.Instance.GetCurrency(CurrencyType.Gold) > 0)
            {
                int cycleGoldSpent = 0;
                int targetGoldPerCycle = element.GoldCost;
                element.InitProgress(targetGoldPerCycle);

                while (cycleGoldSpent < targetGoldPerCycle && GameplayManager.Instance.GetCurrency(CurrencyType.Gold) > 0)
                {
                    if (rootAnimTrans != null && playerTrans != null)
                    {
                        Vector3 pos = rootAnimTrans.position;
                        pos.z = playerTrans.position.z + distanceOffset;
                        rootAnimTrans.position = pos;
                    }

                    int goldBefore = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
                    GameplayManager.Instance.TrySpendCurrency(CurrencyType.Gold, spendPerFrame);
                    int goldAfter = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
                    int goldSpent = goldBefore - goldAfter;
                    cycleGoldSpent += goldSpent;

                    element.UpdateProgress(cycleGoldSpent);
                    yield return null;
                }

                if (cycleGoldSpent >= targetGoldPerCycle)
                {
                    levelIndex++;
                    element.UpdateLevelCard(levelIndex);
                    element.UpdateProgress(0);
                }
            }
        }

        private IEnumerator Phase3()
        {
            if (rootAnimTrans == null) yield break;

            Quaternion from = rootAnimTrans.localRotation;
            Quaternion to = from * Quaternion.Euler(90f, 0f, 0f);
            float elapsed = 0f;

            while (elapsed < phase3Duration)
            {
                elapsed += Time.deltaTime;
                rootAnimTrans.localRotation = Quaternion.Lerp(from, to, elapsed / phase3Duration);
                yield return null;
            }

            rootAnimTrans.localRotation = to;
        }

        private IncreaseElement GetBestEligibleElement(int gold)
        {
            IncreaseElement best = null;
            foreach (var element in increaseElements)
            {
                if (element == null || !element.IsEligible(gold)) continue;
                if (best == null || element.GoldCost > best.GoldCost)
                    best = element;
            }
            return best;
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

            // 3) Increase count
            _beltUnitCount++;

            // 4) Return slot for conveyor jump target
            var targetSlot = slots[_beltUnitCount - 1];
            return targetSlot;
        }
    }
}

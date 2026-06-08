using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.CardSystem;
using GamePlay.Characters;
using GamePlay.ComponentSystems;
using GamePlay.Effects;
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
        [SerializeField] private float goldDrainDuration = 3.5f;
        [SerializeField] private float goldDrainEffectInterval = 0.12f;
        [SerializeField] private float phase3Duration = 0.75f;
        [SerializeField, Range(0.1f, 1f)] private float goldDrainTimeScale = 0.75f;

        [Header("Buff Applied Effect")]
        [SerializeField] private EffectType buffAppliedEffectType = EffectType.Upgrade;


        private readonly Dictionary<int, List<CharacterUnit>> _beltUnits = new Dictionary<int, List<CharacterUnit>>();
        private int _beltUnitCount;
        private bool _hasCollided = false; // [FIX] Prevent Double Collision
        private readonly List<IncreaseElement> _eligibleElementsBuffer = new List<IncreaseElement>(8);
        private readonly List<IncreaseElement> _alreadyAppliedBuffElementsBuffer = new List<IncreaseElement>(8);
        private readonly Dictionary<IncreaseElement, int> _upgradeByElementBuffer = new Dictionary<IncreaseElement, int>(8);
        private readonly HashSet<IncreaseElement> _exhaustedElementsBuffer = new HashSet<IncreaseElement>();
        private readonly List<UpgradeResolution> _upgradeResolutionBuffer = new List<UpgradeResolution>(8);
        private struct UpgradeResolution
        {
            public IncreaseElement Element;
            public int UpgradeLevels;

            public UpgradeResolution(IncreaseElement element, int upgradeLevels)
            {
                Element = element;
                UpgradeLevels = upgradeLevels;
            }
        }

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
            if (_entityType == GamePlay.Entities.EntityType.None)
            {
                _entityType = GamePlay.Entities.EntityType.CapacityGate;
                Debug.LogWarning($"[CapacityIncreaseGate] {gameObject.name} had EntityType.None! Auto-set to CapacityGate.");
            }
        }

        public override void Initialize()
        {
            _hasCollided = false; // Reset lock on init
            EnsureGateSetup();

            // Only fallback to default if inspector size is invalid/zero.
            if (colliderSize.x <= 0f || colliderSize.y <= 0f || colliderSize.z <= 0f)
                colliderSize = new Vector3(5f, 5f, 5f);

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

            base.Initialize();
            // keep single base.Initialize() call above; avoid duplicate event/collision registration.
        }

        private void EnsureGateSetup()
        {
            if (increaseElements == null)
            {
                increaseElements = new List<IncreaseElement>();
            }

            if (increaseElements.Count == 0)
            {
                var childElements = GetComponentsInChildren<IncreaseElement>(true);
                if (childElements != null && childElements.Length > 0)
                {
                    increaseElements.AddRange(childElements);
                }
            }

            if (Data == null)
            {
                Data = new CapacityIncreaseGateData();
            }

            Data.Type = StatType.Character;

            if (Data.ElementDataList == null || Data.ElementDataList.Count == 0)
            {
                Data.ElementDataList = BuildDefaultElementDataList();
            }

        }

        private static List<IncreaseElementData> BuildDefaultElementDataList()
        {
            return new List<IncreaseElementData>
            {
                new IncreaseElementData
                {
                    Type = StatType.Character,
                    Value = 1,
                    ValueUpgrade = 1,
                    StartLevel = 0,
                    Cost = 30,
                    UpgradeRequire = 50
                },
                new IncreaseElementData
                {
                    Type = StatType.Damage,
                    Value = 12,
                    ValueUpgrade = 5,
                    StartLevel = 0,
                    Cost = 30,
                    UpgradeRequire = 35
                }
            };
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
            if (increaseElements != null)
            {
                for (int i = 0; i < increaseElements.Count; i++)
                {
                    if (increaseElements[i] != null)
                    {
                        increaseElements[i].SetNormalVisual();
                    }
                }
            }

            // Phase 1: random an eligible element based on current gold
            int gold = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
            IncreaseElement selected = GetRandomEligibleElement(gold);
            if (selected == null)
            {


                // Phase 3: tip RootAnimTrans 90° then apply config
                yield return StartCoroutine(Phase3());
                EndOfPhase();
                yield break;
            }

            // Cache initial distance for Phase 2
            float distanceOffset = 0f;
            if (rootAnimTrans != null)
            {
                Transform playerTrans = GameplayManager.Instance.PlayerTransform;
                if (playerTrans != null)
                    distanceOffset = rootAnimTrans.position.z - playerTrans.position.z;
            }

            // Phase 2: follow player Z + drain gold (logic first, do not update upgrade UI yet)
            List<UpgradeResolution> upgradeResolutions = null;
            yield return StartCoroutine(Phase2(selected, distanceOffset, delegate (List<UpgradeResolution> results)
            {
                upgradeResolutions = results;
            }));

            if (upgradeResolutions != null)
            {
                for (int i = 0; i < upgradeResolutions.Count; i++)
                {
                    var result = upgradeResolutions[i];
                    if (result.Element == null || result.UpgradeLevels <= 0)
                    {
                        continue;
                    }

                    if (result.Element.ElementData != null &&
                        result.Element.ElementData.Type == StatType.ExplosionShot &&
                        GameplayManager.Instance.CanOfferExplosionShotThisRun())
                    {
                        GameplayManager.Instance.MarkExplosionShotOffered();
                    }

                    // [FIX] Update LevelCard and refresh Value BEFORE casting/applying StatData
                    // so that StatData.Value reflects the upgraded level, not the base level.
                    result.Element.UpdateLevelCard(result.Element.LevelCard + result.UpgradeLevels);
                    result.Element.RefreshByLevelCard();

                    var gateStatData = result.Element.StatData as CapacityIncreaseGateData;
                    if (gateStatData != null)
                    {
                        // [FIX] UpgradeSteps must be set for types that need it (Character, etc.)
                        // For Damage/ExplosionShot, Value from RefreshByLevelCard is what matters.
                        gateStatData.UpgradeSteps = result.UpgradeLevels;
                    }

                    GameplayManager.Instance.ChangeStatModifierData(result.Element.StatData);
                    GameplayManager.Instance.RunUpgradeEffect(result.Element.transform);
                    WeaponCardSystem.Instance?.PlayCollectAnimation(
                        result.Element.ElementData, result.Element.LevelCard, result.Element.transform);
                    Pack.Effector?.PlayEffect(
                        buffAppliedEffectType,
                        result.Element.transform.position,
                        Quaternion.identity,
                        result.Element.transform);
                }
            }

            // Phase 3: tip RootAnimTrans 90° then apply config
            yield return StartCoroutine(Phase3());
            EndOfPhase();

            void EndOfPhase()
            {
                Pack.Effector?.StopEffect(EffectType.Land);

                ClearBelts();
                // DespawnInterval();
            }
        }

        private IEnumerator Phase2(IncreaseElement selectedElement, float distanceOffset, Action<List<UpgradeResolution>> onResolved)
        {
            int totalGold = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
            if (totalGold <= 0)
            {
                onResolved?.Invoke(null);
                yield break;
            }

            Transform playerTrans = GameplayManager.Instance.PlayerTransform;
            int spendPerFrame = Mathf.Max(1, Mathf.CeilToInt(totalGold / (goldDrainDuration * 60f)));
            _upgradeByElementBuffer.Clear();
            _exhaustedElementsBuffer.Clear();
            _upgradeResolutionBuffer.Clear();
            IncreaseElement activeElement = selectedElement;
            int currentUpgradeSpent = 0;
            int nextUpgradeCost = 0;
            float goldDrainFxTimer = 0f;

            if (!TryActivateElement(activeElement, out nextUpgradeCost))
            {
                activeElement = GetNextEligibleElement(GameplayManager.Instance.GetCurrency(CurrencyType.Gold), _exhaustedElementsBuffer, null);
                if (!TryActivateElement(activeElement, out nextUpgradeCost))
                {
                    onResolved?.Invoke(null);
                    yield break;
                }
            }

            // Làm chậm thời gian để người chơi có cảm giác "hồi hộp" khi vàng đang được sử dụng
            float originalTimeScale = Time.timeScale;
            Time.timeScale = goldDrainTimeScale;

            while (GameplayManager.Instance.GetCurrency(CurrencyType.Gold) > 0)
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
                int spent = goldBefore - goldAfter;

                if (spent <= 0)
                {
                    break;
                }

                currentUpgradeSpent += spent;
                goldDrainFxTimer += Time.deltaTime;

                if (goldDrainFxTimer >= goldDrainEffectInterval && activeElement != null)
                {
                    goldDrainFxTimer = 0f;
                    activeElement.ShowGoldDrainFeedback();
                    Pack.Effector?.PlayEffect(
                        EffectType.Land,
                        activeElement.transform.position,
                        Quaternion.identity,
                        activeElement.transform);
                }

                while (currentUpgradeSpent >= nextUpgradeCost)
                {
                    currentUpgradeSpent -= nextUpgradeCost;
                    if (!_upgradeByElementBuffer.TryGetValue(activeElement, out int upgradedLevels))
                    {
                        upgradedLevels = 0;
                    }
                    upgradedLevels++;
                    _upgradeByElementBuffer[activeElement] = upgradedLevels;

                    int virtualLevel = activeElement.LevelCard + upgradedLevels;
                    nextUpgradeCost = activeElement.GetUpgradeCostForLevel(virtualLevel);
                    if (nextUpgradeCost == int.MaxValue)
                    {
                        _exhaustedElementsBuffer.Add(activeElement);
                        activeElement = GetNextEligibleElement(0, _exhaustedElementsBuffer, activeElement);
                        if (!TryActivateElement(activeElement, out nextUpgradeCost))
                        {
                            currentUpgradeSpent = 0;
                            break;
                        }
                    }
                }

                if (activeElement == null)
                {
                    break;
                }

                activeElement.InitProgress(nextUpgradeCost);
                activeElement.UpdateProgress(currentUpgradeSpent);
                yield return null;
            }

            // Khôi phục lại tốc độ game
            Time.timeScale = originalTimeScale;

            if (_upgradeByElementBuffer.Count == 0)
            {
                onResolved?.Invoke(null);
                yield break;
            }

            foreach (var pair in _upgradeByElementBuffer)
            {
                if (pair.Key != null && pair.Value > 0)
                {
                    _upgradeResolutionBuffer.Add(new UpgradeResolution(pair.Key, pair.Value));
                }
            }
            onResolved?.Invoke(_upgradeResolutionBuffer);

            bool TryActivateElement(IncreaseElement element, out int upgradeCost)
            {
                upgradeCost = int.MaxValue;
                if (element == null)
                {
                    return false;
                }

                element.SetActiveVisual();
                upgradeCost = element.GetNextUpgradeCost();
                if (upgradeCost == int.MaxValue)
                {
                    element.SetNormalVisual();
                    _exhaustedElementsBuffer.Add(element);
                    return false;
                }

                element.InitProgress(upgradeCost);
                element.UpdateProgress(currentUpgradeSpent);
                return true;
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
            SoundManager.Instance?.PlayOneShot(AudioClipName.SFX_Ingame_Hero_Upgrade);
        }

        private IncreaseElement GetRandomEligibleElement(int gold)
        {
            _eligibleElementsBuffer.Clear();
            _alreadyAppliedBuffElementsBuffer.Clear();

            if (increaseElements == null || increaseElements.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < increaseElements.Count; i++)
            {
                var element = increaseElements[i];
                if (element == null || !element.IsEligible(gold))
                {
                    continue;
                }
                if (IsBlockedExplosionElement(element))
                {
                    continue;
                }

                AddEligibleByBuffPriority(element);
            }

            return PickRandomFromPrioritizedEligible();
        }

        private IncreaseElement GetNextEligibleElement(int gold, HashSet<IncreaseElement> exhaustedElements, IncreaseElement currentElement)
        {
            _eligibleElementsBuffer.Clear();
            _alreadyAppliedBuffElementsBuffer.Clear();
            if (increaseElements == null || increaseElements.Count == 0)
            {
                return null;
            }

            StatType currentType = currentElement != null && currentElement.ElementData != null
                ? currentElement.ElementData.Type
                : StatType.None;

            for (int i = 0; i < increaseElements.Count; i++)
            {
                var element = increaseElements[i];
                if (element == null) continue;

                if (exhaustedElements != null && exhaustedElements.Contains(element))
                {
                    continue;
                }

                if (currentType != StatType.None && element.ElementData != null && element.ElementData.Type == currentType)
                {
                    continue;
                }
                if (IsBlockedExplosionElement(element))
                {
                    continue;
                }

                // [FIX] Don't gate on current gold here — this is called mid-drain when gold is near 0.
                // Only check that the element hasn't hit its max level (cost == MaxValue means maxed).
                if (element.GetNextUpgradeCost() == int.MaxValue)
                {
                    continue;
                }

                AddEligibleByBuffPriority(element);
            }

            return PickRandomFromPrioritizedEligible();
        }

        private void AddEligibleByBuffPriority(IncreaseElement element)
        {
            if (element == null)
            {
                return;
            }

            if (IsPrimaryBuffAlreadyApplied(element))
            {
                _alreadyAppliedBuffElementsBuffer.Add(element);
                return;
            }

            _eligibleElementsBuffer.Add(element);
        }

        private IncreaseElement PickRandomFromPrioritizedEligible()
        {
            List<IncreaseElement> source = _eligibleElementsBuffer.Count > 0
                ? _eligibleElementsBuffer
                : _alreadyAppliedBuffElementsBuffer;

            if (source.Count == 0)
            {
                return null;
            }

            int randomIndex = UnityEngine.Random.Range(0, source.Count);
            return source[randomIndex];
        }

        private static bool IsPrimaryBuffAlreadyApplied(IncreaseElement element)
        {
            if (element == null || element.ElementData == null)
            {
                return false;
            }

            StatType type = element.ElementData.Type;
            if (!IsPrimaryBuffType(type))
            {
                return false;
            }

            var gameplayManager = GameplayManager.Instance;
            return gameplayManager != null && gameplayManager.HasAppliedPrimaryBuffThisRun(type);
        }

        private static bool IsPrimaryBuffType(StatType statType)
        {
            return statType == StatType.Character ||
                   statType == StatType.FireRange ||
                   statType == StatType.Damage;
        }

        private static bool IsBlockedExplosionElement(IncreaseElement element)
        {
            if (element == null || element.ElementData == null)
            {
                return false;
            }

            if (element.ElementData.Type != StatType.ExplosionShot)
            {
                return false;
            }

            var gameplayManager = GameplayManager.Instance;
            return gameplayManager != null && !gameplayManager.CanOfferExplosionShotThisRun();
        }

        protected override void HandleNonWheelCollision(IAttacker source) { }

        protected override void DespawnInterval()
        {
            Pack.Effector?.StopEffect(EffectType.Land);
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
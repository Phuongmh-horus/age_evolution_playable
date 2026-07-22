using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.CardSystem;
using GamePlay.Characters;
using GamePlay.ComponentSystems;
using GamePlay.CollisionSystems;
using GamePlay.Effects;
using DG.Tweening;
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
        [Tooltip("Force gate dau tien uu tien mo Explosion Shot. Cac gate sau chi lay Explosion Shot khi cac buff card khac da het upgrade.")]
        [SerializeField] private bool forceFirstGateExplosionShot = true;

        [Header("Gold Gate Settings")]
        [SerializeField] private Transform rootAnimTrans;
        [SerializeField] private List<IncreaseElement> increaseElements;
        [SerializeField] private float goldDrainDuration = 2.0f;
        [SerializeField] private float goldDrainEffectInterval = 0.12f;
        [SerializeField] private float phase3Duration = 0.75f;
        [SerializeField, Range(0.1f, 1f)] private float goldDrainTimeScale = 0.75f;


        private readonly Dictionary<int, List<CharacterUnit>> _beltUnits = new Dictionary<int, List<CharacterUnit>>();
        private int _beltUnitCount;
        private bool _hasCollided = false; // [FIX] Prevent Double Collision
        private readonly Dictionary<IncreaseElement, int> _upgradeByElementBuffer = new Dictionary<IncreaseElement, int>(8);
        private readonly HashSet<IncreaseElement> _exhaustedElementsBuffer = new HashSet<IncreaseElement>();
        private readonly List<UpgradeResolution> _upgradeResolutionBuffer = new List<UpgradeResolution>(8);
        private static bool _hasConsumedForcedExplosionGate;
        private EffectComponent _gateEffectComponent;
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
            if (_entityType == Entities.EntityType.None)
            {
                _entityType = Entities.EntityType.CapacityGate;
            }
        }

#endif

        public override void Initialize()
        {
            _gateEffectComponent = GetComponent<EffectComponent>();
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
#if UNITY_EDITOR
                Debug.LogWarning($"[CapacityIncreaseGate] increaseElements is empty on {name}. Please assign in Inspector to avoid runtime search!");
#endif
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
            FixTextDepthImmediate();
        }

        private void FixTextDepthImmediate()
        {
            var texts = GetComponentsInChildren<TMPro.TMP_Text>(true);
            if (texts == null || texts.Length == 0) return;

            MaterialPropertyBlock mbp = new MaterialPropertyBlock();

            foreach (var t in texts)
            {
                if (t == null) continue;

                t.isOverlay = false;
                t.ForceMeshUpdate();

                var renderer = t.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(mbp);
                    mbp.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                    renderer.SetPropertyBlock(mbp);
                    renderer.sortingOrder = 0;

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

            bool shouldForceExplosionShot = forceFirstGateExplosionShot && !_hasConsumedForcedExplosionGate;
            if (shouldForceExplosionShot)
            {
                _hasConsumedForcedExplosionGate = true;
            }

            // Phase 1: random an eligible element based on current gold
            int gold = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
            IncreaseElement selected = shouldForceExplosionShot
                ? GetEligibleExplosionShotElement(gold)
                : null;

            if (selected == null)
            {
                selected = GetRandomEligibleElement(gold);
            }

            if (selected == null)
            {
                // Phase 3: tip RootAnimTrans 90° then apply config
                yield return Phase3();
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
            yield return Phase2(selected, distanceOffset);

            if (_upgradeResolutionBuffer.Count > 0)
            {
                for (int i = 0; i < _upgradeResolutionBuffer.Count; i++)
                {
                    var result = _upgradeResolutionBuffer[i];
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

                    if (result.Element.ElementData != null && result.Element.ElementData.BuffDef != null)
                    {
                        GameplayManager.Instance.ApplySwordSkillBuff(result.Element.ElementData.BuffDef);
                        BuffCardSystem.Instance?.PlayCustomCollectAnimation(
                            result.Element.ElementData.BuffDef.VisualPrefab,
                            result.Element.ElementData,
                            result.Element.LevelCard,
                            result.Element.transform,
                            i,
                            _upgradeResolutionBuffer.Count);
                    }
                    else
                    {
                        BuffCardSystem.Instance?.PlayCollectAnimation(
                            result.Element.ElementData,
                            result.Element.LevelCard,
                            result.Element.transform,
                            i,
                            _upgradeResolutionBuffer.Count);
                    }

                }
            }

            // Phase 3: tip RootAnimTrans 90° then apply config
            yield return Phase3();
            EndOfPhase();

            void EndOfPhase()
            {
                if (_gateEffectComponent != null)
                    _gateEffectComponent.StopEffect(EffectType.Land);
                else
                    Pack.Effector?.StopEffect(EffectType.Land);

                ClearBelts();
                DespawnInterval();
            }
        }

        private void OnEnable()
        {
            transform.localScale = Vector3.one;
            _hasCollided = false;
        }

        private IEnumerator Phase2(IncreaseElement selectedElement, float distanceOffset)
        {
            int totalGold = GameplayManager.Instance.GetCurrency(CurrencyType.Gold);
            if (totalGold <= 0)
            {
                yield break;
            }

            Transform playerTrans = GameplayManager.Instance.PlayerTransform;
            int spendPerFrame = Mathf.Max(1, Mathf.CeilToInt(totalGold / (goldDrainDuration * 30f)));
            _upgradeByElementBuffer.Clear();
            _exhaustedElementsBuffer.Clear();
            _upgradeResolutionBuffer.Clear();
            IncreaseElement activeElement = selectedElement;
            int currentUpgradeSpent = 0;
            int nextUpgradeCost = 0;
            float goldDrainFxTimer = goldDrainEffectInterval;

            if (!TryActivateElement(activeElement, _exhaustedElementsBuffer, currentUpgradeSpent, out nextUpgradeCost))
            {
                activeElement = GetNextEligibleElement(GameplayManager.Instance.GetCurrency(CurrencyType.Gold), _exhaustedElementsBuffer, null);
                if (!TryActivateElement(activeElement, _exhaustedElementsBuffer, currentUpgradeSpent, out nextUpgradeCost))
                {
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
                GameplayManager.Instance.TrySpendCurrency(CurrencyType.Gold, Mathf.Min(goldBefore, spendPerFrame));
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

                    var effector = _gateEffectComponent != null ? _gateEffectComponent : Pack.Effector;
                    effector?.PlayEffect(
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
                        activeElement.SetNormalVisual();
                        activeElement = GetNextEligibleElement(0, _exhaustedElementsBuffer, activeElement);
                        if (!TryActivateElement(activeElement, _exhaustedElementsBuffer, currentUpgradeSpent, out nextUpgradeCost))
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

            if (activeElement != null)
            {
                activeElement.SetNormalVisual();
            }

            // Khôi phục lại tốc độ game
            Time.timeScale = originalTimeScale;

            if (_upgradeByElementBuffer.Count == 0)
            {
                yield break;
            }

            foreach (var pair in _upgradeByElementBuffer)
            {
                if (pair.Key != null && pair.Value > 0)
                {
                    _upgradeResolutionBuffer.Add(new UpgradeResolution(pair.Key, pair.Value));
                }
            }
        }

        private bool TryActivateElement(IncreaseElement element, HashSet<IncreaseElement> exhaustedElementsBuffer, int currentUpgradeSpent, out int upgradeCost)
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
                exhaustedElementsBuffer.Add(element);
                return false;
            }

            element.InitProgress(upgradeCost);
            element.UpdateProgress(currentUpgradeSpent);
            return true;
        }
        private IEnumerator Phase3()
        {
            if (rootAnimTrans == null) yield break;

            Quaternion from = rootAnimTrans.localRotation;
            Quaternion to = from * Quaternion.Euler(91f, 0f, 0f);

            yield return rootAnimTrans.DOLocalRotateQuaternion(to, phase3Duration).SetEase(Ease.Linear).WaitForCompletion();

            SoundManager.Instance?.PlayOneShot(AudioClipName.SFX_Ingame_Hero_Upgrade);
        }

        private IncreaseElement GetRandomEligibleElement(int gold)
        {
            if (increaseElements == null || increaseElements.Count == 0) return null;

            // Sequential priority based on Inspector order
            for (int i = 0; i < increaseElements.Count; i++)
            {
                var element = increaseElements[i];
                if (element == null) continue;
                if (element.GetNextUpgradeCost() == int.MaxValue) continue;
                if (!element.IsEligible(gold)) continue;

                if (IsBlockedExplosionElement(element)) continue;
                if (ShouldDeferExplosionShotElement(element)) continue;

                return element;
            }

            return null;
        }

        private IncreaseElement GetNextEligibleElement(int gold, HashSet<IncreaseElement> exhaustedElements, IncreaseElement currentElement)
        {
            if (increaseElements == null || increaseElements.Count == 0) return null;

            // Sequential priority based on Inspector order
            for (int i = 0; i < increaseElements.Count; i++)
            {
                var element = increaseElements[i];
                if (element == null) continue;
                if (exhaustedElements != null && exhaustedElements.Contains(element)) continue;
                if (element.GetNextUpgradeCost() == int.MaxValue) continue;

                if (IsBlockedExplosionElement(element)) continue;
                if (ShouldDeferExplosionShotElement(element)) continue;

                return element;
            }

            return null;
        }

        private IncreaseElement GetEligibleExplosionShotElement(int gold)
        {
            if (increaseElements == null || increaseElements.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < increaseElements.Count; i++)
            {
                var element = increaseElements[i];
                if (!IsExplosionShotElement(element) || !element.IsEligible(gold))
                {
                    continue;
                }

                return element;
            }

            return null;
        }

        private bool IsBlockedExplosionElement(IncreaseElement element)
        {
            if (!IsExplosionShotElement(element))
            {
                return false;
            }

            if (forceFirstGateExplosionShot)
            {
                return false;
            }

            var gameplayManager = GameplayManager.Instance;
            return gameplayManager != null && !gameplayManager.CanOfferExplosionShotThisRun();
        }

        private bool ShouldDeferExplosionShotElement(IncreaseElement element)
        {
            return forceFirstGateExplosionShot &&
                   _hasConsumedForcedExplosionGate &&
                   IsExplosionShotElement(element);
        }

        private static bool IsExplosionShotElement(IncreaseElement element)
        {
            return element != null &&
                   element.ElementData != null &&
                   element.ElementData.Type == StatType.ExplosionShot;
        }

        protected override void HandleNonWheelCollision(IAttacker source) { }

        private bool _isDespawning = false;

        protected override void DespawnInterval()
        {
            if (_isDespawning) return;
            _isDespawning = true;

            if (_gateEffectComponent != null)
                _gateEffectComponent.StopEffect(EffectType.Land);
            else
                Pack.Effector?.StopEffect(EffectType.Land);

            ClearBelts();

            if (Pack.Hitable != null)
            {
                CollisionSystem.Unregister(Pack.Hitable);
            }

            //StartCoroutine(ScaleDownRoutine());
        }

        private System.Collections.IEnumerator ScaleDownRoutine()
        {
            float elapsed = 0f;
            float duration = 0.25f;
            Vector3 startScale = transform.localScale;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, elapsed / duration);
                yield return null;
            }

            transform.localScale = Vector3.zero;
            _isDespawning = false;
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


using System.Collections;
using System.Collections.Generic;
using GamePlay.CollisionSystems;
using GamePlay.ComponentSystems;
using GamePlay.CombatSystems;
using GamePlay.Crushers;
using GamePlay.Effects;
using TMPro;
using UnityEngine;

namespace GamePlay.Items
{
    public class SwitchIncreaseGate : StatModifierItem<SwitchIncreaseGateData>
    {
        [Header("Reward Visual")]
        [SerializeField] private List<TMP_Text> rewardTexts = new List<TMP_Text>();
        [SerializeField, HideInInspector] private TMP_Text rewardText;

        [Header("Effect Visual")]
        [SerializeField] private List<EffectComponent> effectComponents = new List<EffectComponent>();
        [SerializeField, HideInInspector] private EffectComponent effectComponent;

        [Header("Hit Component")]
        [SerializeField] private HitComponent hitComponent;

        [Header("Collapse Animation")]
        [SerializeField] private Transform collapseTransform;
        [SerializeField, Min(0f)] private float collapseAngleX = 90f;
        [SerializeField, Min(0.01f)] private float collapseDuration = 1f;

        private Quaternion _originalLocalRotation;
        private Coroutine _collapseRoutine;
        private Vector3 _pendingWheelHitWorldPosition;
        private bool _isCollapsed;
        private Transform _resolvedCollapseTransform;

        protected void Awake()
        {
            if (_entityType == GamePlay.Entities.EntityType.None)
            {
                _entityType = GamePlay.Entities.EntityType.PowerGate;
            }

            if (rewardText == null)
            {
                rewardText = GetComponentInChildren<TMP_Text>(true);
            }

            if (hitComponent == null)
            {
                hitComponent = GetComponentInChildren<HitComponent>(true);
            }

            if (effectComponent == null)
            {
                effectComponent = GetComponentInChildren<EffectComponent>(true);
            }

            _resolvedCollapseTransform = collapseTransform != null ? collapseTransform : transform;
            _originalLocalRotation = _resolvedCollapseTransform.localRotation;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            _entityType = GamePlay.Entities.EntityType.PowerGate;
            Data ??= new SwitchIncreaseGateData();

            if (rewardText == null)
            {
                rewardText = GetComponentInChildren<TMP_Text>(true);
            }

            if (hitComponent == null)
            {
                hitComponent = GetComponentInChildren<HitComponent>(true);
            }

            if (effectComponent == null)
            {
                effectComponent = GetComponentInChildren<EffectComponent>(true);
            }

            EnsureRewardConfigIntegrity();
            EnsureRewardTextIntegrity();
            EnsureEffectComponentIntegrity();
            UpdateRewardTexts();
            _resolvedCollapseTransform = collapseTransform != null ? collapseTransform : transform;
            _originalLocalRotation = _resolvedCollapseTransform.localRotation;
        }
#endif

        public override void Initialize()
        {
            base.Initialize();

            Data ??= new SwitchIncreaseGateData();
            EnsureRewardConfigIntegrity();
            EnsureRewardTextIntegrity();
            EnsureEffectComponentIntegrity();

            _resolvedCollapseTransform = collapseTransform != null ? collapseTransform : transform;
            _originalLocalRotation = _resolvedCollapseTransform.localRotation;
            _isCollapsed = false;

            if (_collapseRoutine != null)
            {
                StopCoroutine(_collapseRoutine);
                _collapseRoutine = null;
            }

            if (hitComponent == null)
            {
                hitComponent = GetComponentInChildren<HitComponent>(true);
            }

            if (hitComponent != null)
            {
                if (Pack.Hitable != null && !ReferenceEquals(Pack.Hitable, hitComponent))
                {
                    RegisterEvents(false);
                    CollisionSystem.Unregister(Pack.Hitable);
                }

                Pack.Hitable = hitComponent;
                ActiveFlags |= CapabilityFlags.Hit;
                hitComponent.enabled = true;
                hitComponent.Initialize();
                CollisionSystem.Register(hitComponent, hitComponent.transform);
                RegisterEvents(true);
            }

            InitializeEffectComponents();
            UpdateRewardTexts();
        }

        protected override void HandleHitComplete(IAttacker source)
        {
            if (_isCollapsed || source == null)
            {
                return;
            }

            var previousEffector = Pack.Effector;
            if (source.EntityType == GamePlay.Entities.EntityType.Wheel)
            {
                _pendingWheelHitWorldPosition = source.Position;
                SetActiveHitEffect(ResolveRewardIndex(source.Position));
            }
            else
            {
                SetActiveHitEffect(-1);
            }

            try
            {
                base.HandleHitComplete(source);
            }
            finally
            {
                var fallbackEffector = ResolveEffectComponent(0);
                if (fallbackEffector == null)
                {
                    fallbackEffector = effectComponent;
                }

                Pack.Effector = previousEffector != null ? previousEffector : fallbackEffector;
            }
        }

        protected override void AdjustStatModifierValue(int value = 0)
        {
        }

        protected override void HandleWheelCollision()
        {
            if (_isCollapsed)
            {
                return;
            }
            StartCollapseAfterReward();

            int rewardIndex = ResolveRewardIndex();

            if (rewardIndex < 0 || Data == null || Data.RewardConfigs == null || rewardIndex >= Data.RewardConfigs.Count)
            {
                RaiseFailAnimation();
                return;
            }

            var rewardConfig = Data.RewardConfigs[rewardIndex];
            if (rewardConfig == null)
            {
                RaiseFailAnimation();
                return;
            }

            if (!CanApplyReward(rewardConfig))
            {
                RaiseFailAnimation();
                return;
            }

            var gameplayManager = GameplayManager.Instance;
            if (gameplayManager == null)
            {
                RaiseFailAnimation();
                return;
            }

            int cost = Mathf.Max(0, rewardConfig.Cost);
            if (!gameplayManager.TrySpendCurrency(CurrencyType.Gold, cost))
            {
                RaiseFailAnimation();
                return;
            }

            ApplyReward(gameplayManager, rewardConfig);
            var parentEffect = gameplayManager.PlayerTransform;
            Pack.Effector?.PlayEffect(EffectType.Land, parentEffect.position, Quaternion.identity, parentEffect);

            void RaiseFailAnimation()
            {
                Pack.Effector?.PlayEffect(EffectType.Die, transform.position, Quaternion.identity);
            }
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
                // if (_isCollapsed)
                // {
                //     return;
                // }

                // StartCollapse();
        }

        private bool CanApplyReward(SwitchIncreaseGateRewardConfig rewardConfig)
        {
            if (rewardConfig == null)
            {
                return false;
            }

            var gameplayManager = GameplayManager.Instance;
            if (gameplayManager == null)
            {
                return false;
            }

            switch (rewardConfig.RewardType)
            {
                case SwitchIncreaseGateRewardType.Stat:
                    return rewardConfig.StatModifierData != null && rewardConfig.StatModifierData.Type != StatType.None;

                case SwitchIncreaseGateRewardType.ArmyTierUpgrade:
                    return gameplayManager.ActiveArmy != null && rewardConfig.UpgradeTargetLevel > 0;

                case SwitchIncreaseGateRewardType.ArmyCount:
                    return gameplayManager.ActiveArmy != null && rewardConfig.RequestDataList != null && rewardConfig.RequestDataList.Count > 0;
            }

            return false;
        }

        private void ApplyReward(GameplayManager gameplayManager, SwitchIncreaseGateRewardConfig rewardConfig)
        {
            switch (rewardConfig.RewardType)
            {
                case SwitchIncreaseGateRewardType.Stat:
                    gameplayManager.ChangeStatModifierData(rewardConfig.StatModifierData);
                    break;

                case SwitchIncreaseGateRewardType.ArmyTierUpgrade:
                    gameplayManager.ActiveArmy?.UpgradeAllUnitsToLevel(rewardConfig.UpgradeTargetLevel);
                    break;

                case SwitchIncreaseGateRewardType.ArmyCount:
                    gameplayManager.ActiveArmy?.AddCards(rewardConfig.RequestDataList, CardSpawnEffectType.Drop);
                    break;
            }
        }

        private int ResolveRewardIndex()
        {
            return ResolveRewardIndex(_pendingWheelHitWorldPosition);
        }

        private int ResolveRewardIndex(Vector3 hitWorldPosition)
        {
            if (Data == null || Data.RewardConfigs == null)
            {
                return -1;
            }

            int rewardCount = Data.RewardConfigs.Count;
            if (rewardCount <= 0)
            {
                return -1;
            }

            float localX = transform.InverseTransformPoint(hitWorldPosition).x;
            float halfWidth = Mathf.Max(0.0001f, Mathf.Abs(colliderSize.x) * 0.5f);
            float normalized = Mathf.InverseLerp(-halfWidth, halfWidth, localX);
            return Mathf.Clamp((int)(normalized * rewardCount), 0, rewardCount - 1);
        }

        private void EnsureRewardConfigIntegrity()
        {
            if (Data == null)
            {
                Data = new SwitchIncreaseGateData();
            }

            if (Data.RewardConfigs == null)
            {
                Data.RewardConfigs = new List<SwitchIncreaseGateRewardConfig>();
            }

            for (int i = 0; i < Data.RewardConfigs.Count; i++)
            {
                var rewardConfig = Data.RewardConfigs[i];
                if (rewardConfig == null)
                {
                    rewardConfig = new SwitchIncreaseGateRewardConfig();
                    Data.RewardConfigs[i] = rewardConfig;
                }

                rewardConfig.StatModifierData ??= new StatModifierData();
                rewardConfig.RequestDataList ??= new List<CardSpawnRequestData>();
            }
        }

        private void EnsureRewardTextIntegrity()
        {
            if (rewardTexts == null)
            {
                rewardTexts = new List<TMP_Text>();
            }

            rewardTexts.RemoveAll(text => text == null);

            if (rewardText != null && !rewardTexts.Contains(rewardText))
            {
                rewardTexts.Insert(0, rewardText);
            }

            if (rewardTexts.Count == 0)
            {
                var texts = GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    var text = texts[i];
                    if (text == null || rewardTexts.Contains(text))
                    {
                        continue;
                    }

                    rewardTexts.Add(text);
                }
            }

            if (rewardText == null && rewardTexts.Count > 0)
            {
                rewardText = rewardTexts[0];
            }
        }

        private void EnsureEffectComponentIntegrity()
        {
            if (effectComponents == null)
            {
                effectComponents = new List<EffectComponent>();
            }

            effectComponents.RemoveAll(component => component == null);

            if (effectComponent != null && !effectComponents.Contains(effectComponent))
            {
                effectComponents.Insert(0, effectComponent);
            }

            if (effectComponents.Count == 0)
            {
                var components = GetComponentsInChildren<EffectComponent>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (component == null || effectComponents.Contains(component))
                    {
                        continue;
                    }

                    effectComponents.Add(component);
                }
            }

            if (Data != null && Data.RewardConfigs != null)
            {
                int requiredCount = Data.RewardConfigs.Count;
                if (requiredCount > effectComponents.Count)
                {
                    EffectComponent fallback = effectComponents.Count > 0 ? effectComponents[0] : effectComponent;
                    while (effectComponents.Count < requiredCount)
                    {
                        effectComponents.Add(fallback);
                    }
                }
            }

            if (effectComponent == null && effectComponents.Count > 0)
            {
                effectComponent = effectComponents[0];
            }
        }

        private void InitializeEffectComponents()
        {
            EnsureEffectComponentIntegrity();

            if (effectComponents == null)
            {
                return;
            }

            var initialized = new List<EffectComponent>();
            for (int i = 0; i < effectComponents.Count; i++)
            {
                var component = effectComponents[i];
                if (component == null || initialized.Contains(component))
                {
                    continue;
                }

                component.enabled = true;
                component.Initialize();
                initialized.Add(component);
            }

            if (effectComponent != null && !initialized.Contains(effectComponent))
            {
                effectComponent.enabled = true;
                effectComponent.Initialize();
                initialized.Add(effectComponent);
            }

            var defaultEffect = ResolveEffectComponent(0);
            if (defaultEffect != null)
            {
                Pack.Effector = defaultEffect;
                ActiveFlags |= CapabilityFlags.Effector;
            }
            else
            {
                Pack.Effector = null;
                ActiveFlags &= ~CapabilityFlags.Effector;
            }
        }

        private EffectComponent ResolveEffectComponent(int rewardIndex)
        {
            EnsureEffectComponentIntegrity();

            if (effectComponents == null || effectComponents.Count == 0)
            {
                return effectComponent;
            }

            if (rewardIndex >= 0 && rewardIndex < effectComponents.Count)
            {
                var component = effectComponents[rewardIndex];
                if (component != null)
                {
                    return component;
                }
            }

            if (effectComponents.Count > 0 && effectComponents[0] != null)
            {
                return effectComponents[0];
            }

            return effectComponent;
        }

        private void SetActiveHitEffect(int rewardIndex)
        {
            var effect = rewardIndex >= 0 ? ResolveEffectComponent(rewardIndex) : ResolveEffectComponent(0);
            if (effect != null)
            {
                Pack.Effector = effect;
            }
        }

        private void UpdateRewardTexts()
        {
            EnsureRewardTextIntegrity();

            if (rewardTexts == null || rewardTexts.Count == 0)
            {
                return;
            }

            for (int i = 0; i < rewardTexts.Count; i++)
            {
                var text = rewardTexts[i];
                if (text == null)
                {
                    continue;
                }

                if (Data == null || Data.RewardConfigs == null || i >= Data.RewardConfigs.Count)
                {
                    text.text = string.Empty;
                    continue;
                }

                var rewardConfig = Data.RewardConfigs[i];
                if (rewardConfig == null)
                {
                    text.text = string.Empty;
                    continue;
                }

                // text.text = $"{GetRewardLabel(rewardConfig)} ({Mathf.Max(0, rewardConfig.Cost)})";
                text.text = $"{Mathf.Max(0, rewardConfig.Cost)}";
            }
        }

        private static string GetPortLabel(int index)
        {
            switch (index)
            {
                case 0:
                    return "Left";
                case 1:
                    return "Middle";
                case 2:
                    return "Right";
                default:
                    return $"Port {index + 1}";
            }
        }

        private static string GetRewardLabel(SwitchIncreaseGateRewardConfig rewardConfig)
        {
            switch (rewardConfig.RewardType)
            {
                case SwitchIncreaseGateRewardType.Stat:
                    return rewardConfig.StatModifierData != null && rewardConfig.StatModifierData.Type != StatType.None
                        ? $"Stat {rewardConfig.StatModifierData.Type}"
                        : "Stat";
                case SwitchIncreaseGateRewardType.ArmyTierUpgrade:
                    return $"Tier {Mathf.Max(1, rewardConfig.UpgradeTargetLevel)}";
                case SwitchIncreaseGateRewardType.ArmyCount:
                    int amount = 0;
                    if (rewardConfig.RequestDataList != null)
                    {
                        for (int i = 0; i < rewardConfig.RequestDataList.Count; i++)
                        {
                            amount += Mathf.Max(1, rewardConfig.RequestDataList[i].Amount);
                        }
                    }

                    return $"Count {Mathf.Max(1, amount)}";
                default:
                    return rewardConfig.RewardType.ToString();
            }
        }

        private void StartCollapseAfterReward()
        {
            if (_isCollapsed)
            {
                return;
            }

            _isCollapsed = true;

            if (_collapseRoutine != null)
            {
                StopCoroutine(_collapseRoutine);
                _collapseRoutine = null;
            }

            if (Pack.Hitable != null)
            {
                RegisterEvents(false);
                CollisionSystem.Unregister(Pack.Hitable);
            }

            if (hitComponent != null)
            {
                hitComponent.enabled = false;
            }

            ActiveFlags &= ~CapabilityFlags.Hit;
            _collapseRoutine = StartCoroutine(CoCollapseAndDisable());
        }

        private IEnumerator CoCollapseAndDisable()
        {
            Transform target = _resolvedCollapseTransform != null ? _resolvedCollapseTransform : transform;
            if (target == null)
            {
                yield break;
            }

            Quaternion from = target.localRotation;
            Quaternion to = _originalLocalRotation * Quaternion.Euler(collapseAngleX, 0f, 0f);

            float elapsed = 0f;
            while (elapsed < collapseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, collapseDuration));
                target.localRotation = Quaternion.Slerp(from, to, t);
                yield return null;
            }

            target.localRotation = to;
            target.gameObject.SetActive(false);
            _collapseRoutine = null;
        }
    }
}

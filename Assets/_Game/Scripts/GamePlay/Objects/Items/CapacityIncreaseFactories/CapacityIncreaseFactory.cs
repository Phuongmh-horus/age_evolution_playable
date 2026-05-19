using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using GamePlay.AnimationSystems;
using GamePlay.Characters;
using GamePlay.CombatSystems;
using GamePlay.CollisionSystems;
using GamePlay.ComponentSystems;
using GamePlay.Effects;
using GamePlay.HealthSystems;
using GamePlay.Managers;
using GamePlay.OscillationSystems;
using Pools;
using TMPro;
using UnityEngine;

namespace GamePlay.Items
{
    public class CapacityIncreaseFactory : StatModifierItem<CapacityIncreaseFactoryData>
    {
        private static readonly int FillAmountProp = Shader.PropertyToID("_FillAmount");
        private static readonly int BrightnessProp = Shader.PropertyToID("_Brightness");

        [Serializable]
        public class FormationConfig
        {
            public string Name;
            public int TargetCount;
            public Transform[] Slots;
        }

        [Header("Display Settings")]
        [SerializeField] protected TextMeshPro valueText;
        [SerializeField] protected SpriteRenderer progressSprite;

        [Header("Spawn Settings")]
        [SerializeField] protected FormationConfig[] formations;
        [SerializeField] protected float beltScale = 1.7f;
        [SerializeField] private bool deferPreviewUnitsUntilGameStart = true;
        [SerializeField, Min(1f)] private float previewActivationRangeX = 10f;
        [SerializeField, Min(5f)] private float previewActivationRangeZ = 28f;

        [Header("Armor Visual Settings")]
        [SerializeField] protected GameObject armorParent;
        [SerializeField] protected TextMeshPro armorText;

        private List<CharacterUnit> _spawnedBelts = new List<CharacterUnit>();
        private CharacterListDataSO _caceCharacterListDataSO;
        private int _level;
        private int _lastVfxLevel;
        private float _lastVfxTime;
        private bool _hasArmorBreaked;
        private FormationConfig _currentFormation;
        private readonly HashSet<Transform> _activeSlotSet = new HashSet<Transform>();
        private readonly List<Transform> _slotBuffer = new List<Transform>(16);
        private bool _pendingFormationRefresh;
        private int _pendingLevelRefreshLevel = -1;
        private int _lastFormationRefreshFrame = -1;

        [Header("Arrow Animation Settings")]
        [SerializeField] private MeshRenderer[] arrows;
        [SerializeField] private float waveSpeed = 0.05f;
        [SerializeField] private float flashDuration = 0.2f;
        [SerializeField] private float baseBrightness = 1.0f;
        [SerializeField] private float activeBrightness = 3.0f;

        [Tooltip("Náº¿u cÃ³ ngÆ°á»i Ä‘Ã¡nh tiáº¿p trong khoáº£ng thá»i gian nÃ y, hiá»‡u á»©ng sáº½ láº·p láº¡i")]
        [SerializeField] private float sustainDuration = 0.5f;

        [Header("Sound Effects")]
        [SerializeField] private AudioClipName hitByWheelSound = AudioClipName.None;
        [SerializeField] private AudioClipName levelUpSfx = AudioClipName.None;

        [Header("Level Up VFX")]
        [SerializeField] private ParticleSystem levelUpVfxPrefab;
        [SerializeField] private Transform levelUpVfxRoot;
        [SerializeField] private float levelUpVfxCooldown = 0.1f;
        [SerializeField] private bool useSlotVfxChildren = false;
        [SerializeField] private bool disableSlotVfxAfterPlay = true;

        [Header("Hit Fly Text")]
        [SerializeField] private HitTextFlyEffect hitTextFlyEffect;
        [SerializeField] private HitComponent hitComponent;
        [SerializeField] private HealthComponent healthComponent;

        [Header("Hit Scale Pulse")]
        [SerializeField] private float scaleUp = 1.08f;
        [SerializeField] private float scaleUpDuration = 0.08f;
        [SerializeField] private float scaleDownDuration = 0.15f;

        private float _lastHitTime = -999f;
        private bool _isWaveRunning;

        private MaterialPropertyBlock[] _arrowPropertyBlocks;
        private MaterialPropertyBlock _progressMpb;
        private int _lastDisplayedLevel = int.MinValue;
        private float _lastDisplayedProgress = float.NaN;

        // DOTween thay LitMotion
        private Tween[] _arrowTweens;

        private float[] _arrowBrightness;

        private Vector3 _originalScale;
        private Coroutine _scalePulseRoutine;
        private int _lastScalePulseFrame = -1;
        private static readonly Dictionary<int, TimedAutoDisable> s_levelUpTimedAutoDisableCache = new Dictionary<int, TimedAutoDisable>(64);

        [Header("Brick Fall Settings")]
        [SerializeField] private BrickFallSettings brickFallSettings;

        [Header("Oscillation")]
        [SerializeField] private bool onlyCenterOscillates = true;
        [SerializeField] private float centerXThreshold = 1.0f;

        [Header("Health Settings")]
        [SerializeField] private bool forceImmortal = true;
        [Header("Capacity Coin Reward")]
        [SerializeField] private bool enableLegacyFactoryCoinReward = false;
        [SerializeField, Min(1)] private int baseCoinPerProgressTick = 3;
        [SerializeField] private bool useCurrentCapacityAsReward = true;
        [SerializeField] private bool rewardOnNonWheelHit = true;
        [SerializeField] private bool rewardOnWheelHit = true;
        private int _lastCoinRewardFrame = -1;

        protected void Awake()
        {
            // [FIX] Ensure EntityType is CapacityFactory at runtime
            if (_entityType == GamePlay.Entities.EntityType.None)
            {
                _entityType = GamePlay.Entities.EntityType.CapacityFactory;
                Debug.LogWarning($"[CapacityIncreaseFactory] {gameObject.name} had EntityType.None! Auto-set to CapacityFactory.");
            }

            _progressMpb = new MaterialPropertyBlock();
            ApplyHealthSettings();

            if (arrows == null || arrows.Length == 0)
            {
                Debug.LogWarning($"[CapacityIncreaseFactory] Missing arrows on {name}. Assign in Inspector.");
                arrows = new MeshRenderer[0];
            }
            else
            {
                Array.Sort(arrows, (a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
            }

            _arrowTweens = new Tween[arrows.Length];
            _arrowBrightness = new float[arrows.Length];
            _arrowPropertyBlocks = new MaterialPropertyBlock[arrows.Length];

            for (int i = 0; i < arrows.Length; i++)
            {
                var arrowRenderer = arrows[i];
                if (arrowRenderer != null)
                {
                    var propertyBlock = new MaterialPropertyBlock();
                    arrowRenderer.GetPropertyBlock(propertyBlock);
                    _arrowPropertyBlocks[i] = propertyBlock;
                }

                SetArrowBrightness(i, baseBrightness, force: true);
            }
        }

        private void OnEnable()
        {
            GameEventBus.UpgradeCapacity += GameEventBus_UpgradeCapacity;
        }

        private void OnDisable()
        {
            foreach (var unit in _spawnedBelts)
            {
                if (unit == null) continue;
                unit.Transform.parent = null;
                unit.Transform.localScale = Vector3.one;
                unit.Despawn();
            }

            _spawnedBelts.Clear();

            GameEventBus.UpgradeCapacity -= GameEventBus_UpgradeCapacity;

            KillAllArrowTweens();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            // Auto-set EntityType for safety
            _entityType = GamePlay.Entities.EntityType.CapacityFactory;

            Data.Type = StatType.Character;

            if (Data.Level < 1) Data.Level = 1;

            if (formations != null)
            {
                foreach (var f in formations)
                {
                    if (f != null) f.Name = $"Formation for {f.TargetCount} Units";
                }
            }
            EnsureHitTextEffect(false);

            ApplyHealthSettings();
        }
        #endif

        public override void Initialize()
        {
            _level = Mathf.Max(1, Data != null ? Data.Level : 1);
            _lastVfxLevel = _level;
            _lastVfxTime = -999f;
            _pendingFormationRefresh = true;
            _pendingLevelRefreshLevel = -1;
            _lastFormationRefreshFrame = -1;
            _lastDisplayedLevel = int.MinValue;
            _lastDisplayedProgress = float.NaN;

            if (_spawnedBelts == null) _spawnedBelts = new List<CharacterUnit>();
            else _spawnedBelts.Clear();

            // Láº¥y sá»‘ lÆ°á»£ng capacity á»Ÿ player
            // [FIX PLAYABLE] DÃ¹ng GamePlayVariable.EvolutionVariable thay vÃ¬ DataManager
            if (GameplayManager.Instance != null &&
                GameplayManager.Instance.gamePlayVariable != null &&
                GameplayManager.Instance.gamePlayVariable.EvolutionVariable != null)
            {
                Data.Value = GameplayManager.Instance.gamePlayVariable.EvolutionVariable.Capacity;
            }
            else
            {
                Data.Value = 5; // Default capacity cho playable
            }

            _entityType = GamePlay.Entities.EntityType.CapacityFactory;

            // [FIX] Find HitComponent FIRST - this is the visual collider we want to use!
            var hitComp = hitComponent;
            if (hitComp != null)
            {

                // [CRITICAL] Only fix if Z is exactly 0 (invalid for AABB check)
                if (hitComp.colliderSize.z < 0.1f)
                {
                    Vector3 fixedSize = hitComp.colliderSize;
                    fixedSize.z = Mathf.Max(fixedSize.x, fixedSize.y);
                    Debug.LogWarning($"[Factory] HitComponent.colliderSize.z was ~0! Fixing to {fixedSize}");
                    hitComp.colliderSize = fixedSize;
                }

                hitComp.Initialize();
            }
            else
            {
                Debug.LogWarning($"[Factory] No HitComponent found! Will use ItemUnit as fallback.");
            }

            // Call base but we will handle CollisionSystem registration ourselves
            base.Initialize();

            // Keep Pack.Hitable aligned with registered collider target to avoid stale collision entries.
            if (hitComp != null)
            {
                bool alreadyCorrect = Pack.Hitable != null && ReferenceEquals(Pack.Hitable, hitComp);
                if (!alreadyCorrect)
                {
                    if (Pack.Hitable != null)
                    {
                        RegisterEvents(false);
                        CollisionSystem.Unregister(Pack.Hitable);
                    }

                    Pack.Hitable = hitComp;
                    ActiveFlags |= CapabilityFlags.Hit;
                    CollisionSystem.Register(hitComp, hitComp.transform);
                    RegisterEvents(true);
                }
            }

            UpdateArmor(true);
            if (!deferPreviewUnitsUntilGameStart || GameplayManager.IsGameStarted)
            {
                UpdateFormationAndUnits();
                _pendingFormationRefresh = false;
            }

            _originalScale = transform.localScale;

            EnsureHitTextEffect(false);
            if (hitTextFlyEffect != null)
                hitTextFlyEffect.enabled = true;

            DisableOscillationIfSide();
            ApplyHealthSettings();

            // [FIX] Fix Visual Distortion caused by Parent Scaling (e.g. Factory is stretched)
            // This applies a "Counter-Scale" to ensure UI elements (Bar, Text) remain 1:1 aspect ratio.
            FixVisualDistortion();
        }

        private void FixVisualDistortion()
        {
            // 1. Fix Progress Sprite (Green Bar) - REVERTED due to Sprite Asset compatibility issues
            /*
            if (progressSprite != null)
            {
               // ... logic removed to restore visibility ...
            }
            */
            
            // 2. Fix Text (Label)
            if (valueText != null)
            {
                 Vector3 lossy = valueText.transform.lossyScale;
                 // If Text is stretched X > Y
                 float ratio = lossy.x / lossy.y;
                 if (Mathf.Abs(ratio - 1f) > 0.01f)
                 {
                     Transform t = valueText.transform;
                     // Just shrink X to restore aspect ratio. 
                     // We don't have 'sliced' size for text, but it's vector text so safe.
                     // New Scale X = Old Scale X / Ratio
                     t.localScale = new Vector3(t.localScale.x / ratio, t.localScale.y, t.localScale.z);
                 }
            }
        }

        private void ApplyHealthSettings()
        {
            if (healthComponent != null)
            {
                healthComponent.SetImmortal(forceImmortal);
            }
            else
            {
                Debug.LogWarning($"[CapacityIncreaseFactory] Missing HealthComponent on {name}. Assign in Inspector.");
            }
        }

        private void DisableOscillationIfSide()
        {
            if (!onlyCenterOscillates) return;

            // [FIX] Check immediately AND after delay for both Unity Editor and Luna build
            CheckAndDisableOscillation();
            StartCoroutine(CoDisableOscillationDelayed());
        }

        private void CheckAndDisableOscillation()
        {
            if (!onlyCenterOscillates) return;
            if ((ActiveFlags & CapabilityFlags.Oscillate) == 0 || Pack.Oscillator == null) return;

            // [FIX] Use World X to detect side placement reliably.
            float worldX = Transform.position.x;

            // Check if NOT center - use centerXThreshold instead of hardcoded value
            bool isCenter = Mathf.Abs(worldX) <= centerXThreshold;

            if (!isCenter)
            {
                OscillationSystem.Unregister(Pack.Oscillator);
            }
        }

        private IEnumerator CoDisableOscillationDelayed()
        {
            if (!onlyCenterOscillates) yield break;

            // [FIX] Use yield return null instead of WaitForEndOfFrame (Luna doesn't support WaitForEndOfFrame)
            yield return null;
            yield return null; // Wait 2 frames to ensure position is set

            CheckAndDisableOscillation();
        }

        protected override void HandleWheelCollision()
        {
            if (enableLegacyFactoryCoinReward && rewardOnWheelHit)
            {
                TryGrantCapacityCoinReward();
            }

            EnsureUnitsSpawned();
            PlayScalePulse();
            SoundManager.Instance.PlayOneShot(hitByWheelSound);
            for (int i = 0; i < _spawnedBelts.Count; i++)
            {
                if (_spawnedBelts[i] != null)
                {
                    _spawnedBelts[i].Transform.localRotation = Quaternion.identity;
                    _spawnedBelts[i].Transform.parent = null;
                }
            }

            bool hasGate = ConveyorManager.Instance.HasGateAhead(Transform.position);

            // [FIX] Ensure base logic (Despawn, etc) runs? 
            // The Playable logic suggests we should despawn the factory visual.
            // But we must ensure units are passed FIRST.

            if (hasGate)
            {
                ConveyorManager.Instance.AddCharactersToBelt(_spawnedBelts, Transform.position);
            }
            else
            {
                 Debug.LogWarning($"[FactoryDebug] No Gate detected for {gameObject.name} logic!");
                for (int i = 0; i < _spawnedBelts.Count; i++)
                {
                    if (_spawnedBelts[i] != null)
                    {
                        _spawnedBelts[i].Transform.localScale = Vector3.one;
                        _spawnedBelts[i].Despawn();
                    }
                }

                GameplayManager.Instance.ChangeStatModifierData(Data);
            }

            _spawnedBelts.Clear();

            DespawnInterval();
        }

        private void Update()
        {
            if (!deferPreviewUnitsUntilGameStart) return;
            if (!GameplayManager.IsGameStarted) return;
            if (Data == null || Data.Value <= 0) return;

            if (_pendingFormationRefresh)
            {
                ProcessPendingFormationRefresh();
                return;
            }

            if (_spawnedBelts != null && _spawnedBelts.Count > 0) return;

            ForceFormationRefresh();
        }

        private void EnsureUnitsSpawned()
        {
            if (Data == null || Data.Value <= 0) return;
            if (_spawnedBelts != null && _spawnedBelts.Count > 0 && !_pendingFormationRefresh) return;

            if (_pendingFormationRefresh)
            {
                ProcessPendingFormationRefresh();
                return;
            }

            ForceFormationRefresh();
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            base.HandleNonWheelCollision(source);

            if (enableLegacyFactoryCoinReward && rewardOnNonWheelHit)
            {
                TryGrantCapacityCoinReward();
            }

            PlayScalePulse();
        }

        private void TryGrantCapacityCoinReward()
        {
            if (_lastCoinRewardFrame == Time.frameCount) return;
            _lastCoinRewardFrame = Time.frameCount;

            var gm = GameplayManager.Instance;
            if (gm == null) return;

            int reward = Mathf.Max(1, baseCoinPerProgressTick);
            if (useCurrentCapacityAsReward &&
                gm.gamePlayVariable != null &&
                gm.gamePlayVariable.EvolutionVariable != null)
            {
                int currentCapacity = Mathf.Max(1, gm.gamePlayVariable.EvolutionVariable.Capacity);
                reward = Mathf.Max(reward, currentCapacity);
            }

            gm.AddCapacityCoinToPool(reward);
        }

        private void EnsureHitTextEffect(bool allowAddRuntime)
        {
            if (hitTextFlyEffect != null) return;
            hitTextFlyEffect = GetComponentInChildren<HitTextFlyEffect>(true);
            if (hitTextFlyEffect == null && allowAddRuntime)
                hitTextFlyEffect = gameObject.AddComponent<HitTextFlyEffect>();
        }

        private void PlayScalePulse()
        {
            if (!isActiveAndEnabled) return;
            if (_lastScalePulseFrame == Time.frameCount) return;
            _lastScalePulseFrame = Time.frameCount;
            if (_scalePulseRoutine != null) StopCoroutine(_scalePulseRoutine);
            _scalePulseRoutine = StartCoroutine(CoScalePulse());
        }

        private IEnumerator CoScalePulse()
        {
            if (_originalScale == Vector3.zero) _originalScale = transform.localScale;

            Vector3 from = _originalScale;
            Vector3 to = _originalScale * scaleUp;

            float t = 0f;
            while (t < scaleUpDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, scaleUpDuration));
                transform.localScale = Vector3.Lerp(from, to, k);
                yield return null;
            }

            t = 0f;
            while (t < scaleDownDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, scaleDownDuration));
                transform.localScale = Vector3.Lerp(to, _originalScale, k);
                yield return null;
            }

            transform.localScale = _originalScale;
            _scalePulseRoutine = null;
        }

        protected override void AdjustStatModifierValue(int value = 0)
        {
            if (value > 0)
                HandleInteract();

            int previousValue = Data.Value;
            Data.AdjustValue(value);

            bool valueChanged = Data.Value != previousValue;
            bool levelChanged = Data.Level > _level;

            if (valueChanged || levelChanged)
            {
                RequestFormationRefresh(levelChanged ? Data.Level : -1);
            }

            if (levelChanged)
            {
                if (_level > 0)
                    PlayLevelUpSfx();
                // Play VFX on currently active slots only.
                TryPlayLevelUpVfx();
                _level = Data.Level;
            }

            UpdateArmor(false);

            UpdateText();
            UpdateImage();
        }

        private void UpdateArmor(bool isInitialize)
        {
            if (Data.Armor > 0)
            {
                if (isInitialize) _hasArmorBreaked = false;

                if (armorParent != null && !armorParent.activeInHierarchy) ShowArmor();
                if (armorText != null) armorText.text = Data.Armor.ToString();
            }
            else if (armorParent != null && armorParent.activeInHierarchy)
            {
                if (isInitialize)
                {
                    _hasArmorBreaked = true;
                    HideArmor();
                }
                else
                {
                    if (!_hasArmorBreaked)
                    {
                        _hasArmorBreaked = true;

                        if (armorText != null) armorText.text = string.Empty;

                        if (Pack.Animator != null)
                        {
                            Pack.Animator.PlayAnimation(AnimationType.Break, waitForAction: 0.2f, onComplete: DissolveArmor);
                        }
                        else
                        {
                            DissolveArmor();
                        }
                    }
                }
            }
        }

        private void PlayLevelUpSfx()
        {
            var sfx = levelUpSfx != AudioClipName.None ? levelUpSfx : AudioClipName.SFX_Ingame_Capacity_LevelUp;
            if (sfx == AudioClipName.None) return;

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayOneShot(sfx);
                return;
            }

            AudioClip clipToPlay = null;
            float volume = 1f;

            if (clipToPlay == null)
            {
                clipToPlay = Resources.Load<AudioClip>($"Sound/{sfx}");
            }

            if (clipToPlay != null)
            {
                var cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetCamera() : null;
                var pos = cam != null ? cam.transform.position : transform.position;
                AudioSource.PlayClipAtPoint(clipToPlay, pos, volume);
                return;
            }

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlayOneShot(sfx);
        }

        /// <summary>
        /// Quáº£n lÃ½ viá»‡c sinh lÃ­nh vÃ  xáº¿p Ä‘á»™i hÃ¬nh
        /// </summary>
        private void UpdateFormationAndUnits()
        {
            int targetCount = GetResolvedTargetCount();

            // Step 0: detach current units from old slots before remapping.
            for (int i = 0; i < _spawnedBelts.Count; i++)
            {
                var unit = _spawnedBelts[i];
                if (unit == null) continue;
                unit.Transform.SetParent(Transform);
            }

            // Step 1: resolve formation without LINQ allocations.
            FormationConfig currentFormation = null;
            FormationConfig fallbackFormation = null;
            int fallbackTargetCount = int.MinValue;
            if (formations != null)
            {
                for (int i = 0; i < formations.Length; i++)
                {
                    var cfg = formations[i];
                    if (cfg == null) continue;

                    if (cfg.TargetCount == targetCount)
                        currentFormation = cfg;

                    if (cfg.TargetCount > fallbackTargetCount)
                    {
                        fallbackTargetCount = cfg.TargetCount;
                        fallbackFormation = cfg;
                    }
                }
            }

            if (currentFormation == null && fallbackFormation != null)
            {
                currentFormation = fallbackFormation;
                if (targetCount > currentFormation.TargetCount)
                    targetCount = currentFormation.TargetCount;
            }

            if (currentFormation == null)
            {
                Debug.LogError($"[Factory] No formation found for Count {targetCount}!");
                return;
            }

            _currentFormation = currentFormation;

            // Step 2: toggle slot visibility with a reused set.
            _activeSlotSet.Clear();
            if (currentFormation.Slots != null)
            {
                for (int i = 0; i < currentFormation.Slots.Length; i++)
                {
                    var slot = currentFormation.Slots[i];
                    if (slot != null) _activeSlotSet.Add(slot);
                }
            }

            if (formations != null)
            {
                for (int i = 0; i < formations.Length; i++)
                {
                    var config = formations[i];
                    if (config == null || config.Slots == null) continue;

                    for (int j = 0; j < config.Slots.Length; j++)
                    {
                        var slot = config.Slots[j];
                        if (slot == null) continue;

                        bool shouldBeActive = _activeSlotSet.Contains(slot);
                        if (slot.gameObject.activeSelf != shouldBeActive)
                            slot.gameObject.SetActive(shouldBeActive);
                    }
                }
            }

            // Step 3: resize belt unit count.
            while (_spawnedBelts.Count < targetCount)
            {
                var newBelt = CreateBeltUnit(Transform, Data.Level);
                if (newBelt != null) _spawnedBelts.Add(newBelt);
                else break;
            }

            while (_spawnedBelts.Count > targetCount)
            {
                int lastIndex = _spawnedBelts.Count - 1;
                var unit = _spawnedBelts[lastIndex];
                if (unit != null)
                {
                    unit.Transform.parent = null;
                    unit.Transform.localScale = Vector3.one;
                    unit.Despawn();
                }
                _spawnedBelts.RemoveAt(lastIndex);
            }

            // Step 4: assign units to current formation slots.
            for (int i = 0; i < _spawnedBelts.Count; i++)
            {
                if (currentFormation.Slots == null || i >= currentFormation.Slots.Length)
                {
                    Debug.LogWarning($"[Factory] Unit {i} has no slot index in formation {currentFormation.Name}!");
                    continue;
                }

                Transform targetSlot = currentFormation.Slots[i];
                if (targetSlot == null)
                {
                    Debug.LogError($"[Factory] Slot {i} in formation {currentFormation.Name} is NULL!");
                    continue;
                }

                CharacterUnit unit = _spawnedBelts[i];
                if (unit == null) continue;

                unit.Transform.SetParent(targetSlot);
                unit.Transform.localPosition = Vector3.zero;
                unit.Transform.localRotation = Quaternion.Euler(0, 180, 0);
                unit.Transform.localScale = Vector3.one * beltScale;
                unit.transform.localPosition = Vector3.zero;
            }
        }

        private CharacterUnit CreateBeltUnit(Transform parent, int level)
        {
            var characterData = GetCharacterData(level);
            if (characterData == null) return null;

            // FIX Lá»–I:
            // 1. HÃ m Spawn khÃ´ng cÃ³ tham sá»‘ 'localRotation', ta chá»‰ truyá»n 'parent'.
            // 2. Set localRotation thá»§ cÃ´ng sau khi spawn.
            var beltInstance = characterData.CharacterPrefab.Spawn(parent: parent);

            if (beltInstance != null)
            {
                // Xoay 180 Ä‘á»™ trá»¥c Y Ä‘á»ƒ lÃ­nh quay máº·t ra ngoÃ i (theo logic cÅ© cá»§a báº¡n)
                beltInstance.Transform.localRotation = Quaternion.Euler(0, 180, 0);

                beltInstance.Transform.localScale = Vector3.one * beltScale;
                beltInstance.InitializePreview(level);
            }

            return beltInstance;
        }

        /// <summary>
        /// Thay tháº¿ skin lÃ­nh khi lÃªn cáº¥p
        /// </summary>
        private void RefreshUnitLevels(int newLevel)
        {
            _slotBuffer.Clear();
            for (int i = 0; i < _spawnedBelts.Count; i++)
            {
                var unit = _spawnedBelts[i];
                if (unit == null) continue;

                _slotBuffer.Add(unit.Transform.parent);
                unit.Transform.parent = null;
                unit.Transform.localScale = Vector3.one;
                unit.Despawn();
            }

            _spawnedBelts.Clear();

            for (int i = 0; i < _slotBuffer.Count; i++)
            {
                var slot = _slotBuffer[i];
                var newBelt = CreateBeltUnit(slot, newLevel);
                if (newBelt != null) _spawnedBelts.Add(newBelt);
            }
            _slotBuffer.Clear();
        }

        private void RequestFormationRefresh(int levelToRefresh)
        {
            _pendingFormationRefresh = true;
            if (levelToRefresh > _pendingLevelRefreshLevel)
                _pendingLevelRefreshLevel = levelToRefresh;

            if (_lastFormationRefreshFrame == Time.frameCount)
                return;

            bool canRefreshNow = !deferPreviewUnitsUntilGameStart ||
                                 !GameplayManager.IsGameStarted ||
                                 (_spawnedBelts != null && _spawnedBelts.Count > 0) ||
                                 IsWithinPreviewRange();
            if (!canRefreshNow)
                return;

            ProcessPendingFormationRefresh();
        }

        private void ForceFormationRefresh()
        {
            UpdateFormationAndUnits();
            _pendingFormationRefresh = false;
            _pendingLevelRefreshLevel = -1;
            _lastFormationRefreshFrame = Time.frameCount;
        }

        private void ProcessPendingFormationRefresh()
        {
            if (!_pendingFormationRefresh)
                return;

            int pendingLevel = _pendingLevelRefreshLevel;
            int resolvedTargetCount = GetResolvedTargetCount();
            _pendingFormationRefresh = false;
            _pendingLevelRefreshLevel = -1;
            _lastFormationRefreshFrame = Time.frameCount;

            bool canRefreshLevelsInPlace = pendingLevel > 0 &&
                                           _spawnedBelts != null &&
                                           _spawnedBelts.Count > 0 &&
                                           _currentFormation != null &&
                                           _currentFormation.Slots != null &&
                                           _spawnedBelts.Count == resolvedTargetCount &&
                                           _currentFormation.Slots.Length >= resolvedTargetCount;

            if (pendingLevel > 0 && _spawnedBelts != null && _spawnedBelts.Count > 0)
                RefreshUnitLevels(pendingLevel);

            if (!canRefreshLevelsInPlace)
                UpdateFormationAndUnits();
        }

        private void PlayLevelUpVfx()
        {
            if (_currentFormation == null || _currentFormation.Slots == null) return;

            for (int i = 0; i < _currentFormation.Slots.Length; i++)
            {
                var slot = _currentFormation.Slots[i];
                if (slot == null) continue;
                if (!slot.gameObject.activeInHierarchy) continue;

                if (useSlotVfxChildren)
                {
                    var vfx = slot.GetComponentInChildren<ParticleSystem>(true);
                    if (vfx == null) continue;

                    vfx.gameObject.SetActive(true);
                    vfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    vfx.Play(true);

                    if (disableSlotVfxAfterPlay)
                    {
                        float lifetime = GetParticleLifetime(vfx);
                        if (lifetime > 0f)
                        {
                            StartCoroutine(CoDisableVfx(vfx, lifetime));
                        }
                    }
                }
                else
                {
                    if (levelUpVfxPrefab == null) continue;

                    Transform parent = levelUpVfxRoot != null ? levelUpVfxRoot : null;
                    bool canPool = PoolManager.Instance != null;
                    var vfx = canPool ? PoolManager.Instance.Get(levelUpVfxPrefab) : Instantiate(levelUpVfxPrefab, slot.position, slot.rotation, parent);
                    if (vfx == null) continue;

                    Transform vfxTransform = vfx.transform;
                    vfxTransform.SetParent(parent, false);
                    vfxTransform.position = slot.position;
                    vfxTransform.rotation = slot.rotation;
                    vfxTransform.localScale = Vector3.one;

                    var vfxObject = vfx.gameObject;
                    vfxObject.SetActive(true);

                    var main = vfx.main;
                    if (main.loop) main.loop = false;
                    vfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    vfx.Play(true);

                    float lifetime = GetParticleLifetime(vfx);
                    if (lifetime > 0f)
                    {
                        if (canPool)
                        {
                            var autoDisable = GetOrAddLevelUpTimedAutoDisable(vfx);
                            autoDisable?.Play(lifetime);
                        }
                        else
                        {
                            Destroy(vfxObject, lifetime);
                        }
                    }
                }
            }
        }

        private void TryPlayLevelUpVfx()
        {
            // Prevent VFX spam when characters keep attacking.
            if (Data.Level <= _lastVfxLevel) return;
            if (Time.time - _lastVfxTime < levelUpVfxCooldown) return;

            _lastVfxLevel = Data.Level;
            _lastVfxTime = Time.time;
            PlayLevelUpVfx();
        }

        private IEnumerator CoDisableVfx(ParticleSystem vfx, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (vfx != null)
                vfx.gameObject.SetActive(false);
        }

        private static float GetParticleLifetime(ParticleSystem ps)
        {
            if (ps == null) return 0f;

            var main = ps.main;
            float duration = main.duration;
            float startLifetime = 0f;

            switch (main.startLifetime.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    startLifetime = main.startLifetime.constant;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    startLifetime = main.startLifetime.constantMax;
                    break;
                case ParticleSystemCurveMode.Curve:
                    startLifetime = main.startLifetime.curveMultiplier;
                    break;
                case ParticleSystemCurveMode.TwoCurves:
                    startLifetime = main.startLifetime.curveMultiplier;
                    break;
            }

            return duration + startLifetime;
        }

        private void UpdateText()
        {
            if (valueText == null) return;
            if (_lastDisplayedLevel == Data.Level) return;

            _lastDisplayedLevel = Data.Level;
            // Playable: dÃ¹ng text trá»±c tiáº¿p thay vÃ¬ I2 Localization
            valueText.text = "Lv " + _lastDisplayedLevel;
        }

        private void UpdateImage()
        {
            if (progressSprite == null) return;

            float value = Data.GetUpgradeProgress();
            float max = 0.792f;
            float min = 0.532f;

            value = value * (max - min) + min;

            if (!float.IsNaN(_lastDisplayedProgress) && Mathf.Abs(_lastDisplayedProgress - value) <= 0.0001f)
                return;

            _lastDisplayedProgress = value;

            if (_progressMpb == null) _progressMpb = new MaterialPropertyBlock();
            progressSprite.GetPropertyBlock(_progressMpb);
            _progressMpb.SetFloat(FillAmountProp, value);
            progressSprite.SetPropertyBlock(_progressMpb);
        }

        private void GameEventBus_UpgradeCapacity(int capacity)
        {
            if (capacity > Data.Value)
            {
                Data.Value = capacity;
                RequestFormationRefresh(-1);
            }
        }

        private static TimedAutoDisable GetOrAddLevelUpTimedAutoDisable(ParticleSystem vfx)
        {
            if (vfx == null) return null;

            int key = vfx.gameObject.GetInstanceID();
            if (s_levelUpTimedAutoDisableCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            if (!vfx.TryGetComponent(out cached))
                cached = vfx.gameObject.AddComponent<TimedAutoDisable>();

            s_levelUpTimedAutoDisableCache[key] = cached;
            return cached;
        }

        private bool IsWithinPreviewRange()
        {
            if (GameplayManager.Instance == null || GameplayManager.Instance.Turnable == null)
                return false;

            Transform wheel = GameplayManager.Instance.Turnable.Transform;
            if (wheel == null) return false;

            Vector3 delta = wheel.position - Transform.position;
            return Mathf.Abs(delta.x) <= previewActivationRangeX &&
                   Mathf.Abs(delta.z) <= previewActivationRangeZ;
        }

        #region SPAWN BELTS HELPERS

        private void InitializeCharacterData()
        {
            EraDataSO eraData = null;

            if (ConfigHolder.Instance != null)
            {
                eraData = ConfigHolder.Instance.GetCurrentEraConfig();
            }

            if (!eraData && GameplayManager.Instance != null)
            {
                eraData = GameplayManager.Instance.PlayableEra;
            }

            if (eraData != null)
            {
                _caceCharacterListDataSO = eraData.CharacterList;
            }
        }

        private CharacterListDataSO.CharacterEntry GetCharacterData(int level)
        {
            if (_caceCharacterListDataSO == null) InitializeCharacterData();
            if (_caceCharacterListDataSO == null) return null;
            return _caceCharacterListDataSO.GetCharacterByLevel(level);
        }

        private int GetMaxConfiguredCapacity()
        {
            if (formations == null || formations.Length == 0) return 0;
            int max = 0;
            for (int i = 0; i < formations.Length; i++)
            {
                var cfg = formations[i];
                if (cfg == null) continue;
                if (cfg.TargetCount > max) max = cfg.TargetCount;
            }

            return max;
        }

        private int GetResolvedTargetCount()
        {
            int targetCount = Data != null ? Data.Value : 0;
            if (targetCount < 0) targetCount = 0;

            int maxCapacity = GetMaxConfiguredCapacity();
            if (maxCapacity > 0 && targetCount > maxCapacity)
            {
                targetCount = maxCapacity;
            }

            return targetCount;
        }

        #endregion

        #region Arrow Effect (DOTween)

        private void HandleInteract()
        {
            _lastHitTime = Time.time;

            if (!_isWaveRunning)
            {
                StartCoroutine(RunWaveSequence());
            }
        }

        private IEnumerator RunWaveSequence()
        {
            _isWaveRunning = true;

            do
            {
                for (int i = 0; i < arrows.Length; i++)
                {
                    FlashArrow(i);
                    yield return new WaitForSeconds(waveSpeed);
                }

                yield return new WaitForSeconds(flashDuration);
            }
            while (Time.time < _lastHitTime + sustainDuration);

            _isWaveRunning = false;
        }

        private void FlashArrow(int index)
        {
            if (index < 0 || index >= arrows.Length) return;

            var arrowRenderer = arrows[index];
            if (arrowRenderer == null) return;

            if (_arrowTweens[index] == null || !_arrowTweens[index].IsActive())
                _arrowTweens[index] = CreateArrowTween(index);

            SetArrowBrightness(index, baseBrightness, force: true);
            _arrowTweens[index].Restart();
        }

        private Tween CreateArrowTween(int index)
        {
            return DOTween
                .To(() => _arrowBrightness[index],
                    v => SetArrowBrightness(index, v),
                    activeBrightness,
                    flashDuration)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo)
                .SetAutoKill(false)
                .Pause();
        }

        private void SetArrowBrightness(int index, float value, bool force = false)
        {
            if (_arrowBrightness == null || index < 0 || index >= _arrowBrightness.Length) return;

            if (!force && Mathf.Abs(_arrowBrightness[index] - value) <= 0.0001f)
                return;

            _arrowBrightness[index] = value;

            if (arrows == null || index >= arrows.Length) return;

            var renderer = arrows[index];
            if (renderer == null) return;

            if (_arrowPropertyBlocks == null || index >= _arrowPropertyBlocks.Length)
                return;

            var propertyBlock = _arrowPropertyBlocks[index];
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                _arrowPropertyBlocks[index] = propertyBlock;
            }

            propertyBlock.SetFloat(BrightnessProp, value);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private void KillAllArrowTweens()
        {
            if (_arrowTweens == null) return;

            for (int i = 0; i < _arrowTweens.Length; i++)
            {
                if (_arrowTweens[i] != null && _arrowTweens[i].IsActive())
                {
                    _arrowTweens[i].Kill();
                }

                _arrowTweens[i] = null;
            }
        }

        private void OnDestroy()
        {
            KillAllArrowTweens();
        }

        [ContextMenu("Test Start")]
        public void TestStart() => HandleInteract();

        #endregion

        #region Armor

        private void ShowArmor()
        {
            if (armorParent != null) armorParent.SetActive(true);
        }

        private void HideArmor()
        {
            if (armorParent != null) armorParent.SetActive(false);
        }

        private void DissolveArmor()
        {
            if (Pack.Effector != null)
            {
                Pack.Effector.PlayEffect(EffectType.Break, onComplete: HideArmor);
            }
            else
            {
                HideArmor();
            }
        }

        #endregion
    }
}


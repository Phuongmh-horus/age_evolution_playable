using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.Characters;
using GamePlay.CollisionSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Crushers;
using GamePlay.Entities;
using GamePlay.Inputs;
using GamePlay.Weapons;
using Pools;
using UnityEngine;
using WeaponCraft;

namespace PlayerArmy
{
    public enum PlayerArmyState : byte { Idle, IntroRun, Active, KnockBack }
    public enum PlayerArmyAttackMode : byte { Melee, ForwardRanged, ThrownProjectile }

    [DisallowMultipleComponent]
    public class PlayerArmySystem : MonoBehaviour, IAttacker
    {
        [Header("Movement")]
        [SerializeField] private Transform bodyRoot;
        [SerializeField] private float fallbackForwardSpeed = 6f;
        [SerializeField] private float fallbackSpeedChangeRate = 2f;
        [SerializeField, Min(0f)] private float inputSensitivity = 0.015f;
        [SerializeField, Min(1f)] private float strafeFollowMultiplier = 2f;
        [SerializeField] private float xLimit = 4f;
        [SerializeField] private float collisionCheckRangeX = 7f;
        [SerializeField] private float collisionCheckRangeZ = 25f;
        [SerializeField] private Vector2 collisionSize = new Vector2(3f, 3f);

        [Header("Spawn")]
        [SerializeField] private CharacterListDataSO characterList;
        [SerializeField, Min(1)] private int fallbackCharacterLevel = 1;
        [SerializeField, Min(1)] private int maxActiveSpawnedUnits = 10;
        [SerializeField, Min(0f)] private float unitSpacing = 1.2f;
        [SerializeField, Tooltip("Dùng trực tiếp các character đã đặt sẵn trên scene để giảm thời gian spawn lúc boot.")] private bool useSceneUnitsOnly = true;

        [Header("Attack")]
        [SerializeField] private PlayerArmyAttackMode attackMode = PlayerArmyAttackMode.ThrownProjectile;
        [SerializeField, Min(0.1f)] private Vector2 meleeAttackSize = new Vector2(1.4f, 2.2f);
        [SerializeField, Min(0.1f)] private Vector2 rangedAttackSize = new Vector2(1.8f, 6f);
        [SerializeField, Min(0f)] private float attackOriginOffset = 0.9f;
        [SerializeField, Min(0.05f)] private float attackInterval = 0.75f;
        [SerializeField, Min(1)] private int attackDamage = 1;
        [SerializeField, Min(0.1f)] private float projectileDistance = 6f;
        [SerializeField, Min(0.05f)] private float projectileDuration = 0.55f;
        [SerializeField] private float projectileRotationSpeed = 540f;
        [SerializeField, Min(0f)] private float unitscalevalue = 1.35f;
        [SerializeField] private WeaponUnit weaponProjectilePrefab;

        [Header("Refs")]
        [SerializeField] private InputManager inputManager;
        [SerializeField] private PlayerArmyEffectSystem effectSystem;

        [Header("Runtime Units")]
        [SerializeField] private List<CharacterUnit> characterUnits = new List<CharacterUnit>();

        public event Action<IHitable> OnAttackComplete;

        private PlayerArmyState currentState = PlayerArmyState.Active;
        private float _currentForwardSpeed;
        private float _targetX;

        private readonly HashSet<int> _currentEnemyContactIds = new HashSet<int>();
        private readonly HashSet<int> _previousEnemyContactIds = new HashSet<int>();
        private readonly Queue<CharacterUnit> _activeSpawnedUnits = new Queue<CharacterUnit>();
        private readonly Dictionary<int, float> _nextAttackTimes = new Dictionary<int, float>();

        private struct PendingProjectileAttack
        {
            public CharacterUnit Unit;
            public float TriggerTime;
        }
        private readonly List<PendingProjectileAttack> _pendingProjectileAttacks = new List<PendingProjectileAttack>(32);
        private readonly List<CharacterUnit> _unitSnapshotBuffer = new List<CharacterUnit>(64);
        private GameObject _weaponOverridePrefab;
        private int _baseAttackDamage = 1;
        private int _resolvedWeaponDamage;
        private int _damageBonusPoints;
        private float _baseAttackInterval;
        private float _baseProjectileDuration;
        private int _fireRateBonusPoints;
        private float _baseFireRange;
        private float _fireRangeBonus;
        private static readonly Dictionary<int, Vector2Int[]> s_honeycombRingCache = new Dictionary<int, Vector2Int[]>(16);

        private const int HardMaxActiveSpawnedUnits = 40;
        private const float HoneycombForwardStepFactor = 0.8660254f;
        private static readonly Vector2Int[] HoneycombDirections = new Vector2Int[6]
        {
            new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(1, -1)
        };

        private readonly HashSet<int> _finishTowerHitIdsThisFrame = new HashSet<int>();
        private int _finishTowerLastHitFrame = -1;

        private readonly Dictionary<string, int> _samuraiAttackCounters = new Dictionary<string, int>();
        private float _lastSwordSkillIncrementTime = -1f;
        private readonly List<GameObject> _prewarmPrefabBuffer = new List<GameObject>(16);

        public IReadOnlyList<CharacterUnit> Units => characterUnits;
        public PlayerArmyEffectSystem EffectSystem => effectSystem;
        public PlayerArmyState CurrentState => currentState;
        public int ResolvedWeaponDamage => _resolvedWeaponDamage;
        public bool IsActive => currentState != PlayerArmyState.Idle;
        public Transform BodyTransform => bodyRoot != null ? bodyRoot : transform;

        public Transform Transform => transform;
        public bool IsEnabled => isActiveAndEnabled;
        public Vector3 Position => bodyRoot != null ? bodyRoot.position : transform.position;
        public EntityType EntityType => EntityType.Wheel;
        public Vector2 Size => collisionSize;
        public int Damage => ResolveEffectiveAttackDamage();
        public uint TargetMask => 1 << (int)EntityType.Item |
                                         1 << (int)EntityType.Enemy |
                                         1 << (int)EntityType.ResourceTower |
                                         1 << (int)EntityType.CapacityFactory |
                                         1 << (int)EntityType.CapacityGate |
                                         1 << (int)EntityType.PowerGate |
                                         1 << (int)EntityType.FinishTrigger |
                                         1 << (int)EntityType.FinishTower |
                                         1 << (int)EntityType.GateNewEra;


        private bool _isInitialized = false;

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            ResolveDependencies();
            CacheDefaultState();
            //ClearSceneUnits();
            ResetRuntimeSpawnState();

            var sceneUnits = GetComponentsInChildren<CharacterUnit>(true);
            for (int i = 0; i < sceneUnits.Length; i++)
            {
                var unit = sceneUnits[i];
                if (unit == null || unit == this) continue;

                // Nếu unit chưa được đăng ký, gọi AddUnit
                if (!characterUnits.Contains(unit))
                {
                    AddUnit(unit, true, true);
                }
            }
        }

        private void Start()
        {
            if (!_isInitialized)
            {
                Initialize();
                currentState = PlayerArmyState.Active;
                SubscribeWeaponChange();
            }
            else
            {
                SubscribeWeaponChange();
            }
        }

        public IEnumerator PrewarmArmyPrefabsAsync(int maxPerFrame)
        {
            BuildArmyPrewarmPrefabList(ResolveCharacterList(), _prewarmPrefabBuffer);

            int batchSize = Mathf.Max(1, maxPerFrame);
            for (int i = 0; i < _prewarmPrefabBuffer.Count; i++)
            {
                var prefab = _prewarmPrefabBuffer[i];
                if (prefab == null) continue;

                int count = 10; // default to 15 for weapons/projectiles
                if (prefab.GetComponent<CharacterUnit>() != null)
                {
                    count = 7; // prewarm 7 for character units
                }

                var poolable = prefab.GetComponent<IPoolable>();
                if (poolable is Component comp)
                {
                    yield return PoolSystem.PrewarmAsync(comp, count, batchSize);
                }
                else
                {
                    yield return PoolSystem.PrewarmAsync(prefab.transform, count, batchSize);
                }
            }
        }

        private void BuildArmyPrewarmPrefabList(CharacterListDataSO list, List<GameObject> buffer)
        {
            buffer.Clear();
            if (weaponProjectilePrefab != null)
                buffer.Add(weaponProjectilePrefab.gameObject);

            if (list != null && list.Characters != null)
            {
                for (int i = 0; i < list.Characters.Count; i++)
                {
                    var entry = list.Characters[i];
                    if (entry != null)
                    {
                        if (entry.WeaponPrefab != null)
                        {
                            buffer.Add(entry.WeaponPrefab);
                        }
                        if (entry.CharacterPrefab != null)
                        {
                            buffer.Add(entry.CharacterPrefab.gameObject);
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            UnsubscribeWeaponChange();
        }

        private void SubscribeWeaponChange()
        {
            var manager = GameplayManager.Instance;
            if (manager != null)
            {
                manager.OnWeaponChange += OnWeaponChanged;
            }

            GameEventBus.OnAddWheelCard += HandleAddArmyCardEvent;
        }

        private void UnsubscribeWeaponChange()
        {
            var manager = GameplayManager.Instance;
            if (manager != null)
            {
                manager.OnWeaponChange -= OnWeaponChanged;
            }

            GameEventBus.OnAddWheelCard -= HandleAddArmyCardEvent;
        }

        private void HandleAddArmyCardEvent()
        {
            if (useSceneUnitsOnly && characterUnits.Count > 0)
            {
                return;
            }

            // SpawnUnits(fallbackCharacterLevel, 1);
        }

        private void OnWeaponChanged(WeaponItem weapon)
        {
            var prefab = ResolveWeaponPrefab(weapon);
            _weaponOverridePrefab = prefab;
            ApplyWeaponOverrideToAllUnits(prefab);

            var projectilePrefab = ResolveProjectilePrefab(weapon);
            UpdateProjectilePrefab(projectilePrefab);
        }

        /// <summary>
        /// Applies a weapon prefab override to every active unit.
        /// Pass null to clear to the unit's default weapon.
        /// </summary>
        private void ApplyWeaponOverrideToAllUnits(GameObject prefab)
        {
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null)
                {
                    continue;
                }

                unit.SetWeaponPrefabOverride(prefab);
            }
        }

        private GameObject ResolveWeaponPrefab(WeaponItem weapon)
        {
            _resolvedWeaponDamage = 0;
            RefreshCombatDamage();

            if (weapon == null)
            {
                return null;
            }

            var list = ResolveCharacterList();
            if (list == null)
            {
                return null;
            }

            var entry = list.GetCharacterByLevel(weapon.Tier);
            if (entry != null && entry.WeaponPrefab != null)
            {
                _resolvedWeaponDamage = Mathf.Max(0, entry.WeaponDamage);
                RefreshCombatDamage();
                return entry.WeaponPrefab;
            }

            RefreshCombatDamage();
            return null;
        }

        /// <summary>
        /// Resolves the projectile prefab for a given weapon tier.
        /// Derives from the weapon prefab's WeaponUnit component if available.
        /// TODO: Consider storing projectile prefab directly in CharacterEntry if needed.
        /// </summary>
        private WeaponUnit ResolveProjectilePrefab(WeaponItem weapon)
        {
            if (weapon == null)
            {
                return null;
            }

            var weaponPrefab = ResolveWeaponPrefab(weapon);
            if (weaponPrefab == null)
            {
                return null;
            }

            var weaponUnit = weaponPrefab.GetComponent<WeaponUnit>();
            return weaponUnit;
        }

        private void UpdateProjectilePrefab(WeaponUnit prefab)
        {
            if (prefab != null)
            {
                weaponProjectilePrefab = prefab;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveDependencies();
            maxActiveSpawnedUnits = Mathf.Clamp(maxActiveSpawnedUnits, 1, 20);
            if (characterUnits == null) characterUnits = new List<CharacterUnit>();
        }
#endif

        private void Update()
        {
            for (int i = _pendingSwordSkills.Count - 1; i >= 0; i--)
            {
                var skill = _pendingSwordSkills[i];
                skill.RemainingDelay -= Time.deltaTime;
                if (skill.RemainingDelay <= 0f)
                {
                    LaunchSwordSkill(skill.WeaponPrefab, skill.SamuraiConfig, skill.Unit, skill.StartPoint, skill.Forward, skill.Rotation, skill.Distance, skill.Damage);
                    _pendingSwordSkills.RemoveAt(i);
                }
                else
                {
                    _pendingSwordSkills[i] = skill;
                }
            }

            if (currentState == PlayerArmyState.Idle)
            {
                return;
            }

            if (!GameplayManager.IsGameStarted)
            {
                return;
            }

            PruneInactiveSpawnedUnits();

            float dt = Time.deltaTime;

            UpdateMovement(dt);

            UpdateCollisionChecks();

            if (currentState == PlayerArmyState.Active)
            {
                UpdateCharacterAttacks();
            }

            UpdatePendingProjectileAttacks();
        }

        public void AddUnit(CharacterUnit unit, bool parentToRoot = true)
        {
            AddUnit(unit, parentToRoot, true);
        }

        public void AddUnit(CharacterUnit unit, bool parentToRoot, bool initialize)
        {
            if (unit == null || characterUnits.Contains(unit)) return;
            characterUnits.Add(unit);
            if (parentToRoot) unit.transform.SetParent(GetBodyRoot(), true);

            if (initialize && !useSceneUnitsOnly)
            {
                InitializeRuntimeUnit(unit, ResolveSpawnLevel(unit.Level > 0 ? unit.Level : fallbackCharacterLevel));
            }
            else if (!useSceneUnitsOnly)
            {
                RegisterRuntimeUnit(unit);
                SetNextAttackTime(unit, Time.time + attackInterval);
                if (!_activeSpawnedUnits.Contains(unit))
                {
                    _activeSpawnedUnits.Enqueue(unit);
                }
            }
        }

        public bool RemoveUnit(CharacterUnit unit, bool deactivate = false)
        {
            if (unit == null)
            {
                return false;
            }

            if (!characterUnits.Remove(unit))
            {
                return false;
            }

            UnregisterRuntimeUnit(unit, deactivate);
            return true;
        }

        public void ClearUnits(bool deactivate = false)
        {
            for (int i = characterUnits.Count - 1; i >= 0; i--)
            {
                UnregisterRuntimeUnit(characterUnits[i], deactivate);
            }

            characterUnits.Clear();
            _activeSpawnedUnits.Clear();
            _nextAttackTimes.Clear();
        }

        public CharacterUnit SpawnCharacterUnit(int level, Vector3 position, Quaternion rotation, float? nextAttackTime = null, bool playMoveAnimation = false)
        {
            if (!TryReserveSpawnSlot())
            {
                return null;
            }

            var unit = CreateRuntimeCharacterUnit(level, position, rotation, nextAttackTime, playMoveAnimation);
            if (unit == null)
            {
                return null;
            }

            if (!characterUnits.Contains(unit))
            {
                characterUnits.Add(unit);
            }

            return unit;
        }

        private CharacterUnit CreateRuntimeCharacterUnit(int level, Vector3 position, Quaternion rotation, float? nextAttackTime = null, bool playMoveAnimation = false)
        {
            int resolvedLevel = ResolveSpawnLevel(level);
            var list = ResolveCharacterList();
            if (list == null)
            {
                return null;
            }

            var entry = list.GetCharacterByLevel(resolvedLevel);
            if (entry == null || entry.CharacterPrefab == null)
            {
                return null;
            }

            var unit = entry.CharacterPrefab.Spawn(position, rotation, GetBodyRoot());
            if (unit == null)
            {
                return null;
            }

            InitializeRuntimeUnit(unit, resolvedLevel, nextAttackTime, playMoveAnimation);
            return unit;
        }

        private int ResolveSpawnLevel(int requestedLevel)
        {
            int desiredLevel = Mathf.Max(1, requestedLevel > 0 ? requestedLevel : fallbackCharacterLevel);
            var list = ResolveCharacterList();
            if (list == null)
            {
                return desiredLevel;
            }

            var lookup = list.GetCharacterLookup();
            if (lookup == null || lookup.Count == 0)
            {
                return desiredLevel;
            }

            if (lookup.ContainsKey(desiredLevel))
            {
                return desiredLevel;
            }

            int maxLevel = int.MinValue;
            int minLevel = int.MaxValue;
            foreach (var key in lookup.Keys)
            {
                if (key > maxLevel)
                {
                    maxLevel = key;
                }
                if (key < minLevel)
                {
                    minLevel = key;
                }
            }

            if (maxLevel == int.MinValue || minLevel == int.MaxValue)
            {
                return desiredLevel;
            }

            // Clamp overflow upgrades to highest available level instead of wrapping to low tiers.
            if (desiredLevel > maxLevel)
            {
                return maxLevel;
            }

            if (desiredLevel < minLevel)
            {
                return minLevel;
            }

            // Choose the nearest lower-or-equal available level.
            int bestLower = int.MinValue;
            foreach (var key in lookup.Keys)
            {
                if (key <= desiredLevel && key > bestLower)
                {
                    bestLower = key;
                }
            }

            if (bestLower != int.MinValue)
            {
                return bestLower;
            }

            int lowestLevel = minLevel;
            foreach (var key in lookup.Keys)
            {
                if (key < lowestLevel)
                {
                    lowestLevel = key;
                }
            }

            return lowestLevel != int.MaxValue ? lowestLevel : desiredLevel;
        }

        private class DelayedSwordSkill
        {
            public WeaponUnit WeaponPrefab;
            public CardSystem.Data.SamuraiSkillConfigSO SamuraiConfig;
            public CharacterUnit Unit;
            public Vector3 StartPoint;
            public Vector3 Forward;
            public Quaternion Rotation;
            public float Distance;
            public int Damage;
            public float RemainingDelay;
        }

        private readonly List<DelayedSwordSkill> _pendingSwordSkills = new List<DelayedSwordSkill>(8);

        private void LaunchSwordSkill(
            WeaponUnit weaponPrefab,
            CardSystem.Data.SamuraiSkillConfigSO samuraiConfig,
            CharacterUnit unit,
            Vector3 startPoint,
            Vector3 forward,
            Quaternion rotation,
            float distance,
            int damage)
        {
            if (this == null || !IsActive || unit == null || weaponPrefab == null || samuraiConfig == null) return;

            float speed = samuraiConfig.ProjectileSpeed > 0 ? samuraiConfig.ProjectileSpeed : 60f;
            float duration = distance / speed;
            var swordProjectile = weaponPrefab.Spawn(startPoint, rotation, null);
            if (swordProjectile != null)
            {
                swordProjectile.transform.localScale = unit.SelfScale * Mathf.Max(0f, unitscalevalue);
                swordProjectile.SetFly();
                if (!swordProjectile.Launch(
                    startPoint,
                    forward,
                    distance,
                    duration,
                    0f,
                    0f,
                    damage,
                    EnemyProjectileSystem.ProjectileSpinAxis.X,
                    EnemyProjectileSystem.ProjectileMotionMode.Straight))
                {
                    swordProjectile.Despawn();
                }
            }
        }

        private void SpawnUnits(int level, int amount, float? nextAttackTime = null, bool playMoveAnimation = false)
        {
            int spawnCount = Mathf.Max(0, amount);
            if (spawnCount <= 0)
            {
                return;
            }

            int resolvedLevel = ResolveSpawnLevel(level);
            Transform root = GetBodyRoot();
            Quaternion rotation = root.rotation;
            int startIndex = characterUnits.Count;
            int totalCount = startIndex + spawnCount;

            for (int i = 0; i < spawnCount; i++)
            {
                int index = startIndex + i;
                Vector3 spawnPosition = GetHoneycombSpawnPosition(root, index, totalCount);
                SpawnCharacterUnit(resolvedLevel, spawnPosition, rotation, nextAttackTime, playMoveAnimation);
            }

            if (_weaponOverridePrefab != null)
            {
                ApplyWeaponOverrideToAllUnits(_weaponOverridePrefab);
            }
        }

        private float ResolveSharedNextAttackTimeForSpawn()
        {
            float nextAttackTime = Time.time + attackInterval;
            bool hasNextAttackTime = false;

            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null || !unit.IsActive)
                {
                    continue;
                }

                int id = unit.GetInstanceID();
                if (!_nextAttackTimes.TryGetValue(id, out float unitNextAttackTime))
                {
                    continue;
                }

                if (!hasNextAttackTime || unitNextAttackTime < nextAttackTime)
                {
                    nextAttackTime = unitNextAttackTime;
                    hasNextAttackTime = true;
                }
            }

            return nextAttackTime;
        }

        public void PlayEffect(EffectType effectType, Transform anchor = null, Action onComplete = null, float waitForAction = 0f)
        {
            effectSystem?.PlayEffect(effectType, anchor != null ? anchor : GetBodyRoot(), onComplete, waitForAction);
        }

        public void PlayEffectAt(EffectType effectType, Vector3 position, Quaternion rotation, Transform parent = null, Action onComplete = null, float waitForAction = 0f)
        {
            effectSystem?.PlayEffectAt(effectType, position, rotation, parent != null ? parent : GetBodyRoot(), onComplete, waitForAction);
        }

        public void OnAttackSucceed(IHitable target)
        {
            OnAttackComplete?.Invoke(target);
        }

        public void Setup(int damage)
        {
            _baseAttackDamage = Mathf.Max(1, damage);
            RefreshCombatDamage();
        }

        public void ApplyFireRangeModifier(int value)
        {
            if (value == 0)
            {
                return;
            }

            _fireRangeBonus += value;
            RefreshFireRange();
        }

        private void ApplyUnitCombatProfile(CharacterListDataSO.CharacterEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _baseAttackDamage = Mathf.Max(1, entry.UnitDamage);
            _baseFireRange = Mathf.Max(0.1f, entry.FireRange);
            RefreshCombatDamage();
            RefreshFireRange();
        }

        private void RefreshCombatDamage()
        {
            attackDamage = Mathf.Max(1, _baseAttackDamage + Mathf.Max(0, _resolvedWeaponDamage) + Mathf.Max(0, _damageBonusPoints));
        }

        private void RefreshFireRange()
        {
            projectileDistance = Mathf.Max(0.1f, _baseFireRange + _fireRangeBonus);
        }

        private int ResolveEffectiveAttackDamage()
        {
            return Mathf.Max(1, attackDamage);
        }

        public void Dispose()
        {
            ClearUnits(true);
        }

        public void SetIdle()
        {
            currentState = PlayerArmyState.Idle;
            foreach (var unit in characterUnits)
            {
                if (unit != null && unit.IsActive)
                {
                    unit.PlayAnimation(AnimationType.Idle, 0f, null);
                }
            }
        }

        public void SetActive()
        {
            currentState = PlayerArmyState.Active;
            if (characterUnits != null)
            {
                for (int i = 0; i < characterUnits.Count; i++)
                {
                    if (characterUnits[i] != null)
                    {
                        characterUnits[i].ShowWeapon();
                        characterUnits[i].PlayAnimation(AnimationType.Idle, 0f, null);
                        _nextAttackTimes[characterUnits[i].GetInstanceID()] = Time.time;
                    }
                }
            }
        }

        public void SetIntroRun()
        {
            currentState = PlayerArmyState.IntroRun;
            foreach (var unit in characterUnits)
            {
                if (unit != null)
                {
                    unit.PlayAnimation(AnimationType.Move, 0f, null);
                }
            }
        }

        public void AddCards(List<CardSpawnRequestData> requests, CardSpawnEffectType effectType)
        {
            if (useSceneUnitsOnly && effectType == CardSpawnEffectType.DropWithoutAction && characterUnits.Count > 0)
            {
                int targetLevel = fallbackCharacterLevel;
                if (requests != null && requests.Count > 0 && requests[0].Level > 0)
                {
                    targetLevel = requests[0].Level;
                }

                var list = ResolveCharacterList();
                if (list != null)
                {
                    ApplyUnitCombatProfile(list.GetCharacterByLevel(targetLevel));
                }

                for (int i = 0; i < characterUnits.Count; i++)
                {
                    var unit = characterUnits[i];
                    if (unit == null) continue;

                    unit.gameObject.SetActive(true);
                    unit.Initialize(targetLevel, true);
                    unit.Setup(targetLevel, GameplayManager.IsGameStarted);
                    unit.ShowWeapon();
                }
                return;
            }

            float syncNextAttackTime = ResolveSharedNextAttackTimeForSpawn();
            bool playMoveAnimation = effectType != CardSpawnEffectType.DropWithoutAction;

            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                int level = req.Level > 0 ? req.Level : ResolveCurrentArmyLevel();
                int amount = Mathf.Max(1, req.Amount);
                SpawnUnits(level, amount, syncNextAttackTime, playMoveAnimation);
            }
        }

        public void ApplyFireRateModifier(int value)
        {
            if (value <= 0)
            {
                return;
            }

            _fireRateBonusPoints += value;

            const float fireRateStep = 0.05f;
            attackInterval = Mathf.Max(0.05f, _baseAttackInterval - _fireRateBonusPoints * fireRateStep);
            projectileDuration = Mathf.Max(0.05f, _baseProjectileDuration - _fireRateBonusPoints * fireRateStep);

            float nextAttackTime = Time.time + attackInterval;
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null || !unit.IsActive)
                {
                    continue;
                }

                _nextAttackTimes[unit.GetInstanceID()] = nextAttackTime;
            }
        }

        public void ApplyDamageModifier(int value)
        {
            if (value <= 0)
            {
                return;
            }

            _damageBonusPoints += value;
            RefreshCombatDamage();
        }

        public void ApplyExplosionShotModifier(int value)
        {
            // ExplosionShot runtime state is managed centrally by GameplayManager.
        }

        public bool HasExplosionShot => GameplayManager.Instance != null && GameplayManager.Instance.IsExplosionShotUnlocked;

        public float ExplosionRadius => GameplayManager.Instance != null ? Mathf.Max(0f, GameplayManager.Instance.ExplosionShotRadius) : 0f;

        public int ResolveExplosionDamage(int baseDamage)
        {
            if (baseDamage <= 0 || GameplayManager.Instance == null || !GameplayManager.Instance.IsExplosionShotUnlocked)
            {
                return 0;
            }

            int percent = Mathf.Max(0, GameplayManager.Instance.ExplosionShotDamagePercent);
            if (percent <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.CeilToInt(baseDamage * (percent / 100f)));
        }

        public void UpgradeAllUnitsToLevel(int levelbonus, bool includeWeapon = true)
        {
            // if (levelbonus <= 0 || characterUnits == null || characterUnits.Count == 0)
            // {
            //     return;
            // }

            // int oldLevel = 1;
            // for (int i = 0; i < characterUnits.Count; i++)
            // {
            //     var unit = characterUnits[i];
            //     if (unit != null && unit.Level > 0)
            //     {
            //         oldLevel = unit.Level;
            //         break;
            //     }
            // }

            // int targetLevel = Mathf.Max(1, oldLevel + levelbonus);
            // fallbackCharacterLevel = targetLevel;
            // _unitSnapshotBuffer.Clear();
            // _unitSnapshotBuffer.AddRange(characterUnits);

            // for (int i = 0; i < _unitSnapshotBuffer.Count; i++)
            // {
            //     var oldUnit = _unitSnapshotBuffer[i];
            //     if (oldUnit == null)
            //     {
            //         continue;
            //     }

            //     int oldUnitId = oldUnit.GetInstanceID();
            //     if (!_nextAttackTimes.TryGetValue(oldUnitId, out float nextAttackTime))
            //     {
            //         nextAttackTime = Time.time + attackInterval;
            //     }

            //     Vector3 position = oldUnit.transform.position;
            //     Quaternion rotation = oldUnit.transform.rotation;
            //     var newUnit = CreateRuntimeCharacterUnit(targetLevel, position, rotation, nextAttackTime, true);
            //     if (newUnit == null)
            //     {
            //         continue;
            //     }

            //     if (!RemoveUnit(oldUnit, true))
            //     {
            //         newUnit.RecycleImmediate(false);
            //         continue;
            //     }

            //     int insertIndex = Mathf.Clamp(i, 0, characterUnits.Count);
            //     characterUnits.Insert(insertIndex, newUnit);

            //     if (!includeWeapon)
            //     {
            //         newUnit.Setup(targetLevel, false);
            //     }
            // }
        }

        private int ResolveCurrentArmyLevel()
        {
            int level = Mathf.Max(1, fallbackCharacterLevel);
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null || unit.Level <= 0)
                {
                    continue;
                }

                if (unit.Level > level)
                {
                    level = unit.Level;
                }
            }

            return level;
        }

        public void PlayAnimationForAllUnits(AnimationType animationType, float waitForAction = 0f)
        {
            _unitSnapshotBuffer.Clear();
            _unitSnapshotBuffer.AddRange(characterUnits);

            for (int i = 0; i < _unitSnapshotBuffer.Count; i++)
            {
                var unit = _unitSnapshotBuffer[i];
                if (unit == null)
                {
                    continue;
                }

                unit.PlayAnimation(animationType, waitForAction, null);
            }
        }

        public IReadOnlyList<CardSpawnRequestData> GetQueuedCardRequests() => null;

        public void ClearQueuedRequests() { }

        private void ResolveDependencies()
        {
            if (bodyRoot == null)
            {
                bodyRoot = transform;
            }

            if (inputManager == null)
            {
                inputManager = InputManager.Instance;
            }

            if (effectSystem == null)
            {
                effectSystem = GetComponentInChildren<PlayerArmyEffectSystem>(true);
            }

            if (characterUnits == null) characterUnits = new List<CharacterUnit>();
        }

        private CharacterListDataSO ResolveCharacterList()
        {
            if (characterList != null)
            {
                return characterList;
            }

            return null;
        }

        private void ClearSceneUnits()
        {
            var sceneUnits = GetComponentsInChildren<CharacterUnit>(true);
            for (int i = 0; i < sceneUnits.Length; i++)
            {
                var unit = sceneUnits[i];
                if (unit == null)
                {
                    continue;
                }

                unit.RecycleImmediate(false);
            }

            characterUnits.Clear();
            _activeSpawnedUnits.Clear();
            _nextAttackTimes.Clear();
        }

        private void ResetRuntimeSpawnState()
        {
            _activeSpawnedUnits.Clear();
            _nextAttackTimes.Clear();
        }

        private Vector3 GetHoneycombSpawnPosition(Transform root, int index, int totalCount)
        {
            int cappedTotal = Mathf.Max(1, Mathf.Min(totalCount, Mathf.Min(maxActiveSpawnedUnits, HardMaxActiveSpawnedUnits)));
            int safeIndex = Mathf.Clamp(index, 0, cappedTotal - 1);
            if (safeIndex == 0)
            {
                return root.position;
            }

            int ring = 1;
            int remaining = safeIndex - 1;
            while (remaining >= 6 * ring)
            {
                remaining -= 6 * ring;
                ring++;
            }

            Vector2Int axial = GetHoneycombRingAxialPosition(ring, remaining);
            float spacing = Mathf.Max(0.01f, unitSpacing);
            float xStep = spacing;
            float zStep = spacing * HoneycombForwardStepFactor;

            Vector3 position = root.position;
            position += root.right * ((axial.x + axial.y * 0.5f) * xStep);
            position += root.forward * (axial.y * zStep);

            float jitterSeed = safeIndex * 0.61803398875f;
            float jitterX = (Mathf.PerlinNoise(jitterSeed, cappedTotal * 0.13f) - 0.5f) * spacing * 0.18f;
            float jitterZ = (Mathf.PerlinNoise(cappedTotal * 0.17f, jitterSeed) - 0.5f) * zStep * 0.18f;
            position += root.right * jitterX;
            position += root.forward * jitterZ;

            return position;
        }

        private static Vector2Int GetHoneycombRingAxialPosition(int ring, int offsetInRing)
        {
            ring = Mathf.Max(1, ring);
            int step = Mathf.Max(0, offsetInRing);
            var positions = GetOrBuildHoneycombRingPositions(ring);
            if (positions == null || positions.Length == 0)
                return Vector2Int.zero;
            return positions[Mathf.Clamp(step, 0, positions.Length - 1)];
        }

        private static Vector2Int[] GetOrBuildHoneycombRingPositions(int ring)
        {
            if (s_honeycombRingCache.TryGetValue(ring, out var cached) && cached != null && cached.Length > 0)
                return cached;

            var positions = new Vector2Int[ring * 6];
            int index = 0;
            Vector2Int axial = Vector2Int.zero;

            for (int i = 0; i < ring; i++)
                axial += HoneycombDirections[4];

            for (int side = 0; side < 6; side++)
            {
                for (int i = 0; i < ring; i++)
                {
                    positions[index++] = axial;
                    axial += HoneycombDirections[side];
                }
            }

            Array.Sort(positions, CompareHoneycombPosition);
            s_honeycombRingCache[ring] = positions;
            return positions;
        }

        private static int CompareHoneycombPosition(Vector2Int a, Vector2Int b)
        {
            float aZ = Mathf.Abs(a.y);
            float bZ = Mathf.Abs(b.y);
            if (!Mathf.Approximately(aZ, bZ))
                return aZ.CompareTo(bZ);

            if (a.y != b.y)
                return a.y.CompareTo(b.y);

            float aX = a.x + a.y * 0.5f;
            float bX = b.x + b.y * 0.5f;
            if (!Mathf.Approximately(aX, bX))
                return aX.CompareTo(bX);

            return a.x.CompareTo(b.x);
        }

        private void CacheDefaultState()
        {
            var root = GetBodyRoot();
            _targetX = root.localPosition.x;
            _currentForwardSpeed = fallbackForwardSpeed;
            _baseAttackInterval = Mathf.Max(0.05f, attackInterval);
            _baseProjectileDuration = Mathf.Max(0.05f, projectileDuration);
            _fireRateBonusPoints = 0;
            _baseFireRange = projectileDistance;
            _fireRangeBonus = 0f;
            _damageBonusPoints = 0;
            RefreshCombatDamage();
            RefreshFireRange();
        }

        private void UpdateMovement(float dt)
        {
            float targetSpeed = fallbackForwardSpeed;
            float speedChangeRate = Mathf.Max(0.01f, fallbackSpeedChangeRate);
            _currentForwardSpeed = Mathf.Lerp(_currentForwardSpeed, targetSpeed, dt * speedChangeRate);
            transform.position += transform.forward * (_currentForwardSpeed * dt);

            if (inputManager == null)
            {
                inputManager = InputManager.Instance;
            }

            float inputDelta = inputManager != null ? inputManager.GetMoveDelta() : 0f;
            float inputGain = Mathf.Clamp(inputSensitivity * 100f, 0.5f, 3f);
            float scaledInputDelta = inputDelta * inputGain;
            float tempTargetX = _targetX + (scaledInputDelta * strafeFollowMultiplier);
            tempTargetX = Mathf.Clamp(tempTargetX, -xLimit, xLimit);

            Transform root = GetBodyRoot();
            Vector3 localPos = root.localPosition;
            float baseSmoothness = 0.15f;
            float effectiveSmoothness = Mathf.Clamp01(baseSmoothness * Mathf.Max(1f, strafeFollowMultiplier) * (dt * 60f));
            float newX = Mathf.Lerp(localPos.x, tempTargetX, effectiveSmoothness);
            root.localPosition = new Vector3(newX, localPos.y, localPos.z);
            _targetX = tempTargetX;
        }

        private void UpdateCollisionChecks()
        {
            var collisionSystem = CollisionSystem.Instance;
            if (collisionSystem == null || collisionSystem.Count <= 0)
            {
                _previousEnemyContactIds.Clear();
                _currentEnemyContactIds.Clear();
                return;
            }

            _currentEnemyContactIds.Clear();
            Vector3 myPos = Position;
            Vector2 mySize = Size;
            uint myMask = TargetMask;
            float myHalfX = mySize.x * 0.5f;
            float myHalfZ = mySize.y * 0.5f;
            float preCullX = Mathf.Max(myHalfX + 1f, collisionCheckRangeX);
            float preCullZ = Mathf.Max(myHalfZ + 1f, collisionCheckRangeZ);

            for (int i = 0; i < collisionSystem.Count; i++)
            {
                var target = collisionSystem.GetTargetBySortedIndex(i);
                if (target == null || !target.IsActive)
                {
                    continue;
                }

                if (ReferenceEquals(target, this))
                {
                    continue;
                }

                var targetTr = collisionSystem.GetTransform(i);
                if (targetTr == null)
                {
                    continue;
                }

                Vector3 tPos = targetTr.position;
                float distX = tPos.x - myPos.x;
                float distZ = tPos.z - myPos.z;
                if (distX < -preCullX || distX > preCullX || distZ < -preCullZ || distZ > preCullZ)
                {
                    continue;
                }

                var colData = collisionSystem.GetColliderData(i);
                uint categoryBits = colData.CategoryBits != 0
                    ? colData.CategoryBits
                    : (uint)(1 << (int)target.EntityType);
                if ((myMask & categoryBits) == 0)
                {
                    continue;
                }

                float tHalfX = Mathf.Abs(colData.Size.x);
                float tHalfZ = Mathf.Abs(colData.Size.z);
                if (colData.Type != ShapeType.Box)
                {
                    tHalfZ = Mathf.Max(tHalfX, tHalfZ);
                    tHalfX = tHalfZ;
                }

                bool hitX = distX <= (myHalfX + tHalfX);
                bool hitZ = distZ <= (myHalfZ + tHalfZ);
                if (!hitX || !hitZ)
                {
                    continue;
                }

                if (target.EntityType == EntityType.Enemy)
                {
                    int enemyInstanceId = targetTr.GetInstanceID();
                    _currentEnemyContactIds.Add(enemyInstanceId);

                    if (!_previousEnemyContactIds.Contains(enemyInstanceId))
                    {
                        ResolveEnemyContact(target, tPos);
                    }
                }
                else if (target.EntityType == EntityType.FinishTower)
                {
                    ResolveFinishTowerContact(target, tPos, tHalfX, tHalfZ);
                }
                else if (IsEnvironmentTarget(target.EntityType))
                {
                    target.OnHit(this);
                }
            }

            _previousEnemyContactIds.Clear();
            _previousEnemyContactIds.UnionWith(_currentEnemyContactIds);
        }

        private void UpdateCharacterAttacks()
        {
            if (characterUnits.Count == 0)
            {
                return;
            }

            float now = Time.time;
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null || !unit.IsActive)
                {
                    continue;
                }

                int id = unit.GetInstanceID();
                if (!_nextAttackTimes.TryGetValue(id, out float nextAttackTime) || now < nextAttackTime)
                {
                    continue;
                }

                TryPerformAttack(unit);
                _nextAttackTimes[id] = now + Mathf.Max(0.05f, attackInterval);
            }
        }

        private bool TryPerformAttack(CharacterUnit unit)
        {
            switch (attackMode)
            {
                case PlayerArmyAttackMode.ThrownProjectile:
                    return TryPerformThrownProjectileAttack(unit);
                default:
                    return TryPerformDirectAttack(unit);
            }
        }

        private bool TryPerformDirectAttack(CharacterUnit unit)
        {
            if (unit == null || !unit.IsActive)
            {
                return false;
            }

            Vector2 attackWindow = ResolveAttackWindow();
            Vector3 origin = unit.transform.position + unit.transform.forward * Mathf.Max(0f, attackOriginOffset);
            if (!TryFindBestForwardTarget(unit, origin, Mathf.Max(0.1f, attackWindow.y), attackWindow.x, out var targetInfo))
            {
                return false;
            }

            unit.PlayAnimation(AnimationType.Attack, 0f, null);

            int effectiveDamage = ResolveEffectiveAttackDamage();
            var attackSource = GetUnitAttackSource();
            attackSource.SetupSource(unit.transform, origin, attackWindow, effectiveDamage, TargetMask);
            attackSource.OnAttackSucceed(targetInfo.Target);
            targetInfo.Target.OnHit(attackSource);
            OnAttackComplete?.Invoke(targetInfo.Target);
            attackSource.Dispose();
            if (effectSystem != null)
            {
                effectSystem.PlayEffectAt(EffectType.Attack, targetInfo.Position, Quaternion.identity, unit.transform, null, 0f);
            }

            return true;
        }

        private bool TryPerformThrownProjectileAttack(CharacterUnit unit)
        {
            if (unit == null || !unit.IsActive)
            {
                return false;
            }

            if (weaponProjectilePrefab == null)
            {
                return TryPerformDirectAttack(unit);
            }

            unit.ShowWeapon();
            unit.PlayAnimation(AnimationType.Attack, 0.4f, null);

            _pendingProjectileAttacks.Add(new PendingProjectileAttack
            {
                Unit = unit,
                TriggerTime = Time.time + 0.4f
            });

            return true;
        }

        private void UpdatePendingProjectileAttacks()
        {
            if (_pendingProjectileAttacks.Count == 0) return;

            float now = Time.time;
            for (int i = _pendingProjectileAttacks.Count - 1; i >= 0; i--)
            {
                var attack = _pendingProjectileAttacks[i];
                if (now >= attack.TriggerTime)
                {
                    _pendingProjectileAttacks.RemoveAt(i);
                    ExecuteThrownProjectileAttack(attack.Unit);
                }
            }
        }

        private void ExecuteThrownProjectileAttack(CharacterUnit unit)
        {
            if (!GameplayManager.IsGameStarted || unit == null || !unit.IsActive)
            {
                return;
            }

            Vector3 forward = unit.transform.forward;
            Transform projectilePoint = unit.ProjectilePoint;
            Vector3 startPoint = projectilePoint != null
                ? projectilePoint.position
                : unit.transform.position + forward * Mathf.Max(0f, attackOriginOffset);
            Quaternion rotation = unit.transform.rotation;
            float distance = Mathf.Max(0.1f, projectileDistance);
            float duration = Mathf.Max(0.55f, projectileDuration);
            int damage = ResolveEffectiveAttackDamage();
            var projectile = weaponProjectilePrefab.Spawn(startPoint, rotation, null);
            if (projectile == null)
            {
                TryPerformDirectAttack(unit);
                return;
            }

            projectile.transform.localScale = unit.SelfScale * Mathf.Max(0f, unitscalevalue);
            projectile.SetFly();

            if (!projectile.Launch(
                    startPoint,
                    forward,
                    distance,
                    duration,
                    0f,
                    projectileRotationSpeed * Mathf.Deg2Rad,
                    damage,
                    EnemyProjectileSystem.ProjectileSpinAxis.Y,
                    EnemyProjectileSystem.ProjectileMotionMode.Straight))
            {
                projectile.Despawn();
                TryPerformDirectAttack(unit);
            }

            // Samurai Sword Skill Logic
            if (GameplayManager.Instance != null && GameplayManager.Instance.ActiveSamuraiBuffs.Count > 0)
            {
                if (Time.time - _lastSwordSkillIncrementTime >= _baseAttackInterval * 0.5f)
                {
                    _lastSwordSkillIncrementTime = Time.time;
                    for (int i = 0; i < GameplayManager.Instance.ActiveSamuraiBuffs.Count; i++)
                    {
                        var samuraiBuff = GameplayManager.Instance.ActiveSamuraiBuffs[i];
                        if (samuraiBuff != null && samuraiBuff.SamuraiConfig != null && samuraiBuff.AssociatedWeapon != null)
                        {
                            string buffId = string.IsNullOrEmpty(samuraiBuff.BuffId) ? samuraiBuff.name : samuraiBuff.BuffId;
                            if (!_samuraiAttackCounters.TryGetValue(buffId, out int count))
                            {
                                count = 0;
                            }
                            count++;

                            if (count >= samuraiBuff.SamuraiConfig.ShotThreshold)
                            {
                                count = 0;
                                // Phóng từng skill với delay nhỉnh hơn nhau để không bị trùng (0.15s, 0.3s...)
                                _pendingSwordSkills.Add(new DelayedSwordSkill
                                {
                                    WeaponPrefab = samuraiBuff.AssociatedWeapon,
                                    SamuraiConfig = samuraiBuff.SamuraiConfig,
                                    Unit = unit,
                                    StartPoint = startPoint,
                                    Forward = forward,
                                    Rotation = rotation,
                                    Distance = distance,
                                    Damage = damage * 2,
                                    RemainingDelay = 0.2f + (i * 0.15f)
                                });
                            }
                            _samuraiAttackCounters[buffId] = count;
                        }
                    }
                }
            }

            unit.PlayAttackEffect();
            unit.HideWeapon();
        }

        private struct ForwardTargetInfo
        {
            public IHitable Target;
            public Vector3 Position;
            public float ForwardDistance;
        }

        private bool TryFindBestForwardTarget(CharacterUnit unit, Vector3 origin, float range, float width, out ForwardTargetInfo result)
        {
            result = default;
            if (unit == null)
            {
                return false;
            }

            var collisionSystem = CollisionSystem.Instance;
            if (collisionSystem == null || collisionSystem.Count <= 0)
            {
                return false;
            }

            Vector3 forward = unit.transform.forward;
            Vector3 right = unit.transform.right;
            float halfWidth = Mathf.Max(0.05f, width * 0.5f);
            float bestForward = float.MaxValue;
            IHitable bestTarget = null;
            Vector3 bestPosition = default;

            for (int i = 0; i < collisionSystem.Count; i++)
            {
                var target = collisionSystem.GetTargetBySortedIndex(i);
                if (target == null || !target.IsActive || ReferenceEquals(target, unit))
                {
                    continue;
                }

                var targetTransform = collisionSystem.GetTransform(i);
                if (targetTransform == null)
                {
                    continue;
                }

                var colData = collisionSystem.GetColliderData(i);
                uint categoryBits = colData.CategoryBits != 0
                    ? colData.CategoryBits
                    : (uint)(1 << (int)target.EntityType);
                if ((TargetMask & categoryBits) == 0)
                {
                    continue;
                }

                Vector3 delta = targetTransform.position - origin;
                float forwardDistance = Vector3.Dot(delta, forward);
                if (forwardDistance < 0f || forwardDistance > range || forwardDistance >= bestForward)
                {
                    continue;
                }

                float lateralDistance = Vector3.Dot(delta, right);
                if (lateralDistance < 0) lateralDistance = -lateralDistance; // faster than Mathf.Abs

                float targetHalfWidth = colData.Size.x > colData.Size.z ? colData.Size.x : colData.Size.z;
                if (targetHalfWidth < 0) targetHalfWidth = -targetHalfWidth; // fallback to abs logic just in case

                if (lateralDistance > halfWidth + targetHalfWidth)
                {
                    continue;
                }

                bestForward = forwardDistance;
                bestTarget = target;
                bestPosition = targetTransform.position;
            }

            if (bestTarget == null)
            {
                return false;
            }

            result = new ForwardTargetInfo
            {
                Target = bestTarget,
                Position = bestPosition,
                ForwardDistance = bestForward
            };
            return true;
        }

        private Vector2 ResolveAttackWindow()
        {
            switch (attackMode)
            {
                case PlayerArmyAttackMode.Melee:
                    return meleeAttackSize;
                case PlayerArmyAttackMode.ForwardRanged:
                case PlayerArmyAttackMode.ThrownProjectile:
                default:
                    return new Vector2(rangedAttackSize.x, Mathf.Max(0.1f, projectileDistance));
            }
        }

        private void InitializeRuntimeUnit(CharacterUnit unit, int level, float? nextAttackTime = null, bool playMoveAnimation = false)
        {
            if (unit == null) return;

            unit.gameObject.SetActive(true);
            if (unit.transform.parent != GetBodyRoot())
            {
                unit.transform.SetParent(GetBodyRoot(), true);
            }

            unit.Initialize(level, true);
            unit.Setup(level, GameplayManager.IsGameStarted);

            var list = ResolveCharacterList();
            if (list != null)
            {
                ApplyUnitCombatProfile(list.GetCharacterByLevel(level));
            }

            if (_weaponOverridePrefab != null)
            {
                unit.SetWeaponPrefabOverride(_weaponOverridePrefab);
            }
            else
            {
                if (GameplayManager.IsGameStarted) unit.ShowWeapon(); else unit.HideWeapon();
            }
            unit.PlayAnimation(playMoveAnimation ? AnimationType.Move : AnimationType.Idle, 0f, null);

            RegisterRuntimeUnit(unit);
            SetNextAttackTime(unit, nextAttackTime ?? (Time.time + attackInterval));

            if (!_activeSpawnedUnits.Contains(unit))
            {
                _activeSpawnedUnits.Enqueue(unit);
            }
        }

        private void RegisterRuntimeUnit(CharacterUnit unit)
        {
            if (unit == null)
            {
                return;
            }

            CollisionSystem.Register(unit, unit.transform);
            EnemyProjectileSystem.RegisterTarget(unit);
        }

        private void UnregisterRuntimeUnit(CharacterUnit unit, bool deactivate)
        {
            if (unit == null)
            {
                return;
            }

            CollisionSystem.Unregister(unit);
            EnemyProjectileSystem.UnregisterTarget(unit);
            _nextAttackTimes.Remove(unit.GetInstanceID());

            if (deactivate && unit.gameObject.activeInHierarchy)
            {
                unit.RecycleImmediate(false);
            }
            else if (unit.gameObject.activeInHierarchy)
            {
                unit.RecycleImmediate(false);
            }
        }

        private void PruneInactiveSpawnedUnits()
        {
            for (int i = characterUnits.Count - 1; i >= 0; i--)
            {
                var unit = characterUnits[i];
                if (unit != null && unit.IsActive)
                {
                    continue;
                }

                UnregisterRuntimeUnit(unit, false);
                characterUnits.RemoveAt(i);
            }

            _activeSpawnedUnits.Clear();
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit != null && unit.IsActive)
                {
                    _activeSpawnedUnits.Enqueue(unit);
                }
            }
        }

        private bool TryReserveSpawnSlot()
        {
            PruneInactiveSpawnedUnits();
            int cap = Mathf.Clamp(maxActiveSpawnedUnits, 1, 20);
            return _activeSpawnedUnits.Count < cap;
        }

        private void SetNextAttackTime(CharacterUnit unit, float nextAttackTime)
        {
            if (unit == null)
            {
                return;
            }

            _nextAttackTimes[unit.GetInstanceID()] = nextAttackTime;
        }

        private bool IsEnvironmentTarget(EntityType entityType)
        {
            return entityType == EntityType.CapacityFactory ||
                   entityType == EntityType.CapacityGate ||
                   entityType == EntityType.ResourceTower ||
                   entityType == EntityType.PowerGate ||
                   entityType == EntityType.Item ||
                   entityType == EntityType.FinishTrigger ||
                   entityType == EntityType.GateNewEra;
        }

        private void ResolveEnemyContact(IHitable enemyTarget, Vector3 enemyPos)
        {
            if (enemyTarget == null)
            {
                return;
            }

            if (TryGetClosestActiveCharacterUnit(enemyPos, out var victim))
            {
                victim.OnHit(enemyTarget as IAttacker ?? this);
            }

            enemyTarget.OnHit(this);
        }

        /// <summary>
        /// Khi army va chạm với FinishTower:
        /// - Lấy tất cả các unit có vị trí giao với tháp (cộng thêm unitRadius).
        /// - Giết các unit chạm tháp. Nếu số lượng unit <= số lượng va chạm, giữ lại 1 unit cuối.
        /// - Khi unit cuối cùng chạm tháp, trigger EndGame.
        /// </summary>
        private void ResolveFinishTowerContact(IHitable towerTarget, Vector3 towerPos, float tHalfX, float tHalfZ)
        {
            if (towerTarget == null)
            {
                return;
            }

            int activeCount = CountActiveUnits();
            if (activeCount == 0)
            {
                towerTarget.OnHit(this);
                return;
            }

            float unitRadius = 0.6f; // Dựa trên unitSpacing / 2 
            List<CharacterUnit> hitUnits = new List<CharacterUnit>();

            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null || !unit.IsActive) continue;

                Vector3 unitPos = unit.Position;
                float distX = Mathf.Abs(unitPos.x - towerPos.x);
                float distZ = Mathf.Abs(unitPos.z - towerPos.z);

                if (distX <= (tHalfX + unitRadius) && distZ <= (tHalfZ + unitRadius))
                {
                    hitUnits.Add(unit);
                }
            }

            if (hitUnits.Count == 0)
            {
                return; // Army bounding box chạm nhưng không có unit nào thực sự chạm
            }

            // Nếu số unit đang active <= số unit va chạm => giữ lại 1 unit
            int unitsToKill = Mathf.Min(hitUnits.Count, activeCount - 1);

            for (int i = 0; i < unitsToKill; i++)
            {
                hitUnits[i].RecycleImmediate(true);
            }

            // Đảm bảo tháp nhận sát thương / sự kiện va chạm từ army
            towerTarget.OnHit(this);

            // Nếu đây là unit cuối cùng chạm vào tháp, gọi EndGame
            if (activeCount - unitsToKill <= 1)
            {
                int towerId = towerTarget.GetHashCode();
                int frame = Time.frameCount;
                if (_finishTowerLastHitFrame != frame)
                {
                    _finishTowerLastHitFrame = frame;
                    _finishTowerHitIdsThisFrame.Clear();
                }

                if (_finishTowerHitIdsThisFrame.Add(towerId))
                {
                    if (GameplayManager.Instance != null)
                    {
                        GameplayManager.Instance.EndGame(true);
                    }
                }
            }
        }

        /// <summary>
        /// Đếm số unit đang active trong characterUnits.
        /// </summary>
        private int CountActiveUnits()
        {
            int count = 0;
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit != null && unit.IsActive)
                {
                    count++;
                }
            }
            return count;
        }

        private bool TryGetClosestActiveCharacterUnit(Vector3 enemyPos, out CharacterUnit victim)
        {
            victim = null;

            float bestDistance = float.MaxValue;
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit == null || !unit.IsActive)
                {
                    continue;
                }

                Vector3 delta = unit.Position - enemyPos;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                victim = unit;
            }

            return victim != null;
        }

        private static readonly Queue<UnitAttackSource> _attackSourcePool = new Queue<UnitAttackSource>(64);

        private UnitAttackSource GetUnitAttackSource()
        {
            return _attackSourcePool.Count > 0 ? _attackSourcePool.Dequeue() : new UnitAttackSource(this);
        }

        private static void ReturnUnitAttackSource(UnitAttackSource source)
        {
            if (source != null)
            {
                _attackSourcePool.Enqueue(source);
            }
        }

        private Transform GetBodyRoot()
        {
            return bodyRoot != null ? bodyRoot : transform;
        }

        private sealed class UnitAttackSource : IAttacker
        {
            public event Action<IHitable> OnAttackComplete = delegate { };

            private readonly PlayerArmySystem _owner;
            private Transform _transform;
            private Vector3 _position;
            private Vector2 _size;
            private int _damage;
            private uint _targetMask;

            public UnitAttackSource(PlayerArmySystem owner)
            {
                _owner = owner;
            }

            public void SetupSource(Transform transform, Vector3 position, Vector2 size, int damage, uint targetMask)
            {
                _transform = transform;
                _position = position;
                _size = size;
                _damage = Mathf.Max(1, damage);
                _targetMask = targetMask;
            }

            public Transform Transform => _transform;
            public EntityType EntityType => EntityType.Character;
            public Vector2 Size => _size;
            public int Damage => _damage;
            public uint TargetMask => _targetMask;
            public Vector3 Position => _position;

            public void Initialize()
            {
            }

            public void Dispose()
            {
                ReturnUnitAttackSource(this);
            }

            public void Setup(int damage)
            {
            }

            public void OnAttackSucceed(IHitable target)
            {
                OnAttackComplete?.Invoke(target);
            }
        }
    }
}

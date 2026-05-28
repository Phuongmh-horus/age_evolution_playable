using System;
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
    public enum PlayerArmyState : byte
    {
        Idle,
        Active,
        KnockBack
    }

    public enum PlayerArmyAttackMode : byte
    {
        Melee,
        ForwardRanged,
        ThrownProjectile
    }

    [DisallowMultipleComponent]
    public class PlayerArmySystem : MonoBehaviour, IAttacker
    {
        private const int HoneycombRows = 4;
        private const int HoneycombColumns = 8;
        private const int HardMaxActiveSpawnedUnits = HoneycombRows * HoneycombColumns;
        private const float HoneycombForwardStepFactor = 0.8660254f;
        private static readonly Vector2Int[] HoneycombDirections =
        {
            new Vector2Int(1, 0),
            new Vector2Int(1, -1),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(-1, 1),
            new Vector2Int(0, 1)
        };

        [Header("Movement")]
        [SerializeField] private Transform bodyRoot;
        [SerializeField] private float fallbackForwardSpeed = 6f;
        [SerializeField] private float fallbackSpeedChangeRate = 2f;
        [SerializeField, Min(0f)] private float inputSensitivity = 0.015f;
        [SerializeField, Min(1f)] private float strafeFollowMultiplier = 2f;
        [SerializeField] private float xLimit = 4f;
        [SerializeField] private float collisionCheckRangeX = 7f;
        [SerializeField] private float collisionCheckRangeZ = 22f;
        [SerializeField] private Vector2 collisionSize = new Vector2(3f, 3f);

        [Header("Spawn")]
        [SerializeField] private CharacterListDataSO characterList;
        [SerializeField, Min(1)] private int fallbackCharacterLevel = 1;
        [SerializeField, Min(1)] private int maxActiveSpawnedUnits = 32;
        [SerializeField, Min(0f)] private float unitSpacing = 1.1f;

        [Header("Attack")]
        [SerializeField] private PlayerArmyAttackMode attackMode = PlayerArmyAttackMode.ForwardRanged;
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
        private GameObject _weaponOverridePrefab;
        private int _baseAttackDamage = 1;
        private int _resolvedWeaponDamage;
        private float _baseAttackInterval;
        private float _baseProjectileDuration;
        private int _fireRateBonusPoints;
        private float _baseFireRange;
        private float _fireRangeBonus;

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
        public uint TargetMask => (uint)(1 << (int)EntityType.Item |
                                         1 << (int)EntityType.Enemy |
                                         1 << (int)EntityType.ResourceTower |
                                         1 << (int)EntityType.CapacityFactory |
                                         1 << (int)EntityType.CapacityGate |
                                         1 << (int)EntityType.PowerGate |
                                         1 << (int)EntityType.FinishTrigger |
                                         1 << (int)EntityType.FinishTower |
                                         1 << (int)EntityType.GateNewEra);

        private void Awake()
        {
            ResolveDependencies();
            CacheDefaultState();
            ResetRuntimeSpawnState();
        }

        public void Initialize()
        {
            ResolveDependencies();
            CacheDefaultState();
            ClearSceneUnits();
            ResetRuntimeSpawnState();
        }

        private void Start()
        {
            Initialize();
            currentState = PlayerArmyState.Active;
            SubscribeWeaponChange();
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
            SpawnUnits(fallbackCharacterLevel, 1);
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
            if (prefab == null)
            {
                weaponProjectilePrefab = null;
                return;
            }

            weaponProjectilePrefab = prefab;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveDependencies();
            maxActiveSpawnedUnits = Mathf.Clamp(maxActiveSpawnedUnits, 1, HardMaxActiveSpawnedUnits);
            if (characterUnits == null) characterUnits = new List<CharacterUnit>();
        }
#endif

        private void Update()
        {
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
        }

        public void AddUnit(CharacterUnit unit, bool parentToRoot = true)
        {
            AddUnit(unit, parentToRoot, true);
        }

        public void AddUnit(CharacterUnit unit, bool parentToRoot, bool initialize)
        {
            if (unit == null || characterUnits.Contains(unit))
            {
                return;
            }

            characterUnits.Add(unit);

            if (parentToRoot)
            {
                unit.transform.SetParent(GetBodyRoot(), true);
            }

            if (initialize)
            {
                InitializeRuntimeUnit(unit, ResolveSpawnLevel(unit.Level > 0 ? unit.Level : fallbackCharacterLevel));
            }
            else
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

        public void PlayEffectAt(EffectType effectType, Vector3 position, Quaternion rotation, Action onComplete = null, float waitForAction = 0f)
        {
            effectSystem?.PlayEffectAt(effectType, position, rotation, GetBodyRoot(), onComplete, waitForAction);
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
            attackDamage = Mathf.Max(1, _baseAttackDamage + Mathf.Max(0, _resolvedWeaponDamage));
        }

        private void RefreshFireRange()
        {
            projectileDistance = Mathf.Max(0.1f, _baseFireRange + _fireRangeBonus);
        }

        private int ResolveEffectiveAttackDamage()
        {
            return Mathf.Max(1, attackDamage);
        }

        public void OnUpdate(float dt)
        {
        }

        public void Dispose()
        {
            ClearUnits(true);
        }

        public void SetIdle()
        {
            currentState = PlayerArmyState.Idle;
        }

        public void SetActive()
        {
            currentState = PlayerArmyState.Active;
        }

        public void AddCards(List<CardSpawnRequestData> requests, CardSpawnEffectType effectType)
        {
            if (requests == null || requests.Count == 0)
            {
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

        public void UpgradeAllUnitsToLevel(int levelbonus, bool includeWeapon = true)
        {
            if (levelbonus <= 0 || characterUnits == null || characterUnits.Count == 0)
            {
                return;
            }

            int oldLevel = 1;
            for (int i = 0; i < characterUnits.Count; i++)
            {
                var unit = characterUnits[i];
                if (unit != null && unit.Level > 0)
                {
                    oldLevel = unit.Level;
                    break;
                }
            }

            int targetLevel = Mathf.Max(1, oldLevel + levelbonus);
            fallbackCharacterLevel = targetLevel;
            var snapshot = new List<CharacterUnit>(characterUnits);

            for (int i = 0; i < snapshot.Count; i++)
            {
                var oldUnit = snapshot[i];
                if (oldUnit == null)
                {
                    continue;
                }

                int oldUnitId = oldUnit.GetInstanceID();
                if (!_nextAttackTimes.TryGetValue(oldUnitId, out float nextAttackTime))
                {
                    nextAttackTime = Time.time + attackInterval;
                }

                Vector3 position = oldUnit.transform.position;
                Quaternion rotation = oldUnit.transform.rotation;
                var newUnit = CreateRuntimeCharacterUnit(targetLevel, position, rotation, nextAttackTime, true);
                if (newUnit == null)
                {
                    continue;
                }

                if (!RemoveUnit(oldUnit, true))
                {
                    newUnit.RecycleImmediate(false);
                    continue;
                }

                int insertIndex = Mathf.Clamp(i, 0, characterUnits.Count);
                characterUnits.Insert(insertIndex, newUnit);

                if (!includeWeapon)
                {
                    newUnit.Setup(targetLevel, false);
                }
            }
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
            var snapshot = new List<CharacterUnit>(characterUnits);

            for (int i = 0; i < snapshot.Count; i++)
            {
                var unit = snapshot[i];
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

            if (GameplayManager.Instance != null)
            {
                var era = GameplayManager.Instance.PlayableEra;
                if (era != null)
                {
                    characterList = era.CharacterList;
                }
            }

            return characterList;
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

            var positions = new List<Vector2Int>(ring * 6);
            Vector2Int axial = Vector2Int.zero;

            for (int i = 0; i < ring; i++)
            {
                axial += HoneycombDirections[4];
            }

            for (int side = 0; side < 6; side++)
            {
                for (int i = 0; i < ring; i++)
                {
                    positions.Add(axial);
                    axial += HoneycombDirections[side];
                }
            }

            positions.Sort((a, b) =>
            {
                float aZ = Mathf.Abs(a.y);
                float bZ = Mathf.Abs(b.y);
                if (!Mathf.Approximately(aZ, bZ))
                {
                    return aZ.CompareTo(bZ);
                }

                if (a.y != b.y)
                {
                    return a.y.CompareTo(b.y);
                }

                float aX = a.x + a.y * 0.5f;
                float bX = b.x + b.y * 0.5f;
                if (!Mathf.Approximately(aX, bX))
                {
                    return aX.CompareTo(bX);
                }

                return a.x.CompareTo(b.x);
            });

            return positions[Mathf.Clamp(step, 0, positions.Count - 1)];
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
            float effectiveSmoothness = Mathf.Clamp01(baseSmoothness * Mathf.Max(1f, strafeFollowMultiplier));
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
                float distX = Mathf.Abs(tPos.x - myPos.x);
                float distZ = Mathf.Abs(tPos.z - myPos.z);
                if (distX > preCullX || distZ > preCullZ)
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
            var attackSource = new UnitAttackSource(unit.transform, origin, attackWindow, effectiveDamage, TargetMask);
            attackSource.OnAttackSucceed(targetInfo.Target);
            targetInfo.Target.OnHit(attackSource);
            OnAttackComplete?.Invoke(targetInfo.Target);

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

            unit.PlayAnimation(AnimationType.Attack, 0.4f, () =>
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
                unit.PlayAttackEffect();
                unit.HideWeapon();
            });

            return true;
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
                if (forwardDistance < 0f || forwardDistance > range)
                {
                    continue;
                }

                float lateralDistance = Mathf.Abs(Vector3.Dot(delta, right));
                float targetHalfWidth = Mathf.Max(Mathf.Abs(colData.Size.x), Mathf.Abs(colData.Size.z));
                if (lateralDistance > halfWidth + targetHalfWidth)
                {
                    continue;
                }

                if (forwardDistance >= bestForward)
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
            if (unit == null)
            {
                return;
            }

            if (unit.transform.parent != GetBodyRoot())
            {
                unit.transform.SetParent(GetBodyRoot(), true);
            }

            unit.Initialize(level, true);
            unit.Setup(level, true);

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
                unit.ShowWeapon();
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
            int cap = Mathf.Clamp(maxActiveSpawnedUnits, 1, HardMaxActiveSpawnedUnits);
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
                   entityType == EntityType.FinishTower ||
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

        private Transform GetBodyRoot()
        {
            return bodyRoot != null ? bodyRoot : transform;
        }

        private sealed class UnitAttackSource : IAttacker
        {
            public event Action<IHitable> OnAttackComplete = delegate { };

            private readonly Transform _transform;
            private readonly Vector3 _position;
            private readonly Vector2 _size;
            private readonly int _damage;
            private readonly uint _targetMask;

            public UnitAttackSource(Transform transform, Vector3 position, Vector2 size, int damage, uint targetMask)
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


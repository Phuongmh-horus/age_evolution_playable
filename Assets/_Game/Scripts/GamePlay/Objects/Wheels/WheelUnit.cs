using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.Entities;
using GamePlay.Items;
using GamePlay.Characters;
using GamePlay.CollisionSystems; // FIX: Added
using GamePlay.Inputs;
using GamePlay.ComponentSystems; // FIX: Added
using Pools;
using UnityEngine;
using DG.Tweening;
 // Assuming this exists, or use standard Unity

namespace GamePlay.Crushers
{
    public enum WheelState : byte
    {
        Idle,
        Active,
        SpawningCard,
        KnockBack
    }

    public enum CardSpawnEffectType
    {
        None,
        Drop,
        FlyIn,
        DropWithoutAction,
    }

    [Serializable]
    public struct CardSpawnRequestData
    {
        // Playable keeps character-card flow, but supports the newer card request shape
        // used by the source project (Id + CardType) so imported data/scripts still compile.
        public int Id;
        public int Level;
        public int Amount;
        public CardType CardType;

        public CardSpawnRequestData(int level, int amount)
        {
            Id = level;
            Level = level;
            Amount = amount;
            CardType = GamePlay.Crushers.CardType.Character;
        }

        public CardSpawnRequestData(int level, int amount, CardType cardType)
        {
            Id = level;
            Level = level;
            Amount = amount;
            CardType = cardType;
        }

        public CardSpawnRequestData(int id, int level, int amount, CardType cardType)
        {
            Id = id;
            Level = level;
            Amount = amount;
            CardType = cardType;
        }
    }

    [Serializable]
    internal struct NormalizedCardSpawnRequest
    {
        public int Id;
        public int Level;
        public int Amount;
        public CardType CardType;

        public bool IsCharacter =>
            CardType == CardType.Character || CardType == CardType.Solider;
    }

    [Serializable]
    internal struct WheelCardRuntimeData
    {
        public int Id;
        public int Level;
        public CardType CardType;

        public WheelCardRuntimeData(int id, int level, CardType cardType)
        {
            Id = id;
            Level = level;
            CardType = cardType;
        }

        public bool IsCharacter =>
            CardType == CardType.Character || CardType == CardType.Solider;
    }

    public class WheelUnit : PoolEntity, GamePlay.ComponentSystems.IHitable, GamePlay.ComponentSystems.IAttacker
    {
        private static readonly int SlotBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SlotColorId = Shader.PropertyToID("_Color");
        private static readonly int SlotEmissionColorId = Shader.PropertyToID("_EmissionColor");

        private struct SlotOutlineVisualState
        {
            public int ColorPropertyId;
            public bool HasEmission;
            public Color BaseColor;
        }

        public event Action<WheelState> OnStateChanged = delegate { };
        
        // IHitable Implementation
        public event Action<GamePlay.ComponentSystems.IAttacker> OnHitComplete;
        
        // IAttacker Implementation
        public event Action<GamePlay.ComponentSystems.IHitable> OnAttackComplete;
        // Wheel is an interaction trigger, not a combat attacker. Characters/projectiles deal damage.
        public int Damage => 0;
        public Vector2 Size
        {
            get
            {
                if (hitComponent != null)
                {
                    var col = hitComponent.GetColliderData();
                    switch (col.Type)
                    {
                        case GamePlay.ComponentSystems.ShapeType.Box:
                            return new Vector2(Mathf.Abs(col.Size.x) * 2f, Mathf.Abs(col.Size.z) * 2f);
                        case GamePlay.ComponentSystems.ShapeType.Sphere:
                        case GamePlay.ComponentSystems.ShapeType.Cylinder:
                        default:
                            float r = Mathf.Max(Mathf.Abs(col.Size.x), Mathf.Abs(col.Size.z));
                            return new Vector2(r * 2f, r * 2f);
                    }
                }

                return new Vector2(3f, 3f);
            }
        }
        public uint TargetMask => (uint)(1 << (int)EntityType.Item |
                                         1 << (int)EntityType.Enemy |
                                         1 << (int)EntityType.ResourceTower |
                                         1 << (int)EntityType.CapacityFactory |
                                         1 << (int)EntityType.CapacityGate |
                                         1 << (int)EntityType.PowerGate |
                                         1 << (int)EntityType.FinishTrigger |
                                         1 << (int)EntityType.FinishTower |
                                         1 << (int)EntityType.GateNewEra);

        public void OnAttackSucceed(GamePlay.ComponentSystems.IHitable target)
        {
             OnAttackComplete?.Invoke(target);
        }

        public void Setup(int damage) { } // No-op for Wheel

        public bool IsActive => currentState == WheelState.Active || currentState == WheelState.SpawningCard || currentState == WheelState.Idle; 
        
        // [FIX] Removed shadowing property. Use base _entityType.
        // public new EntityType EntityType => EntityType.Wheel;

        public void Initialize()
        {
            // [FIX] Ensure EntityType is correct for BaseComponent/HitComponent access
            _entityType = EntityType.Wheel;

            ResetLogicData();
            EnsureInitialized();
            
            // [FIX] Initialize Components and Register to Projectile System
            InitializeComponents();
        }

        private void ResetLogicData()
        {
            _currentSlotIndex = 0;
            _angleAccumulator = 0f;
            _totalRotation = 0f;
            _rotationSynced = false;
            _warnedUnsupportedCardType = false;
            _lastEnemyHitFrame = -1;
            _currentEnemyContactIds.Clear();
            _previousEnemyContactIds.Clear();
            ClearActiveSpawnedUnits();
            ClearCardsOnly();
            _queuedRequests.Clear();
        }

        private void ClearCardsOnly()
        {
            _cachedTotalCards = 0;

            if (_slotsMap != null)
            {
                foreach (var list in _slotsMap) list.Clear();
            }
            if (_visualSlotsMap != null)
            {
                foreach (var list in _visualSlotsMap) list.Clear();
            }

            foreach (var c in _cardsMap)
            {
                if (c != null) Destroy(c.gameObject);
            }

            _cardsMap.Clear();
            _runtimeCards.Clear();
            SetupVisualAnchors();
            DisableAllSlotOutlines();
        }

        private void InitializeComponents()
        {
             if (hitComponent != null) hitComponent.Initialize();
             if (attackComponent != null) attackComponent.Initialize();

             // [FIX] Don't register here - Start() already registers 'this' (WheelUnit)
             // which has GetColliderData() that delegates to hitComponent
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        public GamePlay.ComponentSystems.ColliderData GetColliderData()
        {
            // [FIX] Use Cylinder for better vertical coverage (catches high arcs)
            // Radius 1.2 (reasonable hit area), HalfHeight 2.0 (Total 4m height for arcing weapons)
            return new GamePlay.ComponentSystems.ColliderData
            {
                Type = GamePlay.ComponentSystems.ShapeType.Cylinder,
                Size = new Vector3(1.2f, 2.0f, 1.2f),
                Offset = 0f,
                CategoryBits = (uint)(1 << (int)EntityType.Wheel)
            };
        }

        public void OnHit(GamePlay.ComponentSystems.IAttacker source)
        {
             // Take Damage Logic?
             // Usually Wheel has Health component or we just knockback
             // For now, trigger knockback + card loss
             ApplyEnemyHit(applyKnockback: true);
             
             OnHitComplete?.Invoke(source);
        }

        [Header("Hierarchy References")]
        public Transform fullBody;
        public Transform visualModel;
        public Transform unitSpawnPoint;

        [Header("Hit Settings")]
        [SerializeField] private GamePlay.ComponentSystems.HitComponent hitComponent;
        [Header("Attack Settings")]
        [SerializeField] private GamePlay.ComponentSystems.AttackComponent attackComponent;

        [Header("Arrow - Anchor")]
        [SerializeField] protected Transform arrowModel;
        [SerializeField] protected List<GameObject> anchorObjects = new List<GameObject>();

        [Header("Variable")]
        [SerializeField] private WheelVariable variable;
        
        [Header("Config Fallback")]
        [SerializeField] private CharacterListDataSO overrideCharacterList;

        [Header("Pre-baked Data")]
        public List<Transform> preBakedSlots = new List<Transform>();
        public List<MeshRenderer> outlineSlots = new List<MeshRenderer>();

        [Header("Wheel Animation")]
        [SerializeField] private List<Animator> wheelAnimators = new List<Animator>();
        [SerializeField] private string wheelActiveTrigger = "Active";

        [Header("Playable Movement")]
        [SerializeField] private float fallbackForwardSpeed = 6f;
        [SerializeField] private float fallbackSpeedChangeRate = 2f;

        [Header("Input Settings")]
        [Tooltip("Độ nhạy vuốt ngang theo cơ chế delta (không phải chạm đâu tới đó).")]
        [SerializeField] private float inputSensitivity = 0.015f;
        [Tooltip("Tăng tốc độ wheel bám theo target X để giảm cảm giác trễ tay.")]
        [SerializeField, Min(1f)] private float strafeFollowMultiplier = 2f;
        [Header("Collision Culling")]
        [SerializeField] private float collisionCheckRangeX = 7f;
        [SerializeField] private float collisionCheckRangeZ = 22f;

        [Header("Runtime Spawn Cap")]
        [SerializeField, Min(1)] private int maxActiveSpawnedUnits = 220;
        [SerializeField] private bool despawnOldestWhenOverCap = true;
        private const int HardMaxActiveSpawnedUnits = 96;

        [Header("Trigger SFX")]
        [SerializeField] private AudioClipName triggerSfx = AudioClipName.None;
        [SerializeField] private AudioClipName addCardSfx = AudioClipName.SFX_DropCard;
        
        [Header("SFX Fallback (Playable)")]
        [SerializeField] private AudioSource sfxSource;
        private static AudioClip _cachedDropCardClip;
        private bool _rotationSynced;
        private bool _warnedUnsupportedCardType;

        [Header("Arrow Kick Settings")]
        [SerializeField] private float arrowKickForce = 12f;
        [SerializeField] private float arrowMaxAngle = 20f;
        [SerializeField] private float kickDuration = 0.08f;
        [SerializeField] private float returnDuration = 0.12f;

        [Header("Playable Customization")]
        [SerializeField] private bool useCustomSettings = false;
        [SerializeField] private int customTotalSlots = 8;
        [SerializeField] private float customForwardSpeed = 6f;
        [SerializeField] private float customTurnDuration = 1.5f;
        [SerializeField] private float customRadius = 1.55f;
        [SerializeField] private float customLayerHeight = 0.5f;
        [SerializeField] private float customUnitHorizontalSpace = 0.8f;
        [SerializeField] private float customXLimit = 4f;

        // Properties to switch between Custom and SO
        public int TotalSlots => useCustomSettings ? customTotalSlots : (variable != null ? variable.TotalSlots : 8);
        public float ForwardSpeedVal => useCustomSettings ? customForwardSpeed : (variable != null ? variable.ForwardSpeed : 6f);
        public float TurnDuration => useCustomSettings ? customTurnDuration : (variable != null ? variable.TurnDuration : 1.5f);
        public float Radius => useCustomSettings ? customRadius : (variable != null ? variable.Radius : 1.55f);
        public float LayerHeight => useCustomSettings ? customLayerHeight : (variable != null ? variable.LayerHeight : 0.5f);
        public float UnitHorizontalSpace => useCustomSettings ? customUnitHorizontalSpace : (variable != null ? variable.UnitHorizontalSpace : 0.8f);
        public float XLimit => useCustomSettings ? customXLimit : (variable != null ? variable.XLimit : 4f);

        // State
        [SerializeField] private WheelState currentState = WheelState.Idle;

        // Public getters
        public Transform Transform => transform;
        // [FIX] Use fullBody position since that's where the wheel actually moves (strafing)
        public Vector3 Position => fullBody != null ? fullBody.position : transform.position;

        // Visual / Cards Logic
        private List<WheelCardRuntimeData>[] _slotsMap;
        private List<CardUnit>[] _visualSlotsMap;
        private List<CardUnit> _cardsMap = new List<CardUnit>();
        private List<WheelCardRuntimeData> _runtimeCards = new List<WheelCardRuntimeData>();
        private readonly Queue<CharacterUnit> _activeSpawnedUnits = new Queue<CharacterUnit>();
        private int _cachedTotalCards = 0;
        private List<CardSpawnRequestData> _queuedRequests = new List<CardSpawnRequestData>();
        private readonly HashSet<int> _currentEnemyContactIds = new HashSet<int>();
        private readonly HashSet<int> _previousEnemyContactIds = new HashSet<int>();
        private int _lastEnemyHitFrame = -1;
        private bool _wheelEventsRegistered;
        private bool _outlineSlotsUseFallbackTint;
        private readonly Dictionary<MeshRenderer, SlotOutlineVisualState> _slotOutlineVisualStates = new Dictionary<MeshRenderer, SlotOutlineVisualState>();
        private MaterialPropertyBlock _slotOutlineMpb;
        
        // Movement
        private float _targetX = 0f;
        private Vector3 _lastMousePos;
        private bool _isDragging = false;
        private float _currentForwardSpeed;
        private float _knockbackTimer;
        private Vector3 _knockbackStartPos;
        private Vector3 _knockbackTargetPos;
        private InputManager _cachedInputManager;
        
        // Formatting
        private float _totalRotation = 0f;
        private int _currentSlotIndex = 0;
        private float _anglePerSlot;
        private float _angleAccumulator;
        [Header("Spawn Timing")]
        [SerializeField] private float spawnLeadAngle = 6f;
        private Quaternion _arrowInitialRotation;
        private float _currentDeflection;
        private Coroutine _arrowKickRoutine;
        private float _cachedCardDelaySeconds = -1f;
        private WaitForSeconds _cachedCardDelayWait;

        // Dependencies
        private CharacterListDataSO _characterList;

        protected override void Awake()
        {
            base.Awake();
            _entityType = EntityType.Wheel;
            maxActiveSpawnedUnits = Mathf.Clamp(maxActiveSpawnedUnits, 1, HardMaxActiveSpawnedUnits);

            if (fullBody == null) fullBody = transform;

            if (arrowModel != null)
                _arrowInitialRotation = arrowModel.localRotation;
        }

        private void Start()
        {
             _entityType = EntityType.Wheel;
             EnsureInitialized();
             _cachedInputManager = InputManager.Instance;
             _currentForwardSpeed = GetForwardSpeed();
             EnsureWheelAnimatorsResolved();
             if (currentState == WheelState.Active)
             {
                 TriggerWheelActiveAnimation();
             }
             EnsureOutlineSlotsResolved();
             SetupVisualAnchors(); // [FIX] Ensure anchors are hidden by default (Prefab often has them enabled)
             DisableAllSlotOutlines();
             if (fullBody) _targetX = fullBody.localPosition.x;
             RegisterWheelEvents(true);

             // --- Fix Collision: Force Rigidbody + Collider ---
             // [FIX] REMOVED Layer 0 override to allow EnemyProjectileSystem to detect Player Layer correctly.
             // gameObject.layer = 0;

             var rb = GetComponent<Rigidbody>();
             if (rb == null)
             {
                 rb = gameObject.AddComponent<Rigidbody>();
                 rb.isKinematic = true;
                 rb.useGravity = false;
             }

             var col = GetComponent<Collider>();
             if (col == null)
             {
                 var sphere = gameObject.AddComponent<SphereCollider>();
                 sphere.isTrigger = true;
                 sphere.radius = 1.5f; // [FIX] Reduced from 6f to 1.5f for fair collision
                 sphere.center = Vector3.zero;
             }
             else if (!col.isTrigger)
             {
                  // Ensure it is a trigger if we move manually via Translate
                  col.isTrigger = true;
             }
             // ------------------------------------------------

             // Register as Player for Enemy Projectiles (use this, not hitComponent)
             GamePlay.CombatSystems.EnemyProjectileSystem.RegisterPlayer(this);
        }

        private void OnDisable()
        {
            RegisterWheelEvents(false);
            _currentEnemyContactIds.Clear();
            _previousEnemyContactIds.Clear();
            _lastEnemyHitFrame = -1;
            if (Application.isPlaying && GamePlay.CombatSystems.EnemyProjectileSystem.Instance != null)
            {
                GamePlay.CombatSystems.EnemyProjectileSystem.UnregisterPlayer();
            }

            ClearActiveSpawnedUnits();
        }

        private void RegisterWheelEvents(bool register)
        {
            if (register)
            {
                if (_wheelEventsRegistered) return;
                GameEventBus.OnAddWheelCard += HandleAddWheelCardEvent;
                GameEventBus.OnBoostWheelCard += HandleBoostWheelCardLevelUpOnly;
                _wheelEventsRegistered = true;
                return;
            }

            if (!_wheelEventsRegistered) return;
            GameEventBus.OnAddWheelCard -= HandleAddWheelCardEvent;
            GameEventBus.OnBoostWheelCard -= HandleBoostWheelCardLevelUpOnly;
            _wheelEventsRegistered = false;
        }

        private void HandleAddWheelCardEvent()
        {
            if (!TryGetPlayerWheelData(out var wheelData)) return;

            AddCards(new List<CardSpawnRequestData>
            {
                new CardSpawnRequestData(wheelData.CardLevel, 1, CardType.Character)
            }, CardSpawnEffectType.Drop);
        }

        private void HandleBoostWheelCardLevelUpOnly()
        {
            if (!TryGetPlayerWheelData(out var wheelData)) return;
            RebuildCharacterCards(wheelData.CardLevel);
        }

        private void RebuildCharacterCards(int newCharacterLevel)
        {
            if (_runtimeCards == null || _runtimeCards.Count == 0) return;

            int safeLevel = Mathf.Max(1, newCharacterLevel);
            var snapshot = new List<WheelCardRuntimeData>(_runtimeCards.Count);
            for (int i = 0; i < _runtimeCards.Count; i++)
            {
                var card = _runtimeCards[i];
                if (card.IsCharacter)
                    card.Level = safeLevel;
                snapshot.Add(card);
            }

            ClearCardsOnly();

            for (int i = 0; i < snapshot.Count; i++)
            {
                SpawnSingleCard(snapshot[i], CardSpawnEffectType.None);
            }
        }

        private bool TryGetPlayerWheelData(out WheelData wheelData)
        {
            wheelData = null;
            if (DataManager.PlayerData == null) return false;
            wheelData = DataManager.PlayerData.WheelData;
            return wheelData != null;
        }

        private void EnsureInitialized()
        {
             // In Luna/playable builds, the scene may NOT contain ConfigHolder.
             // Prefer resolving from GameplayManager (playableEra / gamePlayVariable), then fall back to ConfigHolder.
             if (variable == null)
             {
                 if (GameplayManager.Instance != null && GameplayManager.Instance.gamePlayVariable != null)
                 {
                     variable = GameplayManager.Instance.gamePlayVariable.WheelVariable;
                 }

                 if (variable == null && ConfigHolder.Instance != null && ConfigHolder.Instance.GamePlayVariable != null)
                 {
                     variable = ConfigHolder.Instance.GamePlayVariable.WheelVariable;
                 }
             }

             if (_characterList == null)
             {
                 // 1) Playable flow: GameplayManager provides EraDataSO directly (no DataManager/ConfigHolder).
                 if (GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
                 {
                     _characterList = GameplayManager.Instance.PlayableEra.CharacterList;
                 }

                 // 2) Full flow: resolve via ConfigHolder (if present)
                 if (_characterList == null && ConfigHolder.Instance != null)
                 {
                     var era = ConfigHolder.Instance.GetCurrentEraConfig();
                     if (era != null) _characterList = era.CharacterList;
                 }
                 
                 // Fallback
                 if (_characterList == null) _characterList = overrideCharacterList;
             }

             if ((_slotsMap != null && _slotsMap.Length != TotalSlots) ||
                 (_visualSlotsMap != null && _visualSlotsMap.Length != TotalSlots))
             {
                 // Size changed (e.g. Inspector override toggled)
                 ResetLogicData(); // This clears _slotsMap
             }

             if (_slotsMap == null || _slotsMap.Length != TotalSlots ||
                 _visualSlotsMap == null || _visualSlotsMap.Length != TotalSlots)
             {
                 int slots = TotalSlots;
                 _slotsMap = new List<WheelCardRuntimeData>[slots];
                 _visualSlotsMap = new List<CardUnit>[slots];
                 for (int i = 0; i < slots; i++)
                 {
                     _slotsMap[i] = new List<WheelCardRuntimeData>();
                     _visualSlotsMap[i] = new List<CardUnit>();
                 }
                 _anglePerSlot = 360f / slots;
                 DisableAllSlotOutlines();
             }
        }

        private void Update()
        {
            if (currentState == WheelState.Idle) return;
            if (!GameplayManager.IsGameStarted) return;

            float dt = Time.deltaTime;

            // Match reference movement flow:
            // - KnockBack uses timed lerp to a target position.
            // - Active/Spawning use smoothed forward speed and smoothed strafe.
            if (currentState == WheelState.KnockBack)
            {
                _knockbackTimer += dt;
                float duration = variable != null ? Mathf.Max(variable.KnockbackDuration, 0.01f) : 0.3f;
                float t = Mathf.Clamp01(_knockbackTimer / duration);
                transform.position = Vector3.Lerp(_knockbackStartPos, _knockbackTargetPos, t);

                if (_knockbackTimer >= duration)
                {
                    _knockbackTimer = 0f;
                    SetState(WheelState.Active);
                }
            }
            else
            {
                float targetSpeed = GetForwardSpeed();
                if (currentState == WheelState.SpawningCard && variable != null)
                    targetSpeed *= variable.SlowMotionSpeedMultiplier;

                float speedChangeRate = Mathf.Max(0.01f, fallbackSpeedChangeRate);

                _currentForwardSpeed = Mathf.Lerp(_currentForwardSpeed, targetSpeed, dt * speedChangeRate);
                transform.position += transform.forward * (_currentForwardSpeed * dt);

                if (_cachedInputManager == null) _cachedInputManager = InputManager.Instance;

                float inputDelta = _cachedInputManager != null ? _cachedInputManager.GetMoveDelta() : 0f;
                float inputGain = Mathf.Clamp(inputSensitivity * 100f, 0.5f, 3f);
                float scaledInputDelta = inputDelta * inputGain;
                float strafeMult = variable != null ? variable.StrafeMultiplier : 0.15f;
                float tempTargetX = _targetX + (scaledInputDelta * strafeMult);
                tempTargetX = Mathf.Clamp(tempTargetX, -XLimit, XLimit);

                Vector3 localPos = fullBody.localPosition;
                float baseSmoothness = variable != null ? variable.MoveSmoothness : 0.15f;
                float effectiveSmoothness = Mathf.Clamp01(baseSmoothness * Mathf.Max(1f, strafeFollowMultiplier));
                float newX = Mathf.Lerp(localPos.x, tempTargetX, effectiveSmoothness);
                fullBody.localPosition = new Vector3(newX, localPos.y, localPos.z);
                _targetX = tempTargetX;
            }

            // 4. Rotate Visual
            // 4. Rotate Visual
            HandleRotation(dt, _currentForwardSpeed);

            // --- REFACTORED COLLISION LOGIC (CollisionSystem - No Unity Physics) ---
            var collisionSystem = CollisionSystem.Instance;
            if (collisionSystem != null && collisionSystem.Count > 0)
            {
                _currentEnemyContactIds.Clear();
                Vector3 myPos = fullBody != null ? fullBody.position : transform.position;
                Vector2 mySize = Size; // 3x3
                uint myMask = TargetMask; // Items, Enemies, etc.
                // Check collision with all targets
                int count = collisionSystem.Count;
                float myHalfX = mySize.x * 0.5f;
                float myHalfZ = mySize.y * 0.5f;
                float preCullX = Mathf.Max(myHalfX + 1f, collisionCheckRangeX);
                float preCullZ = Mathf.Max(myHalfZ + 1f, collisionCheckRangeZ);

                for (int i = 0; i < count; i++)
                {
                    var target = collisionSystem.GetTargetBySortedIndex(i);
                    if (target == null) continue;
                    
                    if (!target.IsActive) {
                        // if (Time.frameCount % 300 == 0) Debug.Log($"[WheelDebug] Target {i} inactive.");
                        continue;
                    }
                    if (ReferenceEquals(target, this)) continue; 

                    // 2. AABB Check
                    var targetTr = collisionSystem.GetTransform(i);
                    if (targetTr == null) continue;

                    Vector3 tPos = targetTr.position;
                    float distX = Mathf.Abs(tPos.x - myPos.x);
                    float distZ = Mathf.Abs(tPos.z - myPos.z);
                    if (distX > preCullX || distZ > preCullZ) continue;

                    var colData = collisionSystem.GetColliderData(i);
                    uint categoryBits = colData.CategoryBits != 0
                        ? colData.CategoryBits
                        : (uint)(1 << (int)target.EntityType);
                    if ((myMask & categoryBits) == 0) continue;

                    // Target AABB halves (Size is Half-Extents)
                    float tHalfX = Mathf.Abs(colData.Size.x);
                    float tHalfZ = Mathf.Abs(colData.Size.z); 

                    if (colData.Type != GamePlay.ComponentSystems.ShapeType.Box) 
                    {
                        tHalfZ = Mathf.Max(tHalfX, tHalfZ); 
                        tHalfX = tHalfZ; 
                    }

                    bool hitX = distX <= (myHalfX + tHalfX);
                    bool hitZ = distZ <= (myHalfZ + tHalfZ);

                    if (hitX && hitZ)
                    {
                         // HIT!
                         // Visual Debug: Green Line
                         #if UNITY_EDITOR
                         Debug.DrawLine(myPos, tPos, Color.green, 1.0f);
                         #endif

                        if (target.EntityType == GamePlay.Entities.EntityType.Enemy)
                        {
                            int enemyInstanceId = targetTr.GetInstanceID();
                            _currentEnemyContactIds.Add(enemyInstanceId);

                            // Only count one card loss when entering enemy contact, not every frame while overlapping.
                            if (!_previousEnemyContactIds.Contains(enemyInstanceId))
                            {
                                HandleCollisionWithEnemy();
                                // Gameplay rule: wheel contact crushes enemy immediately.
                                target.OnHit(this);
                            }
                        }
                        else if (target.EntityType == GamePlay.Entities.EntityType.CapacityFactory || 
                                 target.EntityType == GamePlay.Entities.EntityType.CapacityGate || 
                                 target.EntityType == GamePlay.Entities.EntityType.ResourceTower || 
                                 target.EntityType == GamePlay.Entities.EntityType.PowerGate ||
                                 target.EntityType == GamePlay.Entities.EntityType.Item ||
                                 target.EntityType == GamePlay.Entities.EntityType.FinishTrigger ||
                                 target.EntityType == GamePlay.Entities.EntityType.FinishTower ||
                                 target.EntityType == GamePlay.Entities.EntityType.GateNewEra)
                        {
                            target.OnHit(this);
                        }
                    }
                    else if (distX < 5f && distZ < 5f) 
                    {
                        // Near miss: Yellow Line (Only draw if close)
                        #if UNITY_EDITOR
                        Debug.DrawLine(myPos, tPos, Color.yellow, 0.1f);
                        #endif
                    }
                }

                _previousEnemyContactIds.Clear();
                _previousEnemyContactIds.UnionWith(_currentEnemyContactIds);
            }
            else
            {
                _previousEnemyContactIds.Clear();
                _currentEnemyContactIds.Clear();
            }
            
            // Heartbeat (Optional, reduced freq)
            // -----------------------------
        }

        private void HandleInput()
        {
            if (currentState != WheelState.Active && currentState != WheelState.SpawningCard) return;

            bool isTouching = Input.touchCount > 0;
            if (isTouching)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    _isDragging = false;
                }
                else
                {
                    // Luna: allow drag immediately on first touch (no extra tap required)
                    _isDragging = true;
                }
            }
            else
            {
                if (Input.GetMouseButton(0))
                    _isDragging = true;
                else if (Input.GetMouseButtonUp(0))
                    _isDragging = false;
            }

            if (_isDragging)
            {
                // Map vị trí ngón tay trực tiếp sang vị trí wheel (1:1 mapping)
                // Screen X: 0 -> Width  maps to  Wheel X: -XLimit -> +XLimit
                float screenX = isTouching ? Input.GetTouch(0).position.x : Input.mousePosition.x;
                float screenWidth = Screen.width > 0 ? Screen.width : 1920f;
                float normalizedX = screenX / screenWidth; // 0 -> 1
                float mappedX = (normalizedX - 0.5f) * 2f * XLimit; // -XLimit -> +XLimit
                _targetX = mappedX;
            }
        }
        
        private void HandleRotation(float dt, float currentSpeed)
        {
             if (visualModel == null) return;
             
             // [FIX] Fire Rate / Rotation Scaling
             // User Request: "Scale with Wheel Rotation Speed, NOT Character Speed"
             // Interpretation: Implementing Rolling Physics. 
             // RotationSpeed (Angular) = LinearSpeed / Radius.
             // This ensures that as the Wheel moves faster, it spins faster, and thus spawns units faster.
             // We ignore 'TurnDuration' if it causes constant speed.
             
             float rotateSpeed = 0f;
             // [REVERT] Logic Game Gốc: Dùng TurnDuration cố định
             // User yêu cầu "Check game gốc", và game gốc không có Rolling Physics tính theo Radius.
             // Nếu cần Scaling, ta sẽ Scale TurnDuration ở GamePlayVariable, không hardcode logic vật lý ở đây.
             
             if (TurnDuration > 0)
                rotateSpeed = 360f / TurnDuration;
             else
                rotateSpeed = currentSpeed * 30f;

             // Rotate logic
             float deltaAngle = rotateSpeed * dt;
             _totalRotation += deltaAngle;
             visualModel.localRotation = Quaternion.Euler(0, _totalRotation, 0);

             // Slot accumulation for firing
             _angleAccumulator += deltaAngle;
             float triggerAngle = Mathf.Clamp(_anglePerSlot - Mathf.Abs(spawnLeadAngle), 0.1f, _anglePerSlot);
             while (_angleAccumulator >= triggerAngle)
             {
                 _angleAccumulator -= _anglePerSlot;

                 // Match original flow: trigger current slot, then advance
                 SpawnFromSlot(_currentSlotIndex);

                 _currentSlotIndex++;
                 int currentSlots = _slotsMap != null ? _slotsMap.Length : TotalSlots;
                 _currentSlotIndex %= currentSlots;
             }
        }
        
        // --- Collision Logic (Trigger instead of Jobs) ---
        private void OnTriggerEnter(Collider other)
        {
            // [FIX] Disable Unity Physics Trigger as requested by User to restore Luna functionality.
            // When this was active (filtered or not), it seemingly conflicted with Luna's detection logic.
            return; 

            /*
            if (currentState == WheelState.Idle) return;
            // ... (rest of logic commented out)

            // Search for IHitable in object or parents (robustness)
            var hitable = other.GetComponentInParent<GamePlay.ComponentSystems.IHitable>();
            
            if (hitable == null) {
               return;
            }

            if (hitable != null && hitable.IsActive)
            {
                // Avoid self-collision just in case
                if (ReferenceEquals(hitable, this)) return;

                if (hitable.EntityType == GamePlay.Entities.EntityType.Enemy)
                {
                    HandleCollisionWithEnemy();
                    hitable.OnHit(this); 
                }
                else if (hitable.EntityType == GamePlay.Entities.EntityType.CapacityFactory || 
                         hitable.EntityType == GamePlay.Entities.EntityType.CapacityGate || 
                         hitable.EntityType == GamePlay.Entities.EntityType.ResourceTower || 
                         hitable.EntityType == GamePlay.Entities.EntityType.PowerGate ||
                         hitable.EntityType == GamePlay.Entities.EntityType.Item ||
                         // [FIX] Ensure other entities are hit (Finish/NewEra)
                         hitable.EntityType == GamePlay.Entities.EntityType.FinishTower ||
                         hitable.EntityType == GamePlay.Entities.EntityType.GateNewEra)
                {
                    // Interact with Environment
                    hitable.OnHit(this);
                }
            }
            */
        }
            

        
        private void HandleCollisionWithEnemy()
        {
             ApplyEnemyHit(applyKnockback: true);
        }

        private void ApplyEnemyHit(bool applyKnockback)
        {
            if (_lastEnemyHitFrame == Time.frameCount) return;
            _lastEnemyHitFrame = Time.frameCount;

            if (applyKnockback && currentState != WheelState.KnockBack)
            {
                SetState(WheelState.KnockBack);
                _knockbackTimer = 0f;
                _knockbackStartPos = transform.position;
                float knockbackDistance = variable != null ? variable.KnockbackDistance : 1f;
                _knockbackTargetPos = transform.position - transform.forward * knockbackDistance;
            }

            // Always remove a card on enemy hit (including projectiles)
            RemoveCard(1);
        }

        // --- Card Logic ---

        public void AddCards(List<CardSpawnRequestData> requests, CardSpawnEffectType effectType)
        {
            if (requests == null || requests.Count == 0)
            {
                Debug.LogWarning($"[WheelUnit] AddCards ABORTED: requests is null or empty!");
                return;
            }
            // Copy to avoid external modifications while iterating in coroutine.
            var snapshot = new List<CardSpawnRequestData>(requests);
            _queuedRequests.Clear();
            _queuedRequests.AddRange(snapshot);
            StartCoroutine(CoSpawnCards(snapshot, effectType));
        }
        
        private IEnumerator CoSpawnCards(List<CardSpawnRequestData> requests, CardSpawnEffectType effectType)
        {
            bool spawnInstant = effectType == CardSpawnEffectType.DropWithoutAction;
            CardSpawnEffectType spawnEffect = spawnInstant ? CardSpawnEffectType.None : effectType;

            if (effectType == CardSpawnEffectType.Drop) 
                SetState(WheelState.SpawningCard);

            WaitForSeconds delayWait = null;
            if (!spawnInstant && effectType != CardSpawnEffectType.None)
            {
                float delayPerCard = variable != null ? variable.DelayPerCard : 0.05f;
                delayWait = GetCardDelayWait(delayPerCard);
            }

            foreach (var req in requests)
            {
                if (!TryNormalizeCardRequest(req, out var normalizedReq))
                    continue;

                for (int i = 0; i < normalizedReq.Amount; i++)
                {
                    SpawnSingleCard(normalizedReq, spawnEffect);
                    if (!spawnInstant && effectType != CardSpawnEffectType.None)
                    {
                        if (delayWait != null) yield return delayWait;
                        else yield return null;
                    }
                }
            }

            if (effectType == CardSpawnEffectType.Drop) 
                SetState(WheelState.Active);

            _queuedRequests.Clear();
        }

        private WaitForSeconds GetCardDelayWait(float delaySeconds)
        {
            if (delaySeconds <= 0f) return null;

            if (_cachedCardDelayWait == null || !Mathf.Approximately(_cachedCardDelaySeconds, delaySeconds))
            {
                _cachedCardDelaySeconds = delaySeconds;
                _cachedCardDelayWait = new WaitForSeconds(delaySeconds);
            }

            return _cachedCardDelayWait;
        }

        private void SpawnSingleCard(NormalizedCardSpawnRequest req, CardSpawnEffectType effectType)
        {
            SpawnSingleCard(new WheelCardRuntimeData(req.Id, req.Level, req.CardType), effectType);
        }

        private void SpawnSingleCard(WheelCardRuntimeData runtimeCard, CardSpawnEffectType effectType)
        {
            EnsureInitialized();

            if (_characterList == null || variable == null) 
            {
                Debug.LogError($"[WheelUnit] SpawnSingleCard Failed! _characterList={(_characterList!=null)} variable={(variable!=null)}");
                return;
            }
            
            int totalSlots = TotalSlots;
            int slotIdx = _cachedTotalCards % totalSlots;
            Transform slotParent = preBakedSlots.Count > slotIdx ? preBakedSlots[slotIdx] : fullBody; // Fallback
            
            var slotList = _slotsMap[slotIdx];
            var visualSlotList = (_visualSlotsMap != null && slotIdx < _visualSlotsMap.Length) ? _visualSlotsMap[slotIdx] : null;
            int currentLayer = slotList.Count;
            float layerHeight = LayerHeight;

            if (!runtimeCard.IsCharacter)
            {
                if (!_warnedUnsupportedCardType)
                {
                    Debug.LogWarning("[WheelUnit] Hero card flow is not wired in playable yet (missing HeroList/HeroUnit integration). Card request skipped.");
                    _warnedUnsupportedCardType = true;
                }
                return;
            }

            int level = Mathf.Max(1, runtimeCard.Level);

            // Spawn
            var charData = _characterList.GetCharacterByLevel(level);
            if (charData == null) 
            {
                Debug.LogError($"[WheelUnit] SpawnSingleCard Failed! No CharacterEntry for level {level}");
                return;
            }
            
            // Use CardPrefab from CharacterEntry or Global config? 
            // Reference: _caceCharacterListDataSO.CardPrefab.Spawn
            var cardPrefab = _characterList.CardPrefab; 
            if (cardPrefab == null) 
            {
                Debug.LogError($"[WheelUnit] SpawnSingleCard Failed! CardPrefab is null in CharacterList {_characterList.name}");
                return;
            }

            var cardIns = Instantiate(cardPrefab, slotParent); // Simple Instantiate for now or Pool if available
            // cardIns is CardUnit
            cardIns.Initialize(runtimeCard.CardType, runtimeCard.Id, level, charData.CardData.Material, charData.CardData.Sprite);
            
            // Position Logic
            Vector3 targetLocalPos = new Vector3(0, currentLayer * layerHeight, 0);
            
            // Animation
            if (effectType == CardSpawnEffectType.None)
                cardIns.Transform.localPosition = targetLocalPos;
            else
                StartCoroutine(CoAnimateCard(cardIns.Transform, targetLocalPos, effectType));

            // Cache
            _cardsMap.Add(cardIns);
            if (visualSlotList != null) visualSlotList.Add(cardIns);
            var storedCard = new WheelCardRuntimeData(runtimeCard.Id > 0 ? runtimeCard.Id : level, level, runtimeCard.CardType);
            slotList.Add(storedCard);
            _runtimeCards.Add(storedCard);
            _cachedTotalCards++;
            
            UpdateAnchorVisibility(slotIdx);

            // Match reference feel: spawn SFX should play near the visual landing timing,
            // not immediately when the card object is created.
        }

        private bool TryNormalizeCardRequest(CardSpawnRequestData req, out NormalizedCardSpawnRequest normalizedReq)
        {
            normalizedReq = default;

            if (req.Amount <= 0) return false;

            // Legacy playable requests usually only set Level/Amount. In that case CardType is default(0),
            // which used to mean Hero in the source project, so treat it as Character here.
            bool legacyRequest = req.Level > 0 &&
                                 req.CardType == CardType.Hero &&
                                 (req.Id == 0 || req.Id == req.Level);
            CardType effectiveType = legacyRequest ? CardType.Character : req.CardType;

            int level = req.Level > 0 ? req.Level : req.Id;
            if (level <= 0) return false;

            int id = req.Id > 0 ? req.Id : level;
            if (effectiveType == CardType.Character || effectiveType == CardType.Solider)
            {
                // Character contract: id == level
                id = level;
                effectiveType = CardType.Character;
            }

            normalizedReq = new NormalizedCardSpawnRequest
            {
                Id = id,
                Level = level,
                Amount = req.Amount,
                CardType = effectiveType
            };
            return true;
        }

        private IEnumerator CoAnimateCard(Transform cardTrans, Vector3 targetLocal, CardSpawnEffectType type)
        {
             // Simple Lerp
             float duration = 0.4f;
             if (variable != null) duration = variable.DropDuration;
             
             Vector3 startLocal = targetLocal + Vector3.up * 5f; // Drop from high
             if (type == CardSpawnEffectType.FlyIn) startLocal = Vector3.zero; // From center?
             
             float t = 0;
             bool playedLandingSfx = false;
             while (t < 1f)
             {
                 t += Time.deltaTime / duration;
                 float k = Mathf.Clamp01(t);
                 cardTrans.localPosition = Vector3.Lerp(startLocal, targetLocal, k);

                 if (!playedLandingSfx && type != CardSpawnEffectType.None)
                 {
                     // Reference wheel plays DropCardSfx when the card is close to landing.
                     if (k >= 0.7f)
                     {
                         playedLandingSfx = true;
                         PlayAddCardSfx();
                     }
                 }
                 yield return null;
             }
             cardTrans.localPosition = targetLocal;

             if (!playedLandingSfx && type != CardSpawnEffectType.None)
             {
                 PlayAddCardSfx();
             }
        }

        
        public void RemoveCard(int amount)
        {
            if (amount <= 0) return;
            // [FIX] Ensure we have cards to remove
            if (_cardsMap.Count == 0 || _runtimeCards.Count == 0) return;

            int heroCardCount = 0;
            for (int i = 0; i < _runtimeCards.Count; i++)
            {
                if (!_runtimeCards[i].IsCharacter) heroCardCount++;
            }
            int minKeep = heroCardCount > 0 ? 2 : 1;
            int removable = Mathf.Max(0, _cardsMap.Count - minKeep);
            if (removable <= 0) return;

            int removeCount = Mathf.Min(amount, Mathf.Min(removable, Mathf.Min(_cardsMap.Count, _runtimeCards.Count)));

            for (int i = 0; i < removeCount; i++)
            {
                int lastIndex = _cardsMap.Count - 1;
                var cardToRemove = _cardsMap[lastIndex];

                int slotIdx = (_slotsMap != null && _slotsMap.Length > 0) ? (lastIndex % _slotsMap.Length) : -1;

                if (cardToRemove != null)
                {
                    PlayCardRemoveAnimation(cardToRemove);
                }

                _cardsMap.RemoveAt(lastIndex);
                _runtimeCards.RemoveAt(lastIndex);

                if (slotIdx >= 0 && slotIdx < _slotsMap.Length)
                {
                    var slotCardList = _slotsMap[slotIdx];
                    if (slotCardList.Count > 0)
                    {
                        // LIFO remove matches add order (round-robin by total index).
                        slotCardList.RemoveAt(slotCardList.Count - 1);
                    }
                    if (_visualSlotsMap != null && slotIdx < _visualSlotsMap.Length)
                    {
                        var slotVisualList = _visualSlotsMap[slotIdx];
                        if (slotVisualList.Count > 0)
                        {
                            slotVisualList.RemoveAt(slotVisualList.Count - 1);
                        }
                    }
                    UpdateAnchorVisibility(slotIdx);
                }

                _cachedTotalCards = Mathf.Max(0, _cachedTotalCards - 1);
            }
        }

        private void PlayCardRemoveAnimation(CardUnit card)
        {
            if (card == null) return;

            card.Transform.SetParent(null);

            Vector3 startPos = card.Transform.position;
            Quaternion startRot = card.Transform.rotation;

            Vector3 backwardDir = -fullBody.forward;
            float randomAngle = UnityEngine.Random.Range(-30f, 30f);
            Vector3 throwDir = Quaternion.Euler(0, randomAngle, 0) * backwardDir;

            Vector3 landPos = fullBody.position + (throwDir * variable.RemoveThrowDistance);
            landPos.y = variable.TargetGroundY;

            Quaternion landRot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            // [FIX] Use DOTween instead of LitMotion
            DOVirtual.Float(0f, 1f, variable.RemoveDuration, t =>
            {
                if (card == null || card.Transform == null) return;
                float x = Mathf.Lerp(startPos.x, landPos.x, t);
                float z = Mathf.Lerp(startPos.z, landPos.z, t);

                float linearY = Mathf.Lerp(startPos.y, landPos.y, t);
                float arc = 4 * variable.RemoveJumpHeight * t * (1f - t);

                card.Transform.position = new Vector3(x, linearY + arc, z);
                
                float rotT = Mathf.SmoothStep(0, 1, t);
                card.Transform.rotation = Quaternion.Slerp(startRot, landRot, rotT);
            })
            .SetEase(Ease.Linear)
            .OnComplete(() => 
            { 
                if (card != null) PlayCardBounceAnimation(card, landPos, throwDir, landRot); 
            });
        }

        private void PlayCardBounceAnimation(CardUnit card, Vector3 startPos, Vector3 slideDir, Quaternion startRot)
        {
            if (card == null) return;

            Vector3 finalPos = startPos + (slideDir.normalized * variable.BounceSlideDist);
            finalPos.y = variable.TargetGroundY;

            // [FIX] Use DOTween instead of LitMotion
            DOVirtual.Float(0f, 1f, variable.BounceDuration, t =>
            {
                if (card == null || card.Transform == null) return;
                float x = Mathf.Lerp(startPos.x, finalPos.x, t);
                float z = Mathf.Lerp(startPos.z, finalPos.z, t);
                float arc = 4 * variable.BounceHeight * t * (1f - t);

                card.Transform.position = new Vector3(x, variable.TargetGroundY + arc, z);
                card.Transform.rotation = startRot;
            })
            .SetEase(Ease.OutQuad)
            .OnComplete(() => 
            { 
                if (card != null) { Destroy(card.gameObject); } 
            });
        }

        // --- Helpers ---
        private float GetForwardSpeed()
        {
            return ForwardSpeedVal;
        }

        public void SetIdle() => SetState(WheelState.Idle);
        public void SetActive()
        {
            bool wasActive = currentState == WheelState.Active;
            SetState(WheelState.Active);
            if (!wasActive)
            {
                TriggerWheelActiveAnimation();
            }
        }

        public void SetState(WheelState state)
        {
            if (currentState == state) return;
            if (state == WheelState.Active)
            {
                SyncVisualRotationIfNeeded();
            }
            currentState = state;
            OnStateChanged?.Invoke(currentState);
        }

        private void TriggerWheelActiveAnimation()
        {
            EnsureWheelAnimatorsResolved();

            if (wheelAnimators == null || wheelAnimators.Count == 0) return;
            if (string.IsNullOrEmpty(wheelActiveTrigger)) return;

            for (int i = 0; i < wheelAnimators.Count; i++)
            {
                var animator = wheelAnimators[i];
                if (animator == null) continue;
                animator.SetTrigger(wheelActiveTrigger);
            }
        }

        private void EnsureWheelAnimatorsResolved()
        {
            if (wheelAnimators == null)
            {
                wheelAnimators = new List<Animator>();
            }

            bool hasValid = false;
            for (int i = 0; i < wheelAnimators.Count; i++)
            {
                if (wheelAnimators[i] != null)
                {
                    hasValid = true;
                    break;
                }
            }
            if (hasValid) return;

            wheelAnimators.Clear();

            var allAnimators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < allAnimators.Length; i++)
            {
                var animator = allAnimators[i];
                if (animator == null || animator.runtimeAnimatorController == null) continue;

                string objName = animator.gameObject.name;
                string ctrlName = animator.runtimeAnimatorController.name;

                bool isWheelAnimator =
                    objName.StartsWith("wheel_", StringComparison.OrdinalIgnoreCase) ||
                    ctrlName.IndexOf("roll_wheel", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isWheelAnimator) continue;
                if (!wheelAnimators.Contains(animator))
                {
                    wheelAnimators.Add(animator);
                }
            }
        }
        
        // --- Playable Stub ---
        public IReadOnlyList<CardSpawnRequestData> GetQueuedCardRequests() => _queuedRequests;
        public void ClearQueuedRequests() => _queuedRequests.Clear();

        // --- Spawn Logic ---
        private void SpawnFromSlot(int slotIndex)
        {
            if (_slotsMap == null || slotIndex < 0 || slotIndex >= _slotsMap.Length) 
            {
               // Debug.LogWarning($"[WheelUnit] Invalid Slot Index: {slotIndex}");
               return;
            }

            // Force one active outline only. More robust than previous/current toggling when state drifts.
            DisableAllSlotOutlines();
            SetEnableSlotOutline(slotIndex, true);
            
            List<WheelCardRuntimeData> slotCards = _slotsMap[slotIndex];
            int count = slotCards.Count;
            
            if (count == 0) return;
            PruneInactiveSpawnedUnits();

             if (triggerSfx != AudioClipName.None && SoundManager.Instance != null)
                 SoundManager.Instance.PlayOneShot(triggerSfx);

            PlayArrowBounceEffect();
             
             // Calculate Spawn Position
             Vector3 spawnDir = transform.forward; 
             Vector3 centerPos = unitSpawnPoint != null ? unitSpawnPoint.position : fullBody.position;
             Vector3 rightDir = fullBody.right;
             
             float unitSpace = UnitHorizontalSpace;
             float totalW = (count - 1) * unitSpace;
             float startX = -(totalW / 2f);
             
             Quaternion spawnRotation = Quaternion.LookRotation(spawnDir);

             for (int i = 0; i < count; i++)
             {
                 var slotCard = slotCards[i];
                 if (!slotCard.IsCharacter)
                 {
                     if (!_warnedUnsupportedCardType)
                     {
                         Debug.LogWarning("[WheelUnit] Hero slot trigger is not supported in playable yet. Skipping hero spawn.");
                         _warnedUnsupportedCardType = true;
                     }
                     continue;
                 }

                 int level = slotCard.Level;
                 var characterData = _characterList.GetCharacterByLevel(level);
                 if (characterData == null) 
                 {
                     Debug.LogError($"[WheelUnit] No CharacterEntry for level {level}");
                     continue;
                 }

                 float xOffset = startX + (i * unitSpace);
                 Vector3 pos = centerPos + rightDir * xOffset;

                 if (!TryReserveSpawnSlot())
                 {
                     break;
                 }
                  
                 // Spawn CharacterUnit from Pool
                 // Assumes CharacterPrefab is a PoolEntity or handling basic Instantiate
                 var unit = characterData.CharacterPrefab.Spawn(pos, spawnRotation);
                 if (unit != null)
                 {
                     unit.Initialize(level);
                     _activeSpawnedUnits.Enqueue(unit);
                     // Debug.Log($"[WheelUnit] Spawned Unit Level {level} at {pos}");
                 }
                 else
                 {
                     Debug.LogError($"[WheelUnit] Failed to spawn unit. Prefab: {(characterData.CharacterPrefab != null ? characterData.CharacterPrefab.name : "NULL")}. Spawn returned null.");
                 }
             }
         }

        private void ClearActiveSpawnedUnits()
        {
            while (_activeSpawnedUnits.Count > 0)
            {
                var unit = _activeSpawnedUnits.Dequeue();
                if (unit == null) continue;
                if (!unit.gameObject.activeInHierarchy) continue;

                unit.Transform.parent = null;
                unit.Transform.localScale = Vector3.one;
                unit.RecycleImmediate(false);
            }
        }

        private void PruneInactiveSpawnedUnits()
        {
            int count = _activeSpawnedUnits.Count;
            for (int i = 0; i < count; i++)
            {
                var unit = _activeSpawnedUnits.Dequeue();
                if (unit == null) continue;
                if (!unit.gameObject.activeInHierarchy) continue;

                _activeSpawnedUnits.Enqueue(unit);
            }
        }

        private bool TryReserveSpawnSlot()
        {
            int cap = Mathf.Clamp(maxActiveSpawnedUnits, 1, HardMaxActiveSpawnedUnits);
            if (_activeSpawnedUnits.Count < cap)
                return true;

            if (despawnOldestWhenOverCap)
            {
                // Legacy mode kept for inspector compatibility.
            }

            // Replacing active units under pressure causes heavy churn (spawn/despawn loops).
            // Keeping current units and skipping extra spawns is cheaper and more stable.
            return false;
        }

        private void SyncVisualRotationIfNeeded()
        {
            if (_rotationSynced) return;
            if (visualModel == null) return;

            _totalRotation = visualModel.localEulerAngles.y;
            _angleAccumulator = 0f;
            _rotationSynced = true;
        }

        private void EnsureSfxSource()
        {
            if (sfxSource != null) return;
            Debug.LogWarning($"[WheelUnit] Missing AudioSource on {name}. Assign in Inspector.");
        }

        private void PlayAddCardSfx()
        {
            var sfx = addCardSfx != AudioClipName.None ? addCardSfx : AudioClipName.SFX_DropCard;
            if (sfx == AudioClipName.None) return;

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayOneShot(sfx);
                return;
            }

            AudioClip clip = null;
            float volume = 1f;

            if (clip == null)
            {
                if (_cachedDropCardClip == null)
                {
                    _cachedDropCardClip = Resources.Load<AudioClip>($"Sound/{sfx}");
                }
                clip = _cachedDropCardClip;
            }

            if (clip != null)
            {
                EnsureSfxSource();
                if (sfxSource != null)
                {
                    sfxSource.PlayOneShot(clip, volume);
                }
            }
        }

        private void PlayArrowBounceEffect()
        {
            if (arrowModel == null) return;

            if (_arrowKickRoutine != null)
                StopCoroutine(_arrowKickRoutine);

            _arrowKickRoutine = StartCoroutine(CoArrowKick());
        }

        private System.Collections.IEnumerator CoArrowKick()
        {
            float startVal = _currentDeflection;
            float targetVal = Mathf.Clamp(startVal + arrowKickForce, -arrowMaxAngle, arrowMaxAngle);

            float t = 0f;
            while (t < kickDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, kickDuration));
                float angle = Mathf.Lerp(startVal, targetVal, k);
                UpdateArrowVisual(angle);
                yield return null;
            }

            t = 0f;
            while (t < returnDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, returnDuration));
                float angle = Mathf.Lerp(targetVal, 0f, k);
                UpdateArrowVisual(angle);
                yield return null;
            }

            UpdateArrowVisual(0f);
            _arrowKickRoutine = null;
        }


        private void UpdateArrowVisual(float angle)
        {
            _currentDeflection = angle;
            if (arrowModel != null)
                arrowModel.localRotation = _arrowInitialRotation * Quaternion.Euler(0f, angle, 0f);
        }
        // --- Anchor Logic ---
        private void SetupVisualAnchors()
        {
            if (anchorObjects == null) return;
            for (int i = 0; i < anchorObjects.Count; i++)
            {
                if (anchorObjects[i] != null) anchorObjects[i].SetActive(false);
            }
        }

        private void DisableAllSlotOutlines()
        {
            EnsureOutlineSlotsResolved();

            if (outlineSlots != null)
            {
                for (int i = 0; i < outlineSlots.Count; i++)
                {
                    if (outlineSlots[i] != null) ApplySlotOutlineRendererState(outlineSlots[i], false);
                }
            }

            if (_visualSlotsMap == null) return;

            for (int i = 0; i < _visualSlotsMap.Length; i++)
            {
                SetEnableSlotOutline(i, false);
            }
        }

        private void SetEnableSlotOutline(int slotIndex, bool enable)
        {
            if (_slotsMap == null || slotIndex < 0 || slotIndex >= _slotsMap.Length) return;
            EnsureOutlineSlotsResolved();

            if (outlineSlots != null && slotIndex < outlineSlots.Count && outlineSlots[slotIndex] != null)
            {
                ApplySlotOutlineRendererState(outlineSlots[slotIndex], enable);
            }

            if (_visualSlotsMap == null || slotIndex >= _visualSlotsMap.Length) return;

            var visualList = _visualSlotsMap[slotIndex];
            if (visualList == null) return;

            for (int i = 0; i < visualList.Count; i++)
            {
                if (visualList[i] != null) visualList[i].SetEnableOutline(enable);
            }
        }

        private void ApplySlotOutlineRendererState(MeshRenderer renderer, bool enable)
        {
            if (renderer == null) return;

            if (!_outlineSlotsUseFallbackTint)
            {
                renderer.enabled = enable;
                return;
            }

            // t1_a3 fallback: these renderers are also used as anchor/count visuals, so tint them instead of hiding.
            renderer.enabled = true;

            if (_slotOutlineMpb == null) _slotOutlineMpb = new MaterialPropertyBlock();
            var state = GetSlotOutlineVisualState(renderer);

            renderer.GetPropertyBlock(_slotOutlineMpb);

            if (state.ColorPropertyId != -1)
            {
                Color targetColor = enable
                    ? Color.Lerp(state.BaseColor, new Color(0.85f, 1f, 1f, state.BaseColor.a), 0.75f)
                    : Color.Lerp(state.BaseColor, Color.black, 0.55f);
                targetColor.a = state.BaseColor.a;
                _slotOutlineMpb.SetColor(state.ColorPropertyId, targetColor);
            }

            if (state.HasEmission)
            {
                _slotOutlineMpb.SetColor(SlotEmissionColorId, enable ? new Color(0.65f, 1f, 1f) * 3f : Color.black);
            }

            renderer.SetPropertyBlock(_slotOutlineMpb);
        }

        private SlotOutlineVisualState GetSlotOutlineVisualState(MeshRenderer renderer)
        {
            if (renderer != null && _slotOutlineVisualStates.TryGetValue(renderer, out var cached))
            {
                return cached;
            }

            var state = new SlotOutlineVisualState
            {
                ColorPropertyId = -1,
                HasEmission = false,
                BaseColor = Color.white
            };

            if (renderer != null)
            {
                var mat = renderer.sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty(SlotBaseColorId)) state.ColorPropertyId = SlotBaseColorId;
                    else if (mat.HasProperty(SlotColorId)) state.ColorPropertyId = SlotColorId;

                    if (state.ColorPropertyId != -1)
                    {
                        state.BaseColor = mat.GetColor(state.ColorPropertyId);
                    }

                    state.HasEmission = mat.HasProperty(SlotEmissionColorId);
                }

                _slotOutlineVisualStates[renderer] = state;
            }

            return state;
        }

        private void EnsureOutlineSlotsResolved()
        {
            int required = TotalSlots > 0 ? TotalSlots : 8;

            if (outlineSlots == null)
            {
                outlineSlots = new List<MeshRenderer>(required);
            }

            bool needResolve = outlineSlots.Count == 0;
            if (!needResolve)
            {
                int nonNullCount = 0;
                for (int i = 0; i < outlineSlots.Count; i++)
                {
                    if (outlineSlots[i] != null) nonNullCount++;
                }
                needResolve = nonNullCount == 0;
            }

            if (!needResolve) return;

            _outlineSlotsUseFallbackTint = false;
            outlineSlots.Clear();
            _slotOutlineVisualStates.Clear();

            // Fallback for imported wheels (ex: t1_a3) where outlineSlots list is missing.
            // In these prefabs, anchorObjects often point to the same "count_card_*" glow meshes.
            if (anchorObjects != null && anchorObjects.Count > 0)
            {
                for (int i = 0; i < anchorObjects.Count && outlineSlots.Count < required; i++)
                {
                    var go = anchorObjects[i];
                    if (go == null)
                    {
                        outlineSlots.Add(null);
                        continue;
                    }

                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer == null)
                    {
                        renderer = go.GetComponentInChildren<MeshRenderer>(true);
                    }
                    outlineSlots.Add(renderer);
                }

                if (outlineSlots.Count > 0)
                {
                    _outlineSlotsUseFallbackTint = true;
                }
            }

            // Secondary fallback: scan under visualModel for "count_card" renderers.
            if (outlineSlots.Count == 0 && visualModel != null)
            {
                var allRenderers = visualModel.GetComponentsInChildren<MeshRenderer>(true);
                Array.Sort(allRenderers, (a, b) => string.CompareOrdinal(a.name, b.name));
                for (int i = 0; i < allRenderers.Length && outlineSlots.Count < required; i++)
                {
                    var r = allRenderers[i];
                    if (r == null) continue;
                    if (r.name.IndexOf("count_card", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        outlineSlots.Add(r);
                    }
                }

                if (outlineSlots.Count > 0)
                {
                    _outlineSlotsUseFallbackTint = true;
                }
            }
        }

        private void UpdateAnchorVisibility(int slotIndex)
        {
            if (anchorObjects == null || slotIndex < 0 || slotIndex >= anchorObjects.Count) return;
            
            bool hasCards = false;
            // Robust check for cards in slot
            if (_slotsMap != null && slotIndex < _slotsMap.Length)
            {
                hasCards = _slotsMap[slotIndex].Count > 0;
            }

            if (anchorObjects[slotIndex] != null && anchorObjects[slotIndex].activeSelf != hasCards)
            {
                anchorObjects[slotIndex].SetActive(hasCards);
            }
        }
    }
}

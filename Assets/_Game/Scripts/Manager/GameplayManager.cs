using System.Collections;
using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.CollisionSystems;
using UnityEngine.Events;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Crushers;
using GamePlay.Enemies;
using GamePlay.Items;
using GamePlay.Managers;
using GamePlay.Map;
using GamePlay.OscillationSystems;
using GamePlay.Effects;
using PlayerArmy;
using Pools;
using UnityEngine;
using UnityEngine.Rendering;
using System.Reflection;

public class GameplayManager : MonoSingleton<GameplayManager>, IGameplayFlow
{
    // Capacity gate/factory coin pool (parity with full project flow).
    public static int StartCoin;
    public static int StartCoinPending;

    [Header("Playable Level (drag trực tiếp - không dùng DataManager/ConfigHolder)")]
    [SerializeField] private EraDataSO playableEra;
    public EraDataSO PlayableEra => playableEra;
    [SerializeField] private ContentDataSO playableContent;
    [SerializeField] private ContentDataSO playableTowerZoneContent;

#if UNITY_EDITOR
    [Header("Editor Auto Generate")]
    [SerializeField] private bool autoGenerateMapInEditor = true;
    [SerializeField] private bool autoGenerateContentInEditor = false;
    [SerializeField] private bool regenerateOnEraChangeOnly = true;
    [SerializeField] private bool usePrebakedMapInPlayMode = true;
    [SerializeField] private bool usePrebakedContentInPlayMode = true;
    private EraDataSO _lastEraEditor;
    private ContentDataSO _lastContentEditor;
    private ContentDataSO _lastTowerZoneContentEditor;
    private bool _isGeneratingEditor;
    private bool _generateQueued;

#endif
    [SerializeField] private bool disableEndGameCameraSwitch = true;
    [SerializeField] private bool useCtaOnlyEndgameMode = false;
    [SerializeField] private List<CardSpawnRequestData> initialCards; // Configurable via Inspectornerator;
    [SerializeField] private AudioClipName winEndcardSfx = AudioClipName.SFX_Level_Complete;
    [Header("Explosion Shot Buff")]
    [SerializeField, Min(0f)] private float explosionShotRadius = 3.25f;
    [SerializeField, Min(0)] private int explosionShotBasePercent = 90;
    [SerializeField, Min(0)] private int explosionShotUpgradePercent = 35;

    [Header("Refs")]
    [SerializeField] private MapGenerator mapGenerator;
    [SerializeField] private MapContentGenerator contentGenerator;

    [Header("Player/Wheel")]
    [HideInInspector] public WheelUnit Turnable;
    public float TurnableSpawnOffset = 7.5f;
    public bool followHorizontal = true;

    [Header("Player/Army (New System)")]
    [SerializeField] private PlayerArmySystem playerArmyPrefab;
    public PlayerArmySystem ActiveArmy { get; private set; }
    private bool IsArmyMode => playerArmyPrefab != null;

    [Header("Gameplay Variables")]
    public GamePlayVariable gamePlayVariable;

    [Header("Startup Performance")]
    [SerializeField] private int initItemsPerFrame = 5; // Reduced to prevent lag spikes
    [SerializeField] private int spawnItemsPerFrame = 10;

    [Header("Startup Flow")]
    [SerializeField] private bool waitForTapBeforeGameplay = true;
    [SerializeField] private bool autoStartIfTutorialMissing = true;

    [Header("VFX Prefabs (Assign in Inspector)")]
    [SerializeField] private List<GameObject> extraVfxPrefabs = new List<GameObject>();

    [Header("Milestone (Playable)")]
    [SerializeField] private bool showMilestoneOnWin = true;
    [SerializeField] private float milestoneEndcardDelay = 1.0f;

    public static bool IsGameStarted;
    private bool _endGameSfxPlayed;
    private WeaponCraft.WeaponItem _mainWeapon;

    // Tick optimization: frame skipping for heavy effects
    private int _tickFrameCounter;
    private const int TIMED_DISABLE_SKIP_FRAMES = 2;  // Update every 2 frames (~30fps instead of 60fps)

    // Reflection caches for Luna-compatible render optimization (avoid per-call lookup/alloc).
    private static readonly PropertyInfo SkinnedQualityProperty =
        typeof(SkinnedMeshRenderer).GetProperty("quality", BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo SkinnedMotionVectorsProperty =
        typeof(SkinnedMeshRenderer).GetProperty("skinnedMotionVectors", BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo SkinnedUpdateWhenOffscreenProperty =
        typeof(SkinnedMeshRenderer).GetProperty("updateWhenOffscreen", BindingFlags.Instance | BindingFlags.Public);

    private Dictionary<CurrencyType, int> _currencyValues = new Dictionary<CurrencyType, int>();
    public WeaponCraft.WeaponItem MainWeapon => _mainWeapon;
    public UnityAction<WeaponCraft.WeaponItem> OnWeaponChange;
    public UnityAction<CurrencyType, int, Vector3> OnCurrencyChanged;

    public int GetCurrency(CurrencyType type)
    {
        _currencyValues.TryGetValue(type, out int val);
        return val;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (_isGeneratingEditor) return;
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

        bool eraChanged = playableEra != _lastEraEditor;
        bool contentChanged = playableContent != _lastContentEditor;
        bool towerContentChanged = playableTowerZoneContent != _lastTowerZoneContentEditor;

        if (regenerateOnEraChangeOnly && !eraChanged && !contentChanged && !towerContentChanged) return;

        // Defer generation to avoid DestroyImmediate during OnValidate.
        if (!_generateQueued)
        {
            _generateQueued = true;
            UnityEditor.EditorApplication.delayCall += GenerateInEditor;
        }
    }
#endif

#if UNITY_EDITOR
    private void GenerateInEditor()
    {
        _generateQueued = false;
        if (Application.isPlaying) return;
        if (_isGeneratingEditor) return;

        try
        {
            _isGeneratingEditor = true;

            if (autoGenerateMapInEditor && playableEra != null && playableEra.MapData != null && mapGenerator != null)
            {
                mapGenerator.GenerateMap(playableEra.MapData);
            }

            if (autoGenerateContentInEditor && playableContent != null && contentGenerator != null)
            {
                contentGenerator.GenerateContentData(playableContent, playableTowerZoneContent);
            }

        }
        finally
        {
            _isGeneratingEditor = false;
            _lastEraEditor = playableEra;
            _lastContentEditor = playableContent;
            _lastTowerZoneContentEditor = playableTowerZoneContent;
        }
    }
#endif

    // Optimized tick system: frame skipping + early exits for empty collections
    private void Update()
    {
        float dt = Time.deltaTime;
        _tickFrameCounter++;

        // Critical effects: every frame (smooth animations)
        HitTextFlyEffect.TickActiveControllers(dt);
        BrickFallMotion.TickActiveMotions(dt);
        CurrencyDropItem.TickActiveDrops(dt);

        // Important effects: every frame (game logic)
        // DeathScaleEffect.TickActiveEffects(dt);
        DebrisBlock.TickActiveBlocks(dt);
    }

    public void AddCurrency(CurrencyType type, int amount, Vector3 worldPosition = default)
    {
        if (amount <= 0) return;

        if (!_currencyValues.ContainsKey(type))
            _currencyValues[type] = 0;

        _currencyValues[type] += amount;
        OnCurrencyChanged?.Invoke(type, _currencyValues[type], worldPosition);
    }

    public bool TrySpendCurrency(CurrencyType type, int amount)
    {
        if (amount <= 0) return true;

        int current = GetCurrency(type);
        if (current < amount) return false;

        _currencyValues[type] = current - amount;
        OnCurrencyChanged?.Invoke(type, _currencyValues[type], Vector3.zero);
        return true;
    }

    public void ResetCurrency(CurrencyType type, int value = 0)
    {
        _currencyValues[type] = Mathf.Max(0, value);
        OnCurrencyChanged?.Invoke(type, _currencyValues[type], Vector3.zero);
    }
    private Coroutine _startGameRoutine;
    private Coroutine _endGameRoutine;
    private readonly List<CardSpawnRequestData> _singleRequestBuffer = new List<CardSpawnRequestData>(1);
    private readonly List<IHitable> _collisionHitablesBuffer = new List<IHitable>(128);
    private readonly List<Transform> _collisionTransformsBuffer = new List<Transform>(128);
    private bool _hasOfferedExplosionShotThisRun;
    private bool _isExplosionShotUnlocked;
    private int _explosionShotDamagePercent;
    private readonly HashSet<StatType> _appliedPrimaryBuffTypes = new HashSet<StatType>();
    public HashSet<string> AcquiredSwordSkills = new HashSet<string>();
    public List<CardSystem.Data.BuffDefinition> ActiveSamuraiBuffs = new List<CardSystem.Data.BuffDefinition>();
    private MilestoneOnMap _currentMilestone;
    private bool _hasMilestoneOverride;
    private Vector3 _milestoneWorldPosOverride;

    public Transform PlayerTransform => IsArmyMode && ActiveArmy != null ? ActiveArmy.BodyTransform : Turnable != null ? Turnable.Transform : null;
    public float ExplosionShotRadius => explosionShotRadius;
    public int ExplosionShotBasePercent => Mathf.Max(0, explosionShotBasePercent);
    public int ExplosionShotUpgradePercent => Mathf.Max(0, explosionShotUpgradePercent);
    public int ExplosionShotDamagePercent => Mathf.Max(0, _explosionShotDamagePercent);
    public bool IsExplosionShotUnlocked => _isExplosionShotUnlocked;

    bool IGameplayFlow.IsGameStarted => IsGameStarted;

    private void Start()
    {
        UIFullScreenBlocker.Instance.Lock();
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0; // Disable VSync to ensure target framerate is respected
        Time.fixedDeltaTime = 1f / 60f; // Optimize physics step to match target framerate
        DataManager.InitData();
        // Auto boot playable
        StartCoroutine(CoBootAndIntroSequence());
    }

    private IEnumerator CoBootAndIntroSequence()
    {
        ClearRuntimeTickCaches();
        DataManager.ResetToDefault();
        GamePlay.CardSystem.BuffCardSystem.Instance?.Clear();

        // 1. Instantly set camera to FollowPlayer and Hide UI
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.FollowPlayer, CameraFollow.TransitionMode.Instant);
        }

        if (LunaUIManager.Instance != null)
        {
            LunaUIManager.Instance.SetUIVisibility(false);
        }

        yield return null;

        // ==============================================================================
        // [A/B TEST BLOCK] - BẬT/TẮT (COMMENT) CÁC PHASE DƯỚI ĐÂY ĐỂ TEST
        // ==============================================================================

        // Phần 1: Map, Content, Player Army & Weapon Projectile
        yield return StartCoroutine(CoPhase1_MapContentAndArmy());

        // Phần 2: Prewarm Asset (Extra VFX, Weapon Item, Buff Card, Sword Skill...)
        yield return StartCoroutine(CoPhase2_PrewarmAssets());

        // ==============================================================================

        // 7. Setup Camera and Milestone (Luôn chạy để đảm bảo flow game không bị treo)
        var trackPreview = CameraManager.Instance.GetCameraFollow().GetStateByName(CameraFollow.CameraStateName.TrackPreview) as TrackPreviewCameraState;
        if (trackPreview && mapGenerator != null && mapGenerator.activeSegments != null && mapGenerator.activeSegments.Count > 0)
        {
            trackPreview.startPoint = mapGenerator.activeSegments[0].EntryPoint;
            trackPreview.endPoint = mapGenerator.activeSegments[mapGenerator.activeSegments.Count - 1].ExitPoint;
        }

        var finishView = CameraManager.Instance.GetCameraFollow().GetStateByName(CameraFollow.CameraStateName.Finish) as StaticCameraState;
        if (finishView && contentGenerator != null && contentGenerator.GateNewEraTrans)
        {
            finishView.SetTargetTransform(contentGenerator.GateNewEraTrans);
        }

        if (_currentMilestone != null)
        {
            _currentMilestone.Despawn();
            _currentMilestone = null;
        }
        if (playableEra != null && playableEra.Milestone != null && contentGenerator != null)
        {
            _currentMilestone = contentGenerator.SpawnMilestoneItem(playableEra.Milestone);
            if (_currentMilestone != null) _currentMilestone.gameObject.SetActive(false);
        }

        EnsureWeaponCraftStarterItem();

        // 8. Start UI Animation and unlock input
        if (LunaUIManager.Instance != null)
        {
            LunaUIManager.Instance.AnimateUIIntro(() =>
            {
                LunaUIManager.Instance.ShowTutorial(true);
                if (UIFullScreenBlocker.Instance != null) UIFullScreenBlocker.Instance.Unlock(-1, forceUnlockAll: true);
            });
        }
        else
        {
            if (UIFullScreenBlocker.Instance != null) UIFullScreenBlocker.Instance.Unlock(-1, forceUnlockAll: true);
        }
    }

    private IEnumerator CoPhase1_MapContentAndArmy()
    {
        // Generate Map
        bool hasPrebakedMap = mapGenerator.GetActiveSegments().Count > 0;
        bool shouldRegenerateMap = !hasPrebakedMap || (mapGenerator != null && mapGenerator.CurrentMapData != null && playableEra != null && mapGenerator.CurrentMapData != playableEra.MapData);

        if (shouldRegenerateMap && mapGenerator != null && playableEra != null)
        {
            mapGenerator.GenerateMap(playableEra.MapData);
        }

        // Generate Content
        if (contentGenerator != null)
        {
#if UNITY_EDITOR
            if (Application.isEditor && !Application.isPlaying && autoGenerateContentInEditor)
            {
                yield return StartCoroutine(contentGenerator.GenerateContentDataAsync(playableContent, playableTowerZoneContent, false, Mathf.Max(1, spawnItemsPerFrame)));
            }
            else
            {
#endif
                if (contentGenerator.HasPrebakedContent()) contentGenerator.UsePrebakedContent(false);
                else yield return StartCoroutine(contentGenerator.GenerateContentDataAsync(playableContent, playableTowerZoneContent, false, Mathf.Max(1, spawnItemsPerFrame)));
#if UNITY_EDITOR
            }
#endif
        }

        // Spawn / Binding Army
        var playerSpawnRect = mapGenerator.GetSpawnPlayerTransform();
        Vector3 targetPos = playerSpawnRect.position + Vector3.forward * TurnableSpawnOffset;
        Vector3 startPos = playerSpawnRect.position + Vector3.forward * 1f;

        if (IsArmyMode)
        {
            // Sử dụng object có sẵn trên scene
            ActiveArmy = playerArmyPrefab;
            ActiveArmy.transform.position = startPos;
            ActiveArmy.transform.rotation = Quaternion.identity;

            if (mapGenerator != null) mapGenerator.BindWheelTransform(ActiveArmy.BodyTransform);
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.SetPlayerTransform(ActiveArmy.BodyTransform);
            }
            ActiveArmy.Initialize();
            var seedCards = (initialCards != null && initialCards.Count > 0)
                ? initialCards
                : BuildInitialWheelCardsFromRuntimeState();
            ActiveArmy.AddCards(seedCards, CardSpawnEffectType.DropWithoutAction);
            ActiveArmy.SetActive();
        }

        if (EnemyManager.Instance != null) EnemyManager.Instance.UnregisterAllEnemies();
        EnemyProjectileSystem.UnregisterPlayer();

        // Initialize Content Items
        if (contentGenerator != null && contentGenerator.generatedObjects != null)
        {
            int batchSize = Mathf.Max(1, initItemsPerFrame);
            for (int i = 0; i < contentGenerator.generatedObjects.Count; i++)
            {
                var item = contentGenerator.generatedObjects[i];
                if (item != null) item.Initialize();
                if ((i + 1) % batchSize == 0) yield return null;
            }
        }

        // Prewarm Army Prefabs & Weapon Projectiles
        if (IsArmyMode && ActiveArmy != null)
        {
            Debug.Log("Prewarm Army Prefabs");
            yield return StartCoroutine(ActiveArmy.PrewarmArmyPrefabsAsync(Mathf.Max(1, spawnItemsPerFrame)));
        }

        // Army Move Sequence
        if (IsArmyMode && ActiveArmy != null)
        {
            ActiveArmy.SetIntroRun();
            float speed = 15f;
            while (Vector3.Distance(ActiveArmy.transform.position, targetPos) > 0.05f)
            {
                ActiveArmy.transform.position = Vector3.MoveTowards(ActiveArmy.transform.position, targetPos, speed * Time.deltaTime);
                yield return null;
            }
            ActiveArmy.transform.position = targetPos;
            ActiveArmy.SetIdle();
        }
    }

    private IEnumerator CoPhase2_PrewarmAssets()
    {
        // Prewarm Extra VFX
        foreach (var prefab in extraVfxPrefabs)
        {
            if (prefab != null)
            {
                yield return PoolSystem.PrewarmAsync(prefab.transform, 5, Mathf.Max(1, spawnItemsPerFrame));
            }
        }

        // Prewarm Buff Cards
        if (GamePlay.CardSystem.BuffCardSystem.Instance != null)
        {
            GamePlay.CardSystem.BuffCardSystem.Instance.PrewarmCards();
        }

        // Prewarm Weapon Craft (Sword Skill, Drops, v.v..)
        if (WeaponCraft.WeaponCraftSystem.Instance != null)
        {
            WeaponCraft.WeaponCraftSystem.Instance.Prewarm();
        }

        yield return null;
    }
    public void RunUpgradeEffect()
    {
        if (ActiveArmy == null)
        {
            return;
        }

        ActiveArmy.PlayEffect(EffectType.Upgrade, ActiveArmy.transform);
    }

    public void RunUpgradeEffect(Transform anchor)
    {
        if (ActiveArmy == null)
        {
            return;
        }

        if (anchor == null)
        {
            RunUpgradeEffect();
            return;
        }

        ActiveArmy.PlayEffectAt(EffectType.Upgrade, anchor.position, anchor.rotation, anchor);
    }

    public void RunUpgradeEffectAt(Vector3 position, Transform parent = null)
    {
        if (ActiveArmy == null)
        {
            return;
        }

        ActiveArmy.PlayEffectAt(EffectType.Upgrade, position, Quaternion.identity, parent);
    }

    // Stub removed to allow generic ChangeStatModifierData to handle EvolutionPoint logic.

    private static void ClearRuntimeTickCaches()
    {
        CurrencyDropItem.ClearActiveDrops();
        DeathScaleEffect.ClearAll();
        DebrisBlock.ClearActiveBlocks();
    }


    #region Start/End Game (Playable)

    public void StartGame(bool activeTurnable = false)
    {
        _hasOfferedExplosionShotThisRun = false;
        _isExplosionShotUnlocked = false;
        _explosionShotDamagePercent = 0;
        _appliedPrimaryBuffTypes.Clear();
        AcquiredSwordSkills.Clear();
        ActiveSamuraiBuffs.Clear();
        gamePlayVariable?.ResetNewGame();
        gamePlayVariable?.ResetEvolutionVariable();
        StartCoin = 0;
        StartCoinPending = 0;

        // Smooth transition from Waiting to FollowPlayer (avoid abrupt jump).
        CameraManager.Instance.SetCameraStateByName(
            CameraFollow.CameraStateName.FollowPlayer,
            CameraFollow.TransitionMode.Smooth
        );

        // Setup collision targets
        CollisionSystem.UnregisterAll();

        _collisionHitablesBuffer.Clear();
        _collisionTransformsBuffer.Clear();
        if (contentGenerator != null && contentGenerator.generatedObjects != null)
        {
            var generated = contentGenerator.generatedObjects;
            int expected = generated.Count;
            if (_collisionHitablesBuffer.Capacity < expected) _collisionHitablesBuffer.Capacity = expected;
            if (_collisionTransformsBuffer.Capacity < expected) _collisionTransformsBuffer.Capacity = expected;

            foreach (var g in generated)
            {
                if (g == null || g.Pack.Hitable == null) continue;

                _collisionHitablesBuffer.Add(g.Pack.Hitable);
                // Use the IHitable's transform when possible (HitComponent may be on a child)
                var hitableComponent = g.Pack.Hitable as Component;
                _collisionTransformsBuffer.Add(hitableComponent != null ? hitableComponent.transform : g.Transform);
            }
        }
        CollisionSystem.RegisterBatch(_collisionHitablesBuffer, _collisionTransformsBuffer);

        // Setup conveyor gates
        if (ConveyorManager.Instance != null && contentGenerator != null)
        {
            ConveyorManager.Instance.SetGatePositions(contentGenerator.generatedObjects);
        }

        // Reset wheel/character variables
        gamePlayVariable?.ResetCharacterVariable();
        gamePlayVariable?.ResetWheelVariable();
        ResetCurrency(CurrencyType.Gold);
        ResetCurrency(CurrencyType.Cash);
        GameEventBus.UpdateCapacityBar?.Invoke();

        //EnsureWeaponCraftStarterItem();

        IsGameStarted = false;

        if (IsArmyMode && ActiveArmy != null)
        {
            ActiveArmy.SetIdle();

            // Cards are already added in CoSpawnPlayerArmy. 
            // We just activate them.
            if (_startGameRoutine != null)
            {
                StopCoroutine(_startGameRoutine);
                _startGameRoutine = null;
            }
            _startGameRoutine = StartCoroutine(CoActivateAfterInitialCards(0f));
        }
        else
        {
            IsGameStarted = true;
        }
    }
    private void SpawnPlayerArmy(EraDataSO eraData)
    {
        var playerSpawnRect = mapGenerator != null ? mapGenerator.GetSpawnPlayerTransform() : null;
        if (playerSpawnRect == null) return;

        if (ActiveArmy != null)
        {
            Destroy(ActiveArmy.gameObject);
            ActiveArmy = null;
        }

        ActiveArmy = Instantiate(playerArmyPrefab, transform);
        ActiveArmy.transform.position = playerSpawnRect.position + Vector3.forward * TurnableSpawnOffset;
        ActiveArmy.transform.rotation = playerSpawnRect.rotation;

        if (mapGenerator != null)
        {
            mapGenerator.BindWheelTransform(ActiveArmy.BodyTransform);
        }

        CameraManager.Instance.SetPlayerTransform(ActiveArmy.BodyTransform);
        ActiveArmy.Initialize();

        var seedCards = (initialCards != null && initialCards.Count > 0)
            ? initialCards
            : BuildInitialWheelCardsFromRuntimeState();

        ActiveArmy.AddCards(seedCards, CardSpawnEffectType.DropWithoutAction);
        OptimizeRenderHierarchy(ActiveArmy.transform);
    }

    private List<CardSpawnRequestData> BuildInitialWheelCardsFromRuntimeState()
    {
        int cardCount = 1;
        int cardLevel = 1;

        if (DataManager.PlayerData != null && DataManager.PlayerData.WheelData != null)
        {
            cardCount = Mathf.Max(1, DataManager.PlayerData.WheelData.CardCount);
            cardLevel = Mathf.Max(1, DataManager.PlayerData.WheelData.CardLevel);
        }

        var result = new List<CardSpawnRequestData>(cardCount);
        for (int i = 0; i < cardCount; i++)
        {
            result.Add(new CardSpawnRequestData(cardLevel, 1, CardType.Character));
        }
        return result;
    }

    private IEnumerator CoActivateAfterInitialCards(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        bool shouldWaitForTap = waitForTapBeforeGameplay;
        if (shouldWaitForTap && autoStartIfTutorialMissing)
        {
            bool hasVisibleTutorial = LunaUIManager.Instance != null && LunaUIManager.Instance.IsTutorialVisible;
            if (!hasVisibleTutorial)
            {
                shouldWaitForTap = false;
            }
        }

        if (shouldWaitForTap)
        {
            Turnable?.SetIdle();
            ActiveArmy?.SetIdle();
            IsGameStarted = false;
        }
        else
        {
            Turnable?.SetActive();
            ActiveArmy?.SetActive();
            IsGameStarted = true;
        }

        _startGameRoutine = null;
    }

    /// <summary>
    /// Gọi khi wheel chạm FinishRaceTrigger - chuyển camera state
    /// </summary>
    public void BeginFinishRace()
    {
        CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.FollowPlayerBeforeWin);
    }

    /// <summary>
    /// Kết thúc game - Playable version (không tracking, không layers phức tạp)
    /// </summary>
    public void EndGame(bool isWin)
    {
        IsGameStarted = false;
        Turnable?.SetIdle();
        ActiveArmy?.SetIdle();
        EnemyProjectileSystem.ClearAllProjectiles();

        if (isWin && ActiveArmy != null)
        {
            ActiveArmy.PlayAnimationForAllUnits(AnimationType.ConveyorJump, 0f);
        }

        if (useCtaOnlyEndgameMode && isWin)
        {
            if (showMilestoneOnWin && TryPlayMilestone())
            {
                if (_endGameRoutine != null) StopCoroutine(_endGameRoutine);
                _endGameRoutine = StartCoroutine(CoFinishWinAfterMilestoneCtaOnly());
                return;
            }

            CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.Finish);

            var lunaUi = LunaUIManager.Instance;
            if (lunaUi != null)
                lunaUi.ShowCtaOnlyEndgame();
            else
                GameEventBus.OnShowCTA?.Invoke();

            // Spawn a fresh player at the start position for this mode.
            if (playableEra != null)
                SpawnPlayerArmy(playableEra);

            return;
        }

        if (!disableEndGameCameraSwitch)
        {
            if (isWin)
            {
                CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.Finish);
            }
            else
            {
                CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.LoseState);
            }
        }

        if (isWin)
        {
            if (showMilestoneOnWin && TryPlayMilestone())
            {
                if (_endGameRoutine != null) StopCoroutine(_endGameRoutine);
                _endGameRoutine = StartCoroutine(CoFinishWinAfterMilestone());
                return;
            }

            ExecuteWinEndFlow();
        }
        else
        {
            GameEventBus.OnGameEnd?.Invoke(false);
        }
    }

    public void SetMilestoneOverridePosition(Vector3 worldPos)
    {
        _hasMilestoneOverride = true;
        _milestoneWorldPosOverride = worldPos;
    }

    private bool TryPlayMilestone()
    {
        if (_currentMilestone == null || contentGenerator == null) return false;
        if (_hasMilestoneOverride)
        {
            float positionOnMap = _milestoneWorldPosOverride.z - contentGenerator.Position.z;
            contentGenerator.SetPositionOnMap(_currentMilestone.transform, positionOnMap);
            _currentMilestone.PlayAnimOpen();
            _hasMilestoneOverride = false;
            return true;
        }

        if (contentGenerator.MilestonePoints == null || contentGenerator.MilestonePoints.Count == 0) return false;

        float maxPos = float.MinValue;
        foreach (var p in contentGenerator.MilestonePoints)
        {
            if (p > maxPos) maxPos = p;
        }

        if (maxPos <= float.MinValue) return false;

        contentGenerator.SetPositionOnMap(_currentMilestone.transform, maxPos);
        _currentMilestone.PlayAnimOpen();
        return true;
    }

    private IEnumerator CoFinishWinAfterMilestone()
    {
        float delay = Mathf.Max(0f, milestoneEndcardDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        ExecuteWinEndFlow();
        _endGameRoutine = null;
    }

    private IEnumerator CoFinishWinAfterMilestoneCtaOnly()
    {
        float delay = Mathf.Max(0f, milestoneEndcardDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.Finish);

        var lunaUi = LunaUIManager.Instance;
        if (lunaUi != null)
            lunaUi.ShowCtaOnlyEndgame();
        else
            GameEventBus.OnShowCTA?.Invoke();

        // Spawn a fresh player at the start position for this mode.
        if (playableEra != null)
        {
            if (ActiveArmy != null) ActiveArmy.ClearUnits(true);
            SpawnPlayerArmy(playableEra);
        }

        _endGameRoutine = null;
    }

    private void ExecuteWinEndFlow()
    {
        if (!_endGameSfxPlayed && SoundManager.Instance != null)
        {
            var sfx = winEndcardSfx != AudioClipName.None ? winEndcardSfx : AudioClipName.SFX_Level_Complete;
            if (sfx != AudioClipName.None)
            {
                SoundManager.Instance.PlayOneShot(sfx);
            }
            _endGameSfxPlayed = true;
        }
        GameEventBus.OnGameEnd?.Invoke(true);
        GameEventBus.OnShowCTA?.Invoke();
    }

    /// <summary>
    /// Kết thúc game - không có parameter (cho backward compatibility)
    /// </summary>
    public void EndGame()
    {
        EndGame(true);
    }

    public void OnCashTowerDestroyed()
    {
        EndGame(true);
    }

    public void PauseGame()
    {
        Turnable?.SetIdle();
        ActiveArmy?.SetIdle();
    }

    public void ContinueGame()
    {
        IsGameStarted = true;
        Turnable?.SetActive();
        ActiveArmy?.SetActive();
    }

    #endregion

    #region Modifier
    public void ApplySwordSkillBuff(CardSystem.Data.BuffDefinition buffDef)
    {
        if (buffDef == null) return;
        AcquiredSwordSkills.Add(buffDef.BuffId);
        if (!ActiveSamuraiBuffs.Contains(buffDef))
        {
            ActiveSamuraiBuffs.Add(buffDef);
        }
        // Có thể spawn UI thông báo buff
    }

    public void ChangeStatModifierData<TData>(TData statModifierData) where TData : StatModifierData
    {
        if (statModifierData == null) return;
        if (statModifierData.Type == StatType.None || statModifierData.Armor > 0) return;

        MarkPrimaryBuffAppliedIfNeeded(statModifierData);

        switch (statModifierData.Type)
        {
            case StatType.FireRate:
                {
                    int upgradeSteps = ResolveUpgradeSteps(statModifierData);
                    gamePlayVariable.ChangeFireRateVariable(upgradeSteps);
                    if (ActiveArmy != null)
                    {
                        ActiveArmy.ApplyFireRateModifier(upgradeSteps);
                    }
                    break;
                }

            case StatType.FireRange:
                {
                    int upgradeSteps = ResolveUpgradeSteps(statModifierData);
                    gamePlayVariable.ChangeFireRangeVariable(upgradeSteps);
                    if (ActiveArmy != null)
                    {
                        ActiveArmy.ApplyFireRangeModifier(upgradeSteps);
                    }
                    break;
                }

            case StatType.Damage:
                {
                    CapacityIncreaseGateData gateDamageData = statModifierData as CapacityIncreaseGateData;

                    if (gateDamageData == null)
                        break;

                    int damageValue = Mathf.Max(0, gateDamageData.Value);
                    if (damageValue <= 0)
                        break;

                    if (ActiveArmy != null)
                    {
                        ActiveArmy.ApplyDamageModifier(damageValue);
                    }
                    break;
                }

            case StatType.Character:
                {
                    CapacityIncreaseGateData gateData = statModifierData as CapacityIncreaseGateData;

                    if (gateData != null)
                    {
                        if (gateData.ElementDataList != null &&
                            gateData.ElementDataList.Count > 0 &&
                            gateData.UpgradeSteps > 0)
                        {
                            AddCharacterCardsFromGate(gateData, CardSpawnEffectType.Drop);
                        }
                        else
                        {
                            Debug.LogWarning("[GameplayManager] Character upgrade gate resolved with no valid upgrade step.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[GameplayManager] Unknown StatModifierData type for Character: " + statModifierData.GetType().Name);
                    }
                    break;
                }

            case StatType.CharacterLevel:
                {
                    int levelBonus = ResolveUpgradeSteps(statModifierData);

                    if (levelBonus > 0 && ActiveArmy != null)
                    {
                        ActiveArmy.UpgradeAllUnitsToLevel(levelBonus);
                    }

                    break;
                }

            case StatType.MoveSpeed:
                {
                    gamePlayVariable.ChangeMoveSpeedVariable(statModifierData.Value);
                    break;
                }

            case StatType.EvolutionPoint:
                {
                    gamePlayVariable.ChangeEvolutionPointVariable(statModifierData.Value);
                    break;
                }

            case StatType.ExplosionShot:
                {
                    CapacityIncreaseGateData explosionData = statModifierData as CapacityIncreaseGateData;

                    if (explosionData == null)
                        break;

                    int configuredPercent = Mathf.Max(0, explosionData.Value);
                    if (configuredPercent <= 0)
                        break;

                    _isExplosionShotUnlocked = true;
                    _explosionShotDamagePercent = Mathf.Max(_explosionShotDamagePercent, configuredPercent);
                    break;
                }
        }
    }
    public bool CanOfferExplosionShotThisRun()
    {
        return !_hasOfferedExplosionShotThisRun;
    }

    public bool HasAppliedPrimaryBuffThisRun(StatType statType)
    {
        return _appliedPrimaryBuffTypes.Contains(statType);
    }

    public void MarkExplosionShotOffered()
    {
        _hasOfferedExplosionShotThisRun = true;
    }

    private void MarkPrimaryBuffAppliedIfNeeded(StatModifierData statModifierData)
    {
        if (statModifierData == null)
        {
            return;
        }

        if (!IsPrimaryBuffType(statModifierData.Type))
        {
            return;
        }

        int upgradeSteps = ResolveUpgradeSteps(statModifierData);
        if (upgradeSteps <= 0)
        {
            return;
        }

        _appliedPrimaryBuffTypes.Add(statModifierData.Type);
    }

    private static bool IsPrimaryBuffType(StatType statType)
    {
        return statType == StatType.FireRate ||
               statType == StatType.Character ||
               statType == StatType.Damage;
    }

    private static int ResolveUpgradeSteps(StatModifierData statModifierData)
    {
        if (statModifierData is CapacityIncreaseGateData gateData)
        {
            return Mathf.Max(0, gateData.UpgradeSteps);
        }

        return Mathf.Max(0, statModifierData.Value);
    }

    public void ResetStatModifierData(StatType statType)
    {
        if (statType is StatType.None) return;

        if (statType == StatType.MoveSpeed)
            gamePlayVariable.ResetWheelVariable_MoveSpeed();
    }

    /// <summary>
    /// Called by WeaponCraftSystem when the leading weapon changes (new craft or merge result).
    /// Stores the new main weapon and applies it to active gameplay systems.
    /// </summary>
    /// <param name="weapon">The new top-tier weapon produced by the craft system.</param>
    public void SetMainWeapon(WeaponCraft.WeaponItem weapon)
    {
        _mainWeapon = weapon;
        OnWeaponChange?.Invoke(weapon);
    }

    private void AddCardsToPlayer(List<CardSpawnRequestData> cards, CardSpawnEffectType effect)
    {
        if (IsArmyMode)
            ActiveArmy?.AddCards(cards, effect);
        else
            Turnable?.AddCards(cards, effect);
    }

    private void AddCharacterCardsFromGate(CapacityIncreaseGateData gateData, CardSpawnEffectType effect)
    {
        if (gateData == null || gateData.ElementDataList == null || gateData.ElementDataList.Count == 0)
        {
            return;
        }

        int upgradeSteps = Mathf.Max(0, gateData.UpgradeSteps);
        if (upgradeSteps <= 0)
        {
            return;
        }

        IncreaseElementData selectedData = gateData.ElementDataList[0];
        int cardsPerStep = Mathf.Max(1, selectedData.Value);
        int totalCards = cardsPerStep * upgradeSteps;

        _singleRequestBuffer.Clear();
        _singleRequestBuffer.Add(new CardSpawnRequestData
        {
            Amount = totalCards,
            Level = IsArmyMode ? -1 : 1,
            CardType = CardType.Character
        });
        AddCardsToPlayer(_singleRequestBuffer, effect);
    }

    private void EnsureWeaponCraftStarterItem()
    {
        var craftSystem = WeaponCraft.WeaponCraftSystem.Instance;
        if (craftSystem == null)
        {
            return;
        }

        if (craftSystem.Items == null || craftSystem.Items.Count == 0)
        {
            craftSystem.EnsureStarterItem();
        }
    }

    public void AddCapacityCoinToPool(int amount)
    {
        int safeAmount = Mathf.Max(0, amount);
        if (safeAmount <= 0) return;
        StartCoin += safeAmount;
        StartCoinPending += safeAmount;
    }

    public int ConsumeCapacityCoinPool()
    {
        // StartCoin is the source-of-truth total.
        // StartCoinPending is a subset (in-flight/visual pending), not an additional amount.
        int total = Mathf.Max(0, StartCoin);
        StartCoin = 0;
        StartCoinPending = 0;
        return total;
    }

    public int GetGoldGateRewardPerProgressTick(int baseReward = 3)
    {
        int safeBase = Mathf.Max(0, baseReward);
        int capacity = 1;
        if (gamePlayVariable != null && gamePlayVariable.EvolutionVariable != null)
        {
            capacity = Mathf.Max(1, gamePlayVariable.EvolutionVariable.Capacity);
        }

        return safeBase + capacity;
    }

    private static void OptimizeRenderHierarchy(Transform root)
    {
        // if (root == null) return;

        // var renderers = root.GetComponentsInChildren<Renderer>(true);
        // for (int i = 0; i < renderers.Length; i++)
        // {
        //     var renderer = renderers[i];
        //     if (renderer == null) continue;

        //     renderer.shadowCastingMode = ShadowCastingMode.Off;
        //     renderer.receiveShadows = false;
        //     renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        //     renderer.lightProbeUsage = LightProbeUsage.Off;
        //     renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        //     if (renderer is SkinnedMeshRenderer skinned)
        //     {
        //         // Luna compatibility: some runtimes strip SkinnedMeshRenderer members.
        //         TrySetSkinnedProperties(skinned);
        //     }
        // }
    }

    private static void TrySetSkinnedProperties(SkinnedMeshRenderer skinned)
    {
        if (skinned == null) return;

        try
        {
            if (SkinnedQualityProperty != null && SkinnedQualityProperty.CanWrite)
                SkinnedQualityProperty.SetValue(skinned, SkinQuality.Bone2, null);
            if (SkinnedMotionVectorsProperty != null && SkinnedMotionVectorsProperty.CanWrite)
                SkinnedMotionVectorsProperty.SetValue(skinned, false, null);
            if (SkinnedUpdateWhenOffscreenProperty != null && SkinnedUpdateWhenOffscreenProperty.CanWrite)
                SkinnedUpdateWhenOffscreenProperty.SetValue(skinned, false, null);
        }
        catch
        {
            // Ignore: optimization only.
        }
    }


    #endregion
}


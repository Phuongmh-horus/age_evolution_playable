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

public class GameplayManager : MonoSingleton<GameplayManager>, GamePlay.Managers.IGameplayFlow
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

    [Header("Playable Config")]
    [SerializeField] private bool activeTurnable = true;
    [SerializeField] private bool disableEndGameCameraSwitch = true;
    [SerializeField] private bool useCtaOnlyEndgameMode = false;
    [SerializeField] private List<GamePlay.Crushers.CardSpawnRequestData> initialCards; // Configurable via Inspectornerator;
    [SerializeField] private AudioClipName winEndcardSfx = AudioClipName.SFX_Level_Complete;

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
    [SerializeField] private int initItemsPerFrame = 12;
    [SerializeField] private int spawnItemsPerFrame = 20;

    [Header("Startup Flow")]
    [SerializeField] private bool waitForTapBeforeGameplay = true;
    [SerializeField] private bool autoStartIfTutorialMissing = true;

    [Header("Milestone (Playable)")]
    [SerializeField] private bool showMilestoneOnWin = true;
    [SerializeField] private float milestoneEndcardDelay = 1.0f;

    public static bool IsGameStarted;
    private bool _endGameSfxPlayed;
    private WeaponCraft.WeaponItem _mainWeapon;
    private Dictionary<CurrencyType, int> _currencyValues = new Dictionary<CurrencyType, int>();
    public WeaponCraft.WeaponItem MainWeapon => _mainWeapon;
    public UnityAction<WeaponCraft.WeaponItem> OnWeaponChange;
    public UnityAction<CurrencyType, int, Vector3> OnCurrencyChanged;

    public int GetCurrency(CurrencyType type)
    {
        _currencyValues.TryGetValue(type, out int val);
        return val;
    }

    public void AddCurrency(CurrencyType type, int amount, Vector3 worldPosition)
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
    private MilestoneOnMap _currentMilestone;
    private bool _hasMilestoneOverride;
    private Vector3 _milestoneWorldPosOverride;

    public Transform PlayerTransform => IsArmyMode && ActiveArmy != null ? ActiveArmy.BodyTransform : Turnable != null ? Turnable.Transform : null;

    bool GamePlay.Managers.IGameplayFlow.IsGameStarted => IsGameStarted;

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

    private void Start()
    {
        DataManager.InitData();
        // Auto boot playable
        StartCoroutine(CoBootPlayable());
    }

    private IEnumerator CoBootPlayable()
    {
        ClearRuntimeTickCaches();
        DataManager.ResetToDefault();
        yield return StartCoroutine(CoLoadPlayableLevel());
        yield return StartCoroutine(CoInitializeGeneratedContent());
        StartGame();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Playable/Luna: be defensive (systems may be missing in some stripped builds)
        HitTextFlyEffect.TickActiveControllers(dt);
        DeathScaleEffect.TickActiveEffects(dt);
        BrickFallMotion.TickActiveMotions(dt);
        CurrencyDropItem.TickActiveDrops(dt);
        if (!IsGameStarted) return;

        OscillationSystem.Instance?.ManualUpdate();
        CombatSystem.Instance?.ManualUpdate();
    }

    public void RunUpgradeEffect()
    {
        ActiveArmy.PlayEffect(EffectType.Land, ActiveArmy.transform);
    }

    // Stub removed to allow generic ChangeStatModifierData to handle EvolutionPoint logic.

    #region Load Playable Level

    public void ReloadLevel()
    {
        StartCoroutine(CoReload());
    }

    private IEnumerator CoReload()
    {
        IsGameStarted = false;
        ClearRuntimeTickCaches();
        DataManager.ResetToDefault();

        yield return StartCoroutine(CoLoadPlayableLevel());
        yield return StartCoroutine(CoInitializeGeneratedContent());

        CameraManager.Instance.SetCameraStateByName(CameraFollow.CameraStateName.Waiting, CameraFollow.TransitionMode.Instant);

        StartGame();
    }

    private static void ClearRuntimeTickCaches()
    {
        CurrencyDropItem.ClearActiveDrops();
        DeathScaleEffect.ClearAll();
    }

    private IEnumerator CoLoadPlayableLevel()
    {
        if (playableEra == null || playableContent == null)
        {
            Debug.LogError($"[GameplayManager] playableEra({playableEra == null})/playableContent({playableContent == null}) is null. Drag vào Inspector.");
            yield break;
        }

        bool hasPrebakedMap = mapGenerator != null &&
                              mapGenerator.GetActiveSegments().Count > 0;

        // Luna/Playable: Ưu tiên sử dụng pre-baked map từ scene
        bool shouldRegenerateMap = !hasPrebakedMap ||
                                   (mapGenerator != null &&
                                    mapGenerator.CurrentMapData != null &&
                                    mapGenerator.CurrentMapData != playableEra.MapData);

        if (shouldRegenerateMap)
        {
            mapGenerator.GenerateMap(playableEra.MapData);
        }

        if (IsArmyMode)
            yield return StartCoroutine(CoSpawnPlayerArmy(playableEra));
        else
            yield return StartCoroutine(CoSpawnTurnTable(playableEra));

        if (EnemyManager.Instance != null) EnemyManager.Instance.UnregisterAllEnemies(); // Safe check?
        else Debug.LogWarning("[GameplayManager] EnemyManager.Instance is NULL!");

        EnemyProjectileSystem.UnregisterPlayer();

        yield return null;

        if (contentGenerator != null)
        {
            // Luna/Playable: Ưu tiên sử dụng pre-baked content từ scene
            // Chỉ generate từ ScriptableObject nếu không có pre-baked content
            if (contentGenerator.HasPrebakedContent())
            {
                contentGenerator.UsePrebakedContent(initializeItems: false);
            }
            else
            {
                yield return StartCoroutine(contentGenerator.GenerateContentDataAsync(
                    playableContent,
                    playableTowerZoneContent,
                    initializeItems: false,
                    customBatchSize: Mathf.Max(1, spawnItemsPerFrame)
                ));
            }
        }
        else
        {
            Debug.LogError("[GameplayManager] contentGenerator is NULL!");
        }


        // Setup camera preview points
        var trackPreview = CameraManager.Instance.GetCameraFollow()
            .GetStateByName(CameraFollow.CameraStateName.TrackPreview) as TrackPreviewCameraState;
        if (trackPreview && mapGenerator.activeSegments != null && mapGenerator.activeSegments.Count > 0)
        {
            trackPreview.startPoint = mapGenerator.activeSegments[0].EntryPoint;
            trackPreview.endPoint = mapGenerator.activeSegments[mapGenerator.activeSegments.Count - 1].ExitPoint;
        }

        // Setup finish view target
        var finishView = CameraManager.Instance.GetCameraFollow()
            .GetStateByName(CameraFollow.CameraStateName.Finish) as StaticCameraState;
        if (finishView && contentGenerator.GateNewEraTrans)
        {
            finishView.SetTargetTransform(contentGenerator.GateNewEraTrans);
        }

        // Setup milestone flag (playable)
        if (_currentMilestone != null)
        {
            _currentMilestone.Despawn();
            _currentMilestone = null;
        }

        if (playableEra.Milestone != null && contentGenerator != null)
        {
            _currentMilestone = contentGenerator.SpawnMilestoneItem(playableEra.Milestone);
            if (_currentMilestone != null)
            {
                _currentMilestone.gameObject.SetActive(false);
            }
        }
    }

    private IEnumerator CoInitializeGeneratedContent()
    {
        if (contentGenerator == null) yield break;

        var items = contentGenerator.generatedObjects;
        if (items == null || items.Count == 0)
        {
            yield break;
        }

        int batchSize = Mathf.Max(1, initItemsPerFrame);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item != null)
                item.Initialize();

            if ((i + 1) % batchSize == 0)
                yield return null;
        }

    }

    private IEnumerator CoSpawnTurnTable(EraDataSO eraData)
    {
        var playerSpawnRect = mapGenerator.GetSpawnPlayerTransform();
        if (playerSpawnRect == null) yield break;

        var turntable = eraData.Turntable;
        if (turntable == null || turntable.TurntablePrefab == null) yield break;

        if (Turnable)
        {
            Destroy(Turnable.gameObject);
            Turnable = null;
            yield return null;
        }

        Turnable = Instantiate(turntable.TurntablePrefab, transform);
        Turnable.Transform.position = playerSpawnRect.position + Vector3.forward * TurnableSpawnOffset;
        Turnable.Transform.rotation = playerSpawnRect.rotation;

        if (mapGenerator != null)
        {
            var wheelTransform = Turnable.fullBody != null ? Turnable.fullBody : Turnable.Transform;
            mapGenerator.BindWheelTransform(wheelTransform);
        }

        Turnable.SetIdle();
        EnemyProjectileSystem.RegisterPlayer(Turnable);

        if (followHorizontal) CameraManager.Instance.SetPlayerTransform(Turnable.fullBody);
        else CameraManager.Instance.SetPlayerTransform(Turnable.Transform);
    }

    private IEnumerator CoSpawnPlayerArmy(EraDataSO eraData)
    {
        var playerSpawnRect = mapGenerator.GetSpawnPlayerTransform();
        if (playerSpawnRect == null) yield break;

        if (ActiveArmy != null)
        {
            Destroy(ActiveArmy.gameObject);
            ActiveArmy = null;
            yield return null;
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
        ActiveArmy.SetIdle();
    }

    #endregion

    #region Start/End Game (Playable)

    public void StartGame(bool activeTurnable = true)
    {
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
        ConveyorManager.Instance.SetGatePositions(contentGenerator.generatedObjects);

        // Reset wheel/character variables
        gamePlayVariable?.ResetCharacterVariable();
        gamePlayVariable?.ResetWheelVariable();
        ResetCurrency(CurrencyType.Gold);
        ResetCurrency(CurrencyType.Cash);
        GameEventBus.UpdateCapacityBar?.Invoke();

        EnsureWeaponCraftStarterItem();

        IsGameStarted = false;

        if (activeTurnable)
        {
            if (IsArmyMode && ActiveArmy != null)
            {
                ActiveArmy.SetIdle();

                var seedCards = (initialCards != null && initialCards.Count > 0)
                    ? initialCards
                    : BuildInitialWheelCardsFromRuntimeState();
                ActiveArmy.AddCards(seedCards, CardSpawnEffectType.DropWithoutAction);

                if (_startGameRoutine != null)
                {
                    StopCoroutine(_startGameRoutine);
                    _startGameRoutine = null;
                }
                _startGameRoutine = StartCoroutine(CoActivateAfterInitialCards(0f));
            }
            else if (Turnable != null)
            {
                Turnable.SetIdle();

                // Seed initial cards: prefer runtime wheel state unless Inspector explicitly overrides.
                var seedCards = (initialCards != null && initialCards.Count > 0)
                    ? initialCards
                    : BuildInitialWheelCardsFromRuntimeState();
                // Spawn initial cards without slow-motion / per-card delay.
                Turnable.AddCards(seedCards, CardSpawnEffectType.DropWithoutAction);

                float spawnDelay = GetInitialCardSpawnDelay(seedCards, includeDropAnimation: false);
                if (_startGameRoutine != null)
                {
                    StopCoroutine(_startGameRoutine);
                    _startGameRoutine = null;
                }
                _startGameRoutine = StartCoroutine(CoActivateAfterInitialCards(spawnDelay));
            }
            else
            {
                Debug.LogError("[GameplayManager] Turnable is NULL! Cannot activate wheel.");
                IsGameStarted = true;
            }
        }
        else
        {
            IsGameStarted = true;
        }
    }

    private float GetInitialCardSpawnDelay(List<CardSpawnRequestData> requests, bool includeDropAnimation = true)
    {
        if (!includeDropAnimation) return 0f;
        if (requests == null || requests.Count == 0) return 0f;

        int totalCards = 0;
        for (int i = 0; i < requests.Count; i++)
        {
            totalCards += Mathf.Max(0, requests[i].Amount);
        }

        if (totalCards <= 0) return 0f;

        float delayPerCard = 0.05f;
        float dropDuration = 0.3f;
        if (gamePlayVariable != null && gamePlayVariable.WheelVariable != null)
        {
            delayPerCard = gamePlayVariable.WheelVariable.DelayPerCard;
            dropDuration = gamePlayVariable.WheelVariable.DropDuration;
        }

        return Mathf.Max(0f, (totalCards - 1) * delayPerCard + dropDuration);
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
                StartCoroutine(IsArmyMode ? CoSpawnPlayerArmy(playableEra) : CoSpawnTurnTable(playableEra));

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
            StartCoroutine(IsArmyMode ? CoSpawnPlayerArmy(playableEra) : CoSpawnTurnTable(playableEra));

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
        Turnable?.SetActive();
        ActiveArmy?.SetActive();
    }

    #endregion

    #region Modifier

    public void ChangeStatModifierData<TData>(TData statModifierData) where TData : StatModifierData
    {
        if (statModifierData == null) return;
        if (statModifierData.Type is StatType.None || statModifierData.Armor > 0) return;

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

            case StatType.Character:
                {
                    if (statModifierData is CapacityIncreaseGateData gateData)
                    {
                        if (gateData.ElementDataList != null && gateData.ElementDataList.Count > 0 && gateData.UpgradeSteps > 0)
                        {
                            AddCharacterCardsFromGate(gateData, CardSpawnEffectType.Drop);
                        }
                        else
                        {
                            Debug.LogWarning("[GameplayManager] Character upgrade gate resolved with no valid upgrade step.");
                        }
                    }
                    else if (statModifierData is CapacityIncreaseFactoryData factoryData)
                    {
                        int gain = Mathf.Max(1, factoryData.Value);
                        AddCapacityCoinToPool(gain);

                        _singleRequestBuffer.Clear();
                        _singleRequestBuffer.Add(new CardSpawnRequestData { Amount = factoryData.Value, Level = factoryData.Level });
                        AddCardsToPlayer(_singleRequestBuffer, CardSpawnEffectType.Drop);
                    }
                    else
                    {
                        Debug.LogWarning($"[GameplayManager] Unknown StatModifierData type for Character: {statModifierData.GetType().Name}");
                    }
                    break;
                }

            case StatType.CharacterLevel:
                {
                    int levelBonus = ResolveUpgradeSteps(statModifierData);
                    if (levelBonus > 0)
                    {
                        ActiveArmy?.UpgradeAllUnitsToLevel(levelBonus);
                    }

                    break;
                }

            case StatType.MoveSpeed:
                gamePlayVariable.ChangeMoveSpeedVariable(statModifierData.Value);
                break;

            case StatType.EvolutionPoint:
                gamePlayVariable.ChangeEvolutionPointVariable(statModifierData.Value);
                break;
        }
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

    private void AddCardsToPlayer(List<IncreaseElementData> elementDataList, CardSpawnEffectType effect)
    {
        if (elementDataList == null || elementDataList.Count == 0) return;

        bool isArmyMode = IsArmyMode;
        var cardRequests = new List<CardSpawnRequestData>(elementDataList.Count);
        for (int i = 0; i < elementDataList.Count; i++)
        {
            var data = elementDataList[i];
            cardRequests.Add(new CardSpawnRequestData
            {
                Amount = data.Value,
                Level = isArmyMode ? -1 : 1,
                CardType = CardType.Character
            });
        }
        AddCardsToPlayer(cardRequests, effect);
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
            craftSystem.ReceiveItem(1, transform.position);
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
        int safeBase = Mathf.Max(1, baseReward);
        int capacity = 1;
        if (gamePlayVariable != null && gamePlayVariable.EvolutionVariable != null)
        {
            capacity = Mathf.Max(1, gamePlayVariable.EvolutionVariable.Capacity);
        }

        return safeBase + capacity;
    }


    #endregion
}

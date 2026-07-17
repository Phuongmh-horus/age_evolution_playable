using System;
using System.Collections.Generic;
using GamePlay.Entities;
using GamePlay.Items;
using UnityEngine;
using Random = UnityEngine.Random;

public class CurrencyDropItem : ItemUnit
{
    private static readonly List<CurrencyDropItem> s_activeDrops = new List<CurrencyDropItem>(128);
    public CurrencyType Type;
    public float Amount;

    [Header("Playable Settings")]
    [Tooltip("Nếu true: chạm đất sẽ tự claim và despawn.")]
    [SerializeField] private bool autoClaimOnGround = true;

    [Tooltip("Giữ nguyên Amount hoặc random thêm (playable dễ tùy biến).")]
    [SerializeField] private Vector2 randomBonusRange = Vector2.zero;

    [Tooltip("Thời gian delay (giây) trước khi claim (bay lên panel) sau khi chạm đất.")]
    [SerializeField] private float claimDelayOnGround = 0.5f;


    // [FIX] Cache loaded clip for Luna (Resources.Load can be slow/fail on repeated calls)
    private static AudioClip _cachedMoneyClip;

    // Physics
    private Vector3 _initialVelocity;
    private float _gravity = 20f;
    [SerializeField] private float groundY = 0f;

    private bool _isSimulating;
    private bool _isWaitingToClaim;
    private float _claimTimer;
    private bool _registeredForTick;
    private Vector3 _simVelocity;
    private Vector3 _simPosition;

    public bool canClaim;

    /// <summary>
    /// Hook cho playable: bên ngoài có thể nghe để update UI fake, counter, v.v.
    /// (Không phụ thuộc DataManager/Reward/GameplayManager)
    /// </summary>
    public static event Action<CurrencyType, int> OnClaimed;

    public override void Initialize()
    {
        base.Initialize();
        canClaim = true;
    }

    public static void TickActiveDrops(float dt)
    {
        if (s_activeDrops.Count == 0) return;

        for (int i = s_activeDrops.Count - 1; i >= 0; i--)
        {
            var item = s_activeDrops[i];
            if (item == null || !item._isSimulating || !item.gameObject.activeInHierarchy)
            {
                if (item != null) item._registeredForTick = false;
                RemoveAtSwapBack(i);
                continue;
            }

            if (!item.StepSimulation(dt))
            {
                item._registeredForTick = false;
                RemoveAtSwapBack(i);
            }
        }
    }

    private void Awake()
    {
        if (_entityType == EntityType.None)
        {
            _entityType = EntityType.Item;
        }

        if (Type == 0)
        {
            Type = CurrencyType.Gold;
        }

    }

    protected override void HandleWheelCollision()
    {
        base.HandleWheelCollision();
        ClaimReward();
    }

    public void ClaimReward()
    {
        if (!canClaim) return;

        canClaim = false;

        // [FIX] Sound - Luna-safe with multiple fallbacks
        PlayClaimSound();

        int amountInt = Mathf.CeilToInt(Amount);

        var gameplayManager = GameplayManager.Instance;
        if (gameplayManager != null)
        {
            gameplayManager.AddCurrency(Type, amountInt, transform.position);
        }

        OnClaimed?.Invoke(Type, amountInt);

        // Base item flow (giữ logic despawn của bạn)
        DespawnInterval();
    }

    private void PlayClaimSound()
    {

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayOneShot(AudioClipName.SFX_MoneyCollect);
            return;
        }

        var cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetCamera() : null;
        var pos = cam != null ? cam.transform.position : transform.position;

    }

    /// <summary>
    /// Playable-safe init:
    /// - Không cộng income theo era/config/save.
    /// - Có random bonus để tùy biến.
    /// - Nếu flyUp = true thì mô phỏng rơi với gravity bằng Coroutine.
    /// </summary>
    public void Initialize(Vector3 initialVelocity, float value, bool flyUp = false)
    {
        canClaim = true;

        _initialVelocity = initialVelocity;

        Amount = value;
        if (randomBonusRange != Vector2.zero)
            Amount += Random.Range(randomBonusRange.x, randomBonusRange.y);

        // Reset rotation về zero để nhất quán
        transform.rotation = Quaternion.Euler(Vector3.zero);

        // Đảm bảo y không < groundY
        var p = transform.position;
        if (p.y < groundY)
        {
            p.y = groundY;
            transform.position = p;
        }

        StopSimulation();

        if (flyUp)
        {
            _isSimulating = true;
            _isWaitingToClaim = false;
            _simVelocity = _initialVelocity;
            _simPosition = transform.position;
            RegisterActiveDrop();
        }
    }

    public void SetAutoClaimOnGround(bool value)
    {
        autoClaimOnGround = value;
    }

    public void SetClaimType(CurrencyType type)
    {
        Type = type;
    }

    public void SetGroundY(float value)
    {
        groundY = value;
    }

        private bool StepSimulation(float dt)
    {
        if (_isWaitingToClaim)
        {
            _claimTimer -= dt;
            if (_claimTimer <= 0f)
            {
                ClaimReward();
                return false; // Stop ticking after claim
            }
            return true;
        }

        _simVelocity.y -= _gravity * dt;
        _simPosition += _simVelocity * dt;

        if (_simPosition.y <= groundY)
        {
            _simPosition.y = groundY;
            transform.position = _simPosition;
            transform.rotation = Quaternion.Euler(Vector3.zero);
            
            if (autoClaimOnGround)
            {
                if (claimDelayOnGround > 0f)
                {
                    _isWaitingToClaim = true;
                    _claimTimer = claimDelayOnGround;
                    return true;
                }
                else
                {
                    ClaimReward();
                    return false;
                }
            }

            _isSimulating = false;
            return false;
        }

        transform.position = _simPosition;
        return true;
    }

    

    private void StopSimulation()
    {
        _isSimulating = false;
    }

    private void OnDisable()
    {
        StopSimulation();
        _registeredForTick = false;
    }

    private void OnDestroy()
    {
        StopSimulation();
    }

    private void RegisterActiveDrop()
    {
        if (_registeredForTick) return;
        _registeredForTick = true;
        s_activeDrops.Add(this);
    }

    private static void RemoveAtSwapBack(int index)
    {
        int last = s_activeDrops.Count - 1;
        if (index < 0 || index > last) return;
        s_activeDrops[index] = s_activeDrops[last];
        s_activeDrops.RemoveAt(last);
    }

    public static void ClearActiveDrops()
    {
        s_activeDrops.Clear();
        s_activeDrops.TrimExcess();
    }
}

[Serializable]
public enum CurrencyType
{
    Gold = 1,
    Cash = 3,
    Gem = 5,
    Diamond = 7
}


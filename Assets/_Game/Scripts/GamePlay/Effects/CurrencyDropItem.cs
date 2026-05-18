using System;
using GamePlay.Entities;
using GamePlay.Items;
using UnityEngine;
using Random = UnityEngine.Random;

public class CurrencyDropItem : ItemUnit
{
    public CurrencyType Type;
    public float Amount;

    [Header("Playable Settings")]
    [Tooltip("Nếu true: chạm đất sẽ tự claim và despawn.")]
    [SerializeField] private bool autoClaimOnGround = true;

    [Tooltip("Giữ nguyên Amount hoặc random thêm (playable dễ tùy biến).")]
    [SerializeField] private Vector2 randomBonusRange = Vector2.zero;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip claimClip; // [FIX] Direct reference for Luna
    [SerializeField] private AudioClipName claimSfx;
    [SerializeField] private AudioSource ownAudioSource;

    // [FIX] Cache loaded clip for Luna (Resources.Load can be slow/fail on repeated calls)
    private static AudioClip _cachedMoneyClip;

    // Physics
    private Vector3 _initialVelocity;
    private float _gravity = 20f;
    [SerializeField] private float groundY = 0f;

    private bool _isSimulating;
    private Coroutine _simRoutine;

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

    private void Awake()
    {
        if (_entityType == EntityType.None)
        {
            _entityType = EntityType.Item;
        }

        if ((int)Type == 0)
        {
            Type = CurrencyType.Gold;
        }

        // [FIX] Luna Audio Robustness:
        // AudioSource must be assigned via Inspector to avoid runtime auto-find.
        if (ownAudioSource == null)
        {
            Debug.LogWarning($"[CurrencyDropItem] Missing AudioSource on {name}. Assign in Inspector.");
        }

        // 2. Aggressively cache the clip
        if (claimClip == null)
        {
             // Try to load cached static first
             if (_cachedMoneyClip == null)
             {
                 _cachedMoneyClip = Resources.Load<AudioClip>("Sound/SFX_MoneyCollect");
             }
             claimClip = _cachedMoneyClip;
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
        var sfx = claimSfx != AudioClipName.None ? claimSfx : AudioClipName.SFX_MoneyCollect;
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayOneShot(sfx);
            return;
        }

        AudioClip clipToUse = claimClip;
        if (clipToUse == null)
        {
            if (_cachedMoneyClip == null) _cachedMoneyClip = Resources.Load<AudioClip>("Sound/SFX_MoneyCollect");
            clipToUse = _cachedMoneyClip;
        }

        if (clipToUse == null) return;

        if (ownAudioSource != null)
        {
            ownAudioSource.PlayOneShot(clipToUse, 1f);
            return;
        }

        var cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetCamera() : null;
        var pos = cam != null ? cam.transform.position : transform.position;
        AudioSource.PlayClipAtPoint(clipToUse, pos, 1f);
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
            _simRoutine = StartCoroutine(PhysicsSimRoutine());
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

    private System.Collections.IEnumerator PhysicsSimRoutine()
    {
        Vector3 currentVelocity = _initialVelocity;
        Vector3 currentPosition = transform.position;

        while (_isSimulating)
        {
            float dt = Time.deltaTime;

            // gravity
            currentVelocity.y -= _gravity * dt;

            // integrate
            currentPosition += currentVelocity * dt;

            // ground
            if (currentPosition.y <= groundY)
            {
                currentPosition.y = groundY;
                transform.position = currentPosition;

                transform.rotation = Quaternion.Euler(Vector3.zero);

                _isSimulating = false;
                _simRoutine = null;

                if (autoClaimOnGround)
                    ClaimReward();

                yield break;
            }

            transform.position = currentPosition;
            yield return null;
        }

        _simRoutine = null;
    }

    private void StopSimulation()
    {
        _isSimulating = false;
        if (_simRoutine != null)
        {
            StopCoroutine(_simRoutine);
            _simRoutine = null;
        }
    }

    private void OnDisable()
    {
        StopSimulation();
    }

    private void OnDestroy()
    {
        StopSimulation();
    }
}

[Serializable]
public enum CurrencyType
{
    Gold = 1,
    Cash = 3,
    Gem = 5,
    Diamond = 7,
    Coin = 15,
    CoinBuff = 16
}

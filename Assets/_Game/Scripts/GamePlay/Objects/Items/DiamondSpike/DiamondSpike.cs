using System.Collections;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Effects;
using DG.Tweening;
using GamePlay.HealthSystems;
using GamePlay.Items;
using UnityEngine;

public class DiamondSpike : StatModifierItem<StatModifierDiamondSpikeData>
{
    [Header("Spike References")]
    [SerializeField] protected HealthComponent healthComponent;
    [SerializeField] protected DropCurrencyEffect dropCurrencyEffect;

    [Header("Drop Settings")]
    [Tooltip("Danh sách các mảnh (visuals) hiển thị trên spike. Số lượng mảnh = Số lượng drop.")]
    [SerializeField] protected Transform[] pieceVisuals;
    [Tooltip("Số lượng currency rớt ra cho MỖI mảnh vỡ")]
    [SerializeField] protected int currencyPerPiece = 10;

    [Header("Death Animation")]
    [SerializeField] protected float scaleDownDuration = 0.15f;

    protected int _piecesReleased;
    protected bool _isDead;
    protected Vector3 _originalScale;

    protected override void Awake()
    {
        base.Awake();
        if (_entityType == GamePlay.Entities.EntityType.None || _entityType == GamePlay.Entities.EntityType.Item)
        {
            _entityType = GamePlay.Entities.EntityType.ResourceTower; // Giống Capacity Pillar
        }
        _originalScale = transform.localScale;
    }

    public override void Initialize()
    {
        base.Initialize();

        _piecesReleased = 0;
        _isDead = false;
        transform.localScale = _originalScale != Vector3.zero ? _originalScale : Vector3.one;

        if (healthComponent != null)
        {
            Pack.Healable = healthComponent;
            ActiveFlags |= CapabilityFlags.Heal;
            healthComponent.Initialize();
        }

        // Bật lại tất cả các mảnh visual
        if (pieceVisuals != null)
        {
            foreach (var piece in pieceVisuals)
            {
                if (piece != null) piece.gameObject.SetActive(true);
            }
        }

        RegisterEvents(true);
    }

    protected override void HandleNonWheelCollision(IAttacker source)
    {
        if (_isDead) return;
        base.HandleNonWheelCollision(source);

        // Gây sát thương lên HealthComponent
        Pack.Healable?.TakeDamage(source);
    }

    protected override void HandleWheelCollision()
    {
        if (_isDead) return;
        base.HandleWheelCollision();
        HandleHealthChange(0, 1);

    }

    protected override void HandleHealthChange(int current, int max)
    {
        if (_isDead) return;

        ProcessPieceDrops(current, max);

        if (current <= 0)
        {
            _isDead = true;
            transform.DOScale(Vector3.zero, scaleDownDuration).SetEase(Ease.InQuad).OnComplete(DespawnInterval);
        }
    }

    protected virtual void ProcessPieceDrops(int current, int max)
    {
        int totalPieces = pieceVisuals != null ? pieceVisuals.Length : 0;
        if (totalPieces <= 0) return;

        float hpPerPiece = (float)max / totalPieces;

        // Tính toán số phần máu còn lại
        int currentSegmentsAlive = Mathf.CeilToInt((float)current / hpPerPiece);
        int targetReleasedCount = totalPieces - currentSegmentsAlive;
        targetReleasedCount = Mathf.Clamp(targetReleasedCount, 0, totalPieces);

        int dropsToPerform = targetReleasedCount - _piecesReleased;

        if (dropsToPerform > 0)
        {
            for (int i = 0; i < dropsToPerform; i++)
            {
                // Tắt visual của mảnh
                if (_piecesReleased < pieceVisuals.Length && pieceVisuals[_piecesReleased] != null)
                {
                    pieceVisuals[_piecesReleased].gameObject.SetActive(false);
                }

                // Spawn 1 cục currency cho mỗi mảnh vỡ (có kèm giá trị currencyPerPiece)
                if (dropCurrencyEffect != null)
                {
                    dropCurrencyEffect.SpawnCurrencyAt(transform.position, currencyPerPiece);
                }

                _piecesReleased++;
            }
        }
    }

}

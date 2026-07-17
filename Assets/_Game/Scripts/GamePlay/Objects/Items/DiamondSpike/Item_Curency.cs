using System;
using GamePlay.Entities;
using DG.Tweening;
using Pools;
using UnityEngine;

namespace GamePlay.Items
{
    /// <summary>
    /// Đại diện cho 1 viên kim cương bay ra từ DiamondSpike
    /// </summary>
    public class Item_Curency : ItemUnit
    {
        [Header("Configuration")]
        [SerializeField] private DiamondPieceConfig _config;

        [Header("Diamond Settings")]
        [SerializeField] private CurrencyType _currencyType = CurrencyType.Gem; // Loại tiền (mặc định là Gem)


        private RectTransform _targetRectTransform; // UI target để bay đến
        private Camera _mainCamera; // Camera để convert world to screen
        public int GemGainPerPiece = 100;

        // Static event replacing DataManager references for Playable Ads compatibility
        public static Action<int, CurrencyType> OnCurrencyCollected;

        public override void Despawn()
        {
            Free();

            // Called automatically by PoolManager when despawned
            CancelAllMotions();
        }

        /// <summary>
        /// Khởi tạo và bắt đầu animation cho diamond piece
        /// </summary>
        /// <param name="startPosition">Vị trí bắt đầu</param>
        /// <param name="targetPosition">Vị trí đích (fallback nếu không tìm thấy UI target)</param>
        public void Active(Vector3 startPosition, Vector3 targetPosition)
        {
            // Reset transform
            Transform.position = startPosition;
            Transform.localScale = Vector3.one;
            Transform.rotation = Quaternion.identity;
            gameObject.SetActive(true);

            // Lấy camera reference
            var cameraFollow = CameraManager.Instance?.GetCameraFollow();
            _mainCamera = cameraFollow?.GetCamera();


            // Cancel previous motions nếu có
            CancelAllMotions();

            // Bắt đầu animation sequence
            StartAnimation(startPosition, targetPosition);
        }

        private void CancelAllMotions()
        {
            transform.DOKill();
        }

        private void StartAnimation(Vector3 startPosition, Vector3 targetPosition)
        {
            // Validate config
            if (_config == null)
            {
                Debug.LogError("DiamondPiece: Config is null! Cannot start animation.");
                OnAnimationComplete();
                return;
            }

            // Validate startPosition
            if (float.IsNaN(startPosition.x) || float.IsNaN(startPosition.y) || float.IsNaN(startPosition.z))
            {
                Debug.LogError("DiamondPiece: startPosition is NaN! Cannot start animation.");
                OnAnimationComplete();
                return;
            }

            // 1. Random hướng bắn trên mặt phẳng XZ (Horizontal Plane)
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized; // normalized để luôn bắn ra rìa (max range)
            Vector3 horizontalDirection = new Vector3(randomCircle.x, 0f, randomCircle.y);
            float arcHeight = 1.0f; // Độ cao vòng cung (Hardcode hoặc thêm vào Config)
            float radius = UnityEngine.Random.Range(_config.spreadRadius.x, _config.spreadRadius.y);
            Vector3 arcPeakPosition = startPosition
                                      + (horizontalDirection * radius)
                                      + (Vector3.up * arcHeight);

            // Validate arcPeakPosition
            if (float.IsNaN(arcPeakPosition.x) || float.IsNaN(arcPeakPosition.y) || float.IsNaN(arcPeakPosition.z))
            {
                Debug.LogError("DiamondPiece: arcPeakPosition is NaN! Using startPosition as fallback.");
                arcPeakPosition = startPosition + Vector3.up * 2f; // Simple fallback
            }

            // 3. Dip position (vị trí nhún xuống)
            Vector3 dipPosition = arcPeakPosition - Vector3.up * _config.dipAmount;

            // Tổng thời gian animation
            float totalDuration = _config.arcDuration + _config.dipDuration + _config.floatDelay + _config.moveToTargetDuration;

            Sequence seq = DOTween.Sequence();

            // Phase 1: Bắn ra theo cung parabol (0 -> arcDuration)
            seq.Append(transform.DOJump(arcPeakPosition, arcHeight, 1, _config.arcDuration).SetEase(Ease.OutCubic));

            // Phase 2: Nhún xuống rồi lên lại (arcDuration -> arcDuration + dipDuration)
            seq.Append(transform.DOMove(dipPosition, _config.dipDuration / 2f).SetEase(Ease.InOutQuad));
            seq.Append(transform.DOMove(arcPeakPosition, _config.dipDuration / 2f).SetEase(Ease.InOutQuad));

            // Phase 3: Lơ lửng (arcDuration + dipDuration -> + floatDelay)
            seq.AppendInterval(_config.floatDelay);

            // Phase 4: Bay đến target (còn lại)
            Tween moveToTargetTween = DOVirtual.Float(0f, 1f, _config.moveToTargetDuration, t =>
            {
                // Ease In Circ
                t = 1f - Mathf.Sqrt(1f - t * t);

                if (_targetRectTransform != null && _mainCamera != null && transform != null)
                {
                    Vector3 currentScreenPos = _mainCamera.WorldToScreenPoint(arcPeakPosition);

                    if (float.IsNaN(currentScreenPos.x) || float.IsNaN(currentScreenPos.y) || float.IsNaN(currentScreenPos.z))
                    {
                        transform.position = Vector3.Lerp(arcPeakPosition, targetPosition, t);
                    }
                    else
                    {
                        Vector3 targetScreenPos = _targetRectTransform.position;
                        Vector3 newScreenPos = Vector3.Lerp(currentScreenPos, targetScreenPos, t);

                        if (!float.IsNaN(newScreenPos.x) && !float.IsNaN(newScreenPos.y) && !float.IsNaN(newScreenPos.z))
                        {
                            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(
                                newScreenPos.x,
                                newScreenPos.y,
                                currentScreenPos.z
                            ));

                            if (!float.IsNaN(worldPos.x) && !float.IsNaN(worldPos.y) && !float.IsNaN(worldPos.z))
                            {
                                transform.position = worldPos;
                            }
                        }
                    }
                }
                else
                {
                    Vector3 newPos = Vector3.Lerp(arcPeakPosition, targetPosition, t);
                    if (!float.IsNaN(newPos.x) && !float.IsNaN(newPos.y) && !float.IsNaN(newPos.z))
                    {
                        transform.position = newPos;
                    }
                }
            });

            seq.Append(moveToTargetTween);

            // === SCALE ANIMATION ===
            transform.DOScale(Vector3.one, 0f);
            transform.DOScale(Vector3.zero, _config.scaleDuration)
                .SetDelay(totalDuration - _config.scaleDuration)
                .SetEase(Ease.InQuad);

            // === ROTATION ANIMATION ===
            Vector3 randomRotationAxis = UnityEngine.Random.onUnitSphere;
            transform.DORotate(randomRotationAxis * 360f * (totalDuration / 1f), totalDuration, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear);

            seq.OnComplete(OnAnimationComplete);
            seq.SetTarget(transform);
        }

        private void OnAnimationComplete()
        {
            int claimAmount = GemGainPerPiece;

            // Keep Buff calculation if needed, otherwise fallback to GemGainPerPiece
            if (_currencyType == CurrencyType.Cash)
            {
                // claimAmount = CardBuffApplier.ApplyIncomeBuff(GemGainPerPiece);
            }

            // Trigger static event instead of DataManager
            OnCurrencyCollected?.Invoke(claimAmount, _currencyType);

            // Sau đó mới despawn object về pool
            this.Despawn();
        }

        private void OnDestroy()
        {
            // Clean up motions khi object bị destroy
            CancelAllMotions();
        }
    }
}

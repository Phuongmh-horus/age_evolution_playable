using System;
using System.Collections;
using GamePlay.Items;
using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.CardSystem
{
    /// <summary>
    /// Visual đại diện cho một card đang bay.
    /// Flow: xuất hiện tại vị trí nguồn (hiện "?") → bay ra trung tâm màn hình
    ///       → reveal card thực tế → bay về vị trí đích.
    /// </summary>
    public class CardInfoVisual : MonoBehaviour
    {
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image iconImage;

        private RectTransform _rectTransform;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        /// <summary>
        /// Bắt đầu toàn bộ animation fly → reveal → fly.
        /// </summary>
        /// <param name="data">Thông tin card.</param>
        /// <param name="startScreenPos">Vị trí bắt đầu (screen space).</param>
        /// <param name="centerScreenPos">Vị trí trung tâm màn hình (screen space).</param>
        /// <param name="destScreenPos">Vị trí đích cuối cùng (screen space).</param>
        /// <param name="flyToCenterDuration">Thời gian bay ra trung tâm.</param>
        /// <param name="revealDuration">Thời gian hiệu ứng reveal.</param>
        /// <param name="flyToDestDuration">Thời gian bay về đích.</param>
        /// <param name="onComplete">Callback khi animation kết thúc.</param>
        public void Play(
            CardInfoData data,
            Vector3 startScreenPos,
            Vector3 centerScreenPos,
            Vector3 destScreenPos,
            float flyToCenterDuration,
            float revealDuration,
            float flyToDestDuration,
            Vector2 targetSize = default,
            RectTransform targetSlot = null,
            float scaleAtCenter = 1f,
            Action onComplete = null)
        {
            StartCoroutine(PlayAnimation(
                data,
                startScreenPos,
                centerScreenPos,
                destScreenPos,
                flyToCenterDuration,
                revealDuration,
                flyToDestDuration,
                targetSize,
                targetSlot,
                scaleAtCenter,
                onComplete));
        }

        private IEnumerator PlayAnimation(
            CardInfoData data,
            Vector3 startScreen,
            Vector3 centerScreen,
            Vector3 destScreen,
            float dur1,
            float dur2,
            float dur3,
            Vector2 targetSize,
            RectTransform targetSlot,
            float scaleAtCenter,
            Action onComplete)
        {
            // Khoi too: hien dau "?" (Unknown sprite), an icon
            SetupUnknown(data);
            _rectTransform.position = startScreen;
            _rectTransform.localScale = Vector3.one;

            // Phase A: bay ra trung tâm màn hình
            yield return StartCoroutine(FlyTo(startScreen, centerScreen, dur1, scaleAtCenter));

            // Phase B: reveal � doi sang sprite thuc te + hien icon
            yield return StartCoroutine(RevealCard(data, dur2, scaleAtCenter));

            // Phase C: bay ve vi tri dich, dong thoi lerp size ve targetSlot
            if (targetSlot != null)
            {
                _rectTransform.SetParent(targetSlot, true);
                Vector3 startLocalPos = _rectTransform.localPosition;
                
                float targetScale = 1f;
                if (targetSize != Vector2.zero)
                {
                    targetScale = targetSize.x / Mathf.Max(_rectTransform.sizeDelta.x, 0.01f);
                }

                float elapsed = 0f;
                while (elapsed < dur3)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur3));
                    _rectTransform.localPosition = Vector3.Lerp(startLocalPos, Vector3.zero, t);
                    _rectTransform.localScale = Vector3.one * Mathf.Lerp(scaleAtCenter, targetScale, t);
                    yield return null;
                }
                
                _rectTransform.localPosition = Vector3.zero;
                _rectTransform.localScale = Vector3.one * targetScale;
            }
            else
            {
                float targetScale = 1f;
                if (targetSize != Vector2.zero)
                {
                    targetScale = targetSize.x / Mathf.Max(_rectTransform.sizeDelta.x, 0.01f);
                }

                float elapsed = 0f;
                while (elapsed < dur3)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur3));
                    _rectTransform.position = Vector3.Lerp(centerScreen, destScreen, t);
                    _rectTransform.localScale = Vector3.one * Mathf.Lerp(scaleAtCenter, targetScale, t);
                    yield return null;
                }
                
                _rectTransform.position = destScreen;
                _rectTransform.localScale = Vector3.one * targetScale;
            }

            onComplete?.Invoke();
        }

        private void SetupUnknown(CardInfoData data)
        {
            if (backgroundImage != null)
            {
                if (data.SpriteCardTypeData != null &&
                    data.SpriteCardTypeData.TryGetSprite(data.LevelCard, out var spriteCard))
                    backgroundImage.sprite = spriteCard.Unknown;
                backgroundImage.enabled = true;
            }

            if (iconImage != null)
                iconImage.enabled = false;
        }

        private void UpdateVisual(CardInfoData data)
        {
            if (backgroundImage != null && data.SpriteCardTypeData != null)
            {
                if (data.SpriteCardTypeData.TryGetSprite(data.LevelCard, out var spriteCard))
                    backgroundImage.sprite = spriteCard.Normal;
            }

            if (iconImage != null && data.StatsUpgradeIcon != null)
            {
                var icon = data.StatsUpgradeIcon.GetIcon(data.Type);
                if (icon != null)
                {
                    iconImage.sprite = icon;
                    iconImage.enabled = true;
                }
            }
        }

        private IEnumerator RevealCard(CardInfoData data, float duration, float scaleAtCenter)
        {
            UpdateVisual(data);

            float half = duration * 0.5f;

            // Scale up
            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / half);
                _rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.3f, t) * scaleAtCenter;
                yield return null;
            }

            // Scale down
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / half);
                _rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, t) * scaleAtCenter;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _rectTransform.localScale = Vector3.one * scaleAtCenter;
        }

        private IEnumerator FlyTo(Vector3 start, Vector3 end, float duration, float targetScale = 1f, float startScale = 1f, RectTransform targetSlot = null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                Vector3 currentEnd = targetSlot != null ? targetSlot.position : end;
                _rectTransform.position = Vector3.Lerp(start, currentEnd, t);
                _rectTransform.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, t);
                yield return null;
            }
            _rectTransform.position = targetSlot != null ? targetSlot.position : end;
            _rectTransform.localScale = Vector3.one * targetScale;
        }
    }
}

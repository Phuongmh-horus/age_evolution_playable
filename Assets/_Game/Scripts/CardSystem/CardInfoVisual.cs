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
            Action onComplete)
        {
            // Khởi tạo: hiện dấu "?" (Unknown sprite), ẩn icon
            SetupUnknown(data);
            _rectTransform.position = startScreen;
            _rectTransform.localScale = Vector3.one;

            // Phase A: bay ra trung tâm màn hình
            yield return StartCoroutine(FlyTo(startScreen, centerScreen, dur1));

            // Phase B: reveal — đổi sang sprite thực tế + hiện icon
            yield return StartCoroutine(RevealCard(data, dur2));

            // Phase C: bay về vị trí đích, đồng thời lerp size về targetSlot
            if (targetSize != Vector2.zero)
                yield return StartCoroutine(FlyToWithResize(centerScreen, destScreen, dur3, _rectTransform.sizeDelta, targetSize));
            else
                yield return StartCoroutine(FlyTo(centerScreen, destScreen, dur3));

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

        private IEnumerator RevealCard(CardInfoData data, float duration)
        {
            UpdateVisual(data);

            float half = duration * 0.5f;

            // Scale up
            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / half);
                _rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.3f, t);
                yield return null;
            }

            // Scale down
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / half);
                _rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, t);
                yield return null;
            }
            
            yield return new WaitForSeconds(0.5f);

            _rectTransform.localScale = Vector3.one;
        }

        private IEnumerator FlyTo(Vector3 from, Vector3 to, float duration)
        {
            if (duration <= 0f)
            {
                _rectTransform.position = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                _rectTransform.position = Vector3.Lerp(from, to, t);
                yield return null;
            }

            _rectTransform.position = to;
        }

        /// <summary>
        /// Bay về đích đồng thời lerp localScale về tỉ lệ targetSize/fromSize, giữ đúng tỉ lệ.
        /// </summary>
        private IEnumerator FlyToWithResize(Vector3 from, Vector3 to, float duration, Vector2 fromSize, Vector2 toSize)
        {
            if (duration <= 0f)
            {
                _rectTransform.position = to;
                _rectTransform.localScale = Vector3.one * (toSize.x / Mathf.Max(fromSize.x, 0.01f));
                yield break;
            }

            float targetScale = toSize.x / Mathf.Max(fromSize.x, 0.01f);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                _rectTransform.position = Vector3.Lerp(from, to, t);
                _rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, targetScale, t);
                yield return null;
            }

            _rectTransform.position = to;
            _rectTransform.localScale = Vector3.one * targetScale;
        }
    }
}

using System.Collections.Generic;
using GamePlay.Data;
using GamePlay.Items;
using UnityEngine;
using Pools;

namespace GamePlay.CardSystem
{
    /// <summary>
    /// Hệ thống lưu trữ và hiển thị các card đã thu thập.
    /// Chỉ xử lý visual — không ảnh hưởng logic game.
    ///
    /// Setup:
    ///   1. Gắn component này lên một GameObject trong scene.
    ///   2. Kéo một Canvas (Screen Space - Overlay) vào targetCanvas.
    ///   3. Tạo prefab từ CardInfoVisual và kéo vào cardVisualPrefab.
    ///   4. Đặt cardDestinationRect là RectTransform trên canvas đại diện cho vị trí
    ///      bộ sưu tập card (góc màn hình, v.v.).
    ///   5. Gán SpriteCardTypeData và StatsUpgradeIcon cùng loại với IncreaseElement.
    /// </summary>
    public class BuffCardSystem : MonoBehaviour
    {
        public static BuffCardSystem Instance { get; private set; }

        [Header("References")]
        [SerializeField] private CardInfoVisual cardVisualPrefab;
        [SerializeField] private Canvas targetCanvas;
        [SerializeField] private List<RectTransform> cardSlots;

        [Header("Card Config")]
        [SerializeField] private SpriteCardTypeData spriteCardTypeData;
        [SerializeField] private StatsUpgradeIcon statsUpgradeIcon;

        [Header("Animation Timing")]
        [SerializeField] private float flyToCenterDuration = 0.4f;
        [SerializeField] private float revealDuration = 0.3f;
        [SerializeField] private float flyToDestDuration = 0.5f;

        /// <summary>Danh sách các card đã thu thập (chỉ lưu config để truy vấn).</summary>
        private readonly List<CardInfoData> _collectedCards = new List<CardInfoData>();

        public void Clear()
        {
            _collectedCards.Clear();
            if (cardSlots != null)
            {
                for (int s = 0; s < cardSlots.Count; s++)
                {
                    var slot = cardSlots[s];
                    if (slot == null) continue;
                    for (int i = slot.childCount - 1; i >= 0; i--)
                    {
                        var child = slot.GetChild(i);
                        PoolSystem.Despawn(child);
                    }
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void PrewarmCards()
        {
            if (cardVisualPrefab == null) return;

            int slotCapacity = cardSlots != null && cardSlots.Count > 0 ? cardSlots.Count : 8;
            int amountToPrewarm = Mathf.Clamp(slotCapacity * 3, 12, 24);

            for (int i = 0; i < amountToPrewarm; i++)
            {
                PoolSystem.Prewarm(cardVisualPrefab, 1);
            }
        }

        /// <summary>
        /// Gọi sau Phase2 trong CollisionSequence.
        /// Lưu card và phát animation fly → reveal → fly về đích.
        /// </summary>
        /// <param name="elementData">Dữ liệu element từ IncreaseElement.</param>
        /// <param name="levelCard">Level card đạt được.</param>
        /// <param name="sourceWorldTransform">Transform world-space của IncreaseElement làm điểm xuất phát.</param>
        public void PlayCollectAnimation(
            IncreaseElementData elementData,
            int levelCard,
            Transform sourceWorldTransform,
            int index = 0,
            int total = 1)
        {
            var data = new CardInfoData
            {
                Type = elementData.Type,
                LevelCard = levelCard,
                SpriteCardTypeData = spriteCardTypeData,
                StatsUpgradeIcon = statsUpgradeIcon,
            };

            _collectedCards.Add(data);

            if (cardVisualPrefab == null || targetCanvas == null) return;

            int slotIndex = _collectedCards.Count - 1;
            RectTransform targetSlot = (cardSlots != null && slotIndex < cardSlots.Count)
                ? cardSlots[slotIndex]
                : null;

            float spacing = 130f;
            float totalWidth = (total - 1) * spacing;
            float startX = -totalWidth * 0.5f;
            float offsetX = startX + index * spacing;

            Vector3 startScreenPos = GetScreenPos(sourceWorldTransform);
            Vector3 centerScreenPos = new Vector3(Screen.width * 0.5f + offsetX, Screen.height * 0.5f + 120f, 0f);
            Vector3 destScreenPos = targetSlot != null
                ? (Vector3)targetSlot.position
                : centerScreenPos;

            var visualGo = cardVisualPrefab.gameObject.Spawn();
            var visual = visualGo.GetComponent<CardInfoVisual>();
            visual.transform.SetParent(targetCanvas.transform, false);

            var rect = visual.GetComponent<RectTransform>();
            var prefabRect = cardVisualPrefab.GetComponent<RectTransform>();
            if (prefabRect != null && rect != null)
            {
                rect.anchorMin = prefabRect.anchorMin;
                rect.anchorMax = prefabRect.anchorMax;
                rect.pivot = prefabRect.pivot;
                rect.sizeDelta = prefabRect.sizeDelta;
            }

            float scaleAtCenter = total > 1 ? 0.85f : 1f;

            visual.Play(
                data,
                startScreenPos,
                centerScreenPos,
                destScreenPos,
                flyToCenterDuration,
                revealDuration,
                flyToDestDuration,
                targetSize: targetSlot != null ? targetSlot.rect.size : Vector2.zero,
                targetSlot: targetSlot,
                scaleAtCenter: scaleAtCenter);
        }

        public void PlayCustomCollectAnimation(GameObject customPrefab, IncreaseElementData elementData, int levelCard, Transform sourceWorldTransform, int index = 0, int total = 1)
        {
            if (customPrefab == null || targetCanvas == null) return;

            var data = new CardInfoData
            {
                Type = elementData.Type,
                LevelCard = levelCard,
                SpriteCardTypeData = spriteCardTypeData,
                StatsUpgradeIcon = statsUpgradeIcon,
            };
            _collectedCards.Add(data);

            int slotIndex = _collectedCards.Count - 1;
            RectTransform targetSlot = (cardSlots != null && cardSlots.Count > 0)
                ? cardSlots[Mathf.Min(slotIndex, cardSlots.Count - 1)]
                : null;

            float spacing = 130f;
            float totalWidth = (total - 1) * spacing;
            float startX = -totalWidth * 0.5f;
            float offsetX = startX + index * spacing;

            Vector3 startScreenPos = GetScreenPos(sourceWorldTransform);
            Vector3 centerScreenPos = new Vector3(Screen.width * 0.5f + offsetX, Screen.height * 0.5f + 120f, 0f);
            Vector3 destScreenPos = targetSlot != null
                ? (Vector3)targetSlot.position
                : centerScreenPos;

            var visual = customPrefab.gameObject.Spawn();
            visual.transform.SetParent(targetCanvas.transform, false);

            var rect = visual.GetComponent<RectTransform>();
            if (rect == null)
            {
                // BuffDef.VisualPrefab MUST have RectTransform pre-attached in Editor
                Debug.LogWarning($"[BuffCardSystem] Custom prefab '{customPrefab.name}' missing RectTransform. Add it in Editor to avoid runtime GC.");
                visual.SetActive(false);
                return;
            }

            var prefabRect = customPrefab.GetComponent<RectTransform>();
            if (prefabRect != null && rect != null)
            {
                rect.anchorMin = prefabRect.anchorMin;
                rect.anchorMax = prefabRect.anchorMax;
                rect.pivot = prefabRect.pivot;
                rect.sizeDelta = prefabRect.sizeDelta;
            }

            Vector2 targetSize = targetSlot != null ? targetSlot.rect.size : Vector2.zero;
            float scaleAtCenter = total > 1 ? 0.85f : 1f;

            StartCoroutine(CoPlayCustomAnimation(rect, startScreenPos, centerScreenPos, destScreenPos, targetSize, targetSlot, scaleAtCenter));
        }

        private System.Collections.IEnumerator CoPlayCustomAnimation(RectTransform rect, Vector3 start, Vector3 center, Vector3 dest, Vector2 targetSize, RectTransform targetSlot, float scaleAtCenter)
        {
            rect.position = start;
            rect.localScale = Vector3.one;

            // Phase A: Fly to center
            yield return StartCoroutine(CoFlyTo(rect, start, center, flyToCenterDuration, scaleAtCenter));

            // Phase B: Reveal (Scale up and down slightly)
            float halfReveal = revealDuration * 0.5f;
            float elapsed = 0f;
            while (elapsed < halfReveal)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / halfReveal;
                rect.localScale = Vector3.Lerp(Vector3.one * scaleAtCenter, Vector3.one * (scaleAtCenter * 1.15f), t);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < halfReveal)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / halfReveal;
                rect.localScale = Vector3.Lerp(Vector3.one * (scaleAtCenter * 1.15f), Vector3.one * scaleAtCenter, t);
                yield return null;
            }

            // Small delay before flying to destination
            elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            rect.localScale = Vector3.one * scaleAtCenter;

            // Phase C: Fly to dest
            if (targetSlot != null)
            {
                rect.SetParent(targetSlot, true);
                Vector3 startLocalPos = rect.localPosition;

                float targetScale = 1f;
                if (targetSize != Vector2.zero)
                {
                    targetScale = targetSize.x / Mathf.Max(rect.sizeDelta.x, 0.01f);
                }

                elapsed = 0f;
                while (elapsed < flyToDestDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / flyToDestDuration));
                    rect.localPosition = Vector3.Lerp(startLocalPos, Vector3.zero, t);
                    rect.localScale = Vector3.one * Mathf.Lerp(scaleAtCenter, targetScale, t);
                    yield return null;
                }

                rect.localPosition = Vector3.zero;
                rect.localScale = Vector3.one * targetScale;
            }
            else
            {
                float targetScale = 1f;
                if (targetSize != Vector2.zero)
                {
                    targetScale = targetSize.x / Mathf.Max(rect.sizeDelta.x, 0.01f);
                }

                elapsed = 0f;
                while (elapsed < flyToDestDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / flyToDestDuration));
                    rect.position = Vector3.Lerp(center, dest, t);
                    rect.localScale = Vector3.one * Mathf.Lerp(scaleAtCenter, targetScale, t);
                    yield return null;
                }

                rect.position = dest;
                rect.localScale = Vector3.one * targetScale;
            }
        }



        private System.Collections.IEnumerator CoFlyTo(RectTransform rect, Vector3 from, Vector3 to, float duration, float targetScale = 1f, float startScale = 1f, RectTransform targetSlot = null)
        {
            if (duration <= 0f)
            {
                rect.position = targetSlot != null ? targetSlot.position : to;
                rect.localScale = Vector3.one * targetScale;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                Vector3 currentTo = targetSlot != null ? targetSlot.position : to;
                rect.position = Vector3.Lerp(from, currentTo, t);
                rect.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, t);
                yield return null;
            }
            rect.position = targetSlot != null ? targetSlot.position : to;
            rect.localScale = Vector3.one * targetScale;
        }

        private Vector3 GetScreenPos(Transform worldTransform)
        {
            if (worldTransform == null)
                return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

            if (Camera.main != null)
                return Camera.main.WorldToScreenPoint(worldTransform.position);

            return worldTransform.position;
        }
    }
}

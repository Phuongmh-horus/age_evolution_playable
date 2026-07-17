using System.Collections.Generic;
using GamePlay.Data;
using GamePlay.Items;
using UnityEngine;

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
    public class WeaponCardSystem : MonoBehaviour
    {
        public static WeaponCardSystem Instance { get; private set; }

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

        public IReadOnlyList<CardInfoData> CollectedCards => _collectedCards;

        public void Clear()
        {
            _collectedCards.Clear();
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

            EnsureSlotHierarchyActive(targetSlot);

            float spacing = 220f;
            float totalWidth = (total - 1) * spacing;
            float startX = -totalWidth * 0.5f;
            float offsetX = startX + index * spacing;

            Vector3 startScreenPos = GetScreenPos(sourceWorldTransform);
            Vector3 centerScreenPos = new Vector3(Screen.width * 0.5f + offsetX, Screen.height * 0.5f + 150f, 0f);
            Vector3 destScreenPos = targetSlot != null
                ? (Vector3)targetSlot.position
                : centerScreenPos;

            var visual = Instantiate(cardVisualPrefab, targetCanvas.transform);
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

            EnsureSlotHierarchyActive(targetSlot);

            float spacing = 220f;
            float totalWidth = (total - 1) * spacing;
            float startX = -totalWidth * 0.5f;
            float offsetX = startX + index * spacing;

            Vector3 startScreenPos = GetScreenPos(sourceWorldTransform);
            Vector3 centerScreenPos = new Vector3(Screen.width * 0.5f + offsetX, Screen.height * 0.5f + 150f, 0f);
            Vector3 destScreenPos = targetSlot != null
                ? (Vector3)targetSlot.position
                : centerScreenPos;

            var visual = Instantiate(customPrefab, targetCanvas.transform);

            var layout = visual.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.ignoreLayout = true;

            var rect = visual.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = visual.AddComponent<RectTransform>();
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
                rect.localScale = Vector3.one * Mathf.Lerp(1f, 1.3f, elapsed / halfReveal) * scaleAtCenter;
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < halfReveal)
            {
                elapsed += Time.deltaTime;
                rect.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, elapsed / halfReveal) * scaleAtCenter;
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            rect.localScale = Vector3.one * scaleAtCenter;

            // Phase C: Fly to dest
            if (targetSize != Vector2.zero)
            {
                float targetScale = targetSize.x / Mathf.Max(rect.sizeDelta.x, 0.01f);
                elapsed = 0f;
                while (elapsed < flyToDestDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / flyToDestDuration));
                    rect.position = Vector3.Lerp(center, dest, t);
                    rect.localScale = Vector3.one * Mathf.Lerp(scaleAtCenter, targetScale, t);
                    yield return null;
                }
                rect.localScale = Vector3.one * targetScale;
            }
            else
            {
                yield return StartCoroutine(CoFlyTo(rect, center, dest, flyToDestDuration, 1f, scaleAtCenter));
            }

            rect.position = dest;
            if (targetSlot != null)
            {
                rect.SetParent(targetSlot, false);
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private void EnsureSlotHierarchyActive(RectTransform slot)
        {
            if (slot == null) return;

            Transform canvasTransform = targetCanvas != null ? targetCanvas.transform : null;
            Transform current = slot;

            while (current != null && current != canvasTransform)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);

                current = current.parent;
            }

            if (cardSlots != null)
            {
                for (int i = 0; i < cardSlots.Count; i++)
                {
                    var s = cardSlots[i];
                    if (s != null)
                    {
                        bool shouldBeActive = i < _collectedCards.Count;
                        if (s.gameObject.activeSelf != shouldBeActive)
                        {
                            s.gameObject.SetActive(shouldBeActive);
                        }
                    }
                }
            }

            Canvas.ForceUpdateCanvases();
        }

        private System.Collections.IEnumerator CoFlyTo(RectTransform rect, Vector3 from, Vector3 to, float duration, float targetScale = 1f, float startScale = 1f)
        {
            if (duration <= 0f)
            {
                rect.position = to;
                rect.localScale = Vector3.one * targetScale;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                rect.position = Vector3.Lerp(from, to, t);
                rect.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, t);
                yield return null;
            }
            rect.position = to;
            rect.localScale = Vector3.one * targetScale;
        }

        private static Vector3 GetScreenPos(Transform worldTransform)
        {
            if (worldTransform == null)
                return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

            if (Camera.main != null)
                return Camera.main.WorldToScreenPoint(worldTransform.position);

            return worldTransform.position;
        }
    }
}

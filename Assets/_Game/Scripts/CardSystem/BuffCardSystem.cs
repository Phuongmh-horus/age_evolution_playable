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
            int amountToPrewarm = Mathf.Clamp(slotCapacity * 2, 5, 15);

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

            float spacing = 300f;
            float totalWidth = (total - 1) * spacing;
            float offsetX = -totalWidth * 0.5f + index * spacing;
            float offsetY = 220f;

            Vector3 startLocalPos = GetLocalPos(sourceWorldTransform != null ? sourceWorldTransform.position : Vector3.zero);
            Vector2 screenCenter = GetScreenCenterLocal();
            Vector3 centerLocalPos = new Vector3(screenCenter.x + offsetX, screenCenter.y + offsetY, 0f);
            Vector3 destLocalPos = centerLocalPos;

            var visualGo = cardVisualPrefab.gameObject.Spawn();
            var visual = visualGo.GetComponent<CardInfoVisual>();
            visual.transform.SetParent(targetCanvas.transform, false);


            float originalScale = 1f;
            var rect = visual.GetComponent<RectTransform>();
            var prefabRect = cardVisualPrefab.GetComponent<RectTransform>();
            if (prefabRect != null && rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = prefabRect.sizeDelta;
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = prefabRect.localScale;
                originalScale = prefabRect.localScale.x;
            }

            float scaleAtCenter = originalScale;

            visual.Play(
                data,
                startLocalPos,
                centerLocalPos,
                destLocalPos,
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

            float spacing = 300f;
            float totalWidth = (total - 1) * spacing;
            float offsetX = -totalWidth * 0.5f + index * spacing;
            float offsetY = 220f;

            Vector3 startLocalPos = GetLocalPos(sourceWorldTransform != null ? sourceWorldTransform.position : Vector3.zero);
            Vector2 screenCenter = GetScreenCenterLocal();
            Vector3 centerLocalPos = new Vector3(screenCenter.x + offsetX, screenCenter.y + offsetY, 0f);
            Vector3 destLocalPos = centerLocalPos;

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

            float originalScale = 1f;
            var prefabRect = customPrefab.GetComponent<RectTransform>();
            if (prefabRect != null && rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = prefabRect.sizeDelta;
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = prefabRect.localScale;
                originalScale = prefabRect.localScale.x;
            }

            Vector2 targetSize = targetSlot != null ? targetSlot.rect.size : Vector2.zero;
            float scaleAtCenter = originalScale;

            StartCoroutine(CoPlayCustomAnimation(rect, startLocalPos, centerLocalPos, destLocalPos, targetSize, targetSlot, scaleAtCenter));
        }

        private System.Collections.IEnumerator CoPlayCustomAnimation(RectTransform rect, Vector3 start, Vector3 center, Vector3 dest, Vector2 targetSize, RectTransform targetSlot, float scaleAtCenter)
        {
            rect.anchoredPosition = start;
            rect.localScale = Vector3.one * scaleAtCenter;

            // Phase A: Fly to center
            yield return StartCoroutine(CoFlyTo(rect, start, center, flyToCenterDuration, scaleAtCenter, scaleAtCenter));

            // Phase B: Reveal (No scaling per user request)
            rect.localScale = Vector3.one * scaleAtCenter;

            // Small delay before flying to destination
            float elapsed = 0f;
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
                    rect.anchoredPosition = Vector3.Lerp(center, dest, t);
                    rect.localScale = Vector3.one * Mathf.Lerp(scaleAtCenter, targetScale, t);
                    yield return null;
                }

                rect.anchoredPosition = dest;
                rect.localScale = Vector3.one * targetScale;
            }
        }



        private System.Collections.IEnumerator CoFlyTo(RectTransform rect, Vector2 from, Vector2 to, float duration, float startScale, float targetScale, RectTransform targetSlot = null)
        {
            if (duration <= 0f)
            {
                if (targetSlot != null) rect.position = targetSlot.position;
                else rect.anchoredPosition = to;
                rect.localScale = Vector3.one * targetScale;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                if (targetSlot != null) rect.position = Vector3.Lerp(from, targetSlot.position, t);
                else rect.anchoredPosition = Vector3.Lerp(from, to, t);
                rect.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, t);
                yield return null;
            }
            if (targetSlot != null) rect.position = targetSlot.position;
            else rect.anchoredPosition = to;
            rect.localScale = Vector3.one * targetScale;
        }

        private Vector2 GetScreenCenterLocal()
        {
            if (targetCanvas == null) return Vector2.zero;

            Camera mainCam = Camera.main;
            Camera uiCam = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
            if (uiCam == null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCam = mainCam;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetCanvas.GetComponent<RectTransform>(),
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                uiCam,
                out Vector2 localCenter
            );

            return localCenter;
        }

        private Vector3 GetLocalPos(Vector3 worldPos)
        {
            if (targetCanvas == null) return worldPos;

            // 1. Get screen point using the 3D main camera
            Camera mainCam = Camera.main;
            Vector2 screenPoint = mainCam != null ? (Vector2)mainCam.WorldToScreenPoint(worldPos) : RectTransformUtility.WorldToScreenPoint(null, worldPos);

            // 2. Map screen point to UI using the UI camera
            Camera uiCam = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
            if (uiCam == null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCam = mainCam;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetCanvas.GetComponent<RectTransform>(),
                screenPoint,
                uiCam,
                out Vector2 localPoint
            );

            return localPoint;
        }
    }
}

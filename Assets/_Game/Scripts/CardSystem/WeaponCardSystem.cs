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
            Transform sourceWorldTransform)
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

            Vector3 startScreenPos = GetScreenPos(sourceWorldTransform);
            Vector3 centerScreenPos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            Vector3 destScreenPos = targetSlot != null
                ? (Vector3)targetSlot.position
                : centerScreenPos;

            var visual = Instantiate(cardVisualPrefab, targetCanvas.transform);
            visual.Play(
                data,
                startScreenPos,
                centerScreenPos,
                destScreenPos,
                flyToCenterDuration,
                revealDuration,
                flyToDestDuration,
                targetSize: targetSlot != null ? targetSlot.rect.size : Vector2.zero);
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

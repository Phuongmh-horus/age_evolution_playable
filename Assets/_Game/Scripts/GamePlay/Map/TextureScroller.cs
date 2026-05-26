using UnityEngine;

namespace GamePlay.Roads
{
    /// <summary>
    /// Component để scroll texture của material theo hướng và tốc độ tùy chỉnh
    /// </summary>
    public class TextureScroller : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Mesh Renderer chứa material cần scroll")]
        public MeshRenderer targetRenderer;

        [Header("Scroll Settings")]
        [Tooltip("Tốc độ scroll (units/giây)")]
        public float scrollSpeed = 1f;

        [Tooltip("Hướng scroll")]
        public ScrollDirection direction = ScrollDirection.Up;

        [Tooltip("Tên property của texture trong shader (thường là _MainTex)")]
        public string texturePropertyName = "_MainTex";

        [Header("Options")]
        [Tooltip("Tự động lấy material từ Renderer khi Start")]
        public bool autoGetMaterial = true;

        [Tooltip("Có tạo instance riêng của material không (nên bật để tránh ảnh hưởng material gốc)")]
        public bool createMaterialInstance = true;

        private Material _material;
        private Vector2 _currentOffset = Vector2.zero;
        private bool _isScrolling = true;
        private Vector2 _scrollVector;
        private int _texturePropertyId;

        #region Unity Lifecycle

        private void Start()
        {
            SetupMaterial();
        }

        private void Update()
        {
            if (!GameplayManager.IsGameStarted) return;

            if (_isScrolling)
            {
                ScrollTexture();
            }
        }

        private void OnDestroy()
        {
            CleanupMaterial();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Thiết lập material để scroll
        /// </summary>
        public void SetupMaterial()
        {
            if (targetRenderer == null)
            {
                Debug.LogWarning($"[TextureScroller] Missing MeshRenderer on {gameObject.name}. Assign in Inspector.");
                return;
            }

            if (createMaterialInstance)
            {
                _material = targetRenderer.material; // Tự động tạo instance
            }
            else
            {
                _material = targetRenderer.sharedMaterial; // Dùng material gốc
            }

            _texturePropertyId = Shader.PropertyToID(texturePropertyName);
            _scrollVector = GetScrollVector(direction);
        }

        /// <summary>
        /// Đặt tốc độ scroll mới
        /// </summary>
        public void SetScrollSpeed(float speed)
        {
            scrollSpeed = speed;
        }

        /// <summary>
        /// Đặt hướng scroll mới
        /// </summary>
        public void SetDirection(ScrollDirection newDirection)
        {
            direction = newDirection;
            _scrollVector = GetScrollVector(newDirection);
        }

        /// <summary>
        /// Bật/tắt scroll
        /// </summary>
        public void SetScrolling(bool enabled)
        {
            _isScrolling = enabled;
        }

        /// <summary>
        /// Reset offset về 0
        /// </summary>
        public void ResetOffset()
        {
            _currentOffset = Vector2.zero;
            if (_material != null)
            {
                _material.SetTextureOffset(_texturePropertyId, _currentOffset);
            }
        }

        /// <summary>
        /// Đồng bộ tốc độ với tốc độ world (ví dụ: tốc độ nhân vật)
        /// </summary>
        /// <param name="worldSpeed">Tốc độ world</param>
        /// <param name="multiplier">Hệ số nhân (mặc định 0.5f)</param>
        /// <param name="reverse">Có đảo ngược chiều không</param>
        public void SyncWithWorldSpeed(float worldSpeed, float multiplier = 0.5f, bool reverse = true)
        {
            scrollSpeed = worldSpeed * multiplier * (reverse ? -1f : 1f);
        }

        #endregion

        #region Private Methods

        private void ScrollTexture()
        {
            if (_material == null) return;

            float dt = Time.deltaTime;

            // Cộng dồn offset
            _currentOffset += _scrollVector * scrollSpeed * dt;

            // Giữ giá trị trong khoảng 0-1 để tránh số quá lớn
            _currentOffset.x %= 1.0f;
            _currentOffset.y %= 1.0f;

            // Gán vào material
            _material.SetTextureOffset(_texturePropertyId, _currentOffset);
        }

        private static Vector2 GetScrollVector(ScrollDirection scrollDirection)
        {
            switch (scrollDirection)
            {
                case ScrollDirection.Up:
                    return Vector2.up;
                case ScrollDirection.Down:
                    return Vector2.down;
                case ScrollDirection.Left:
                    return Vector2.left;
                case ScrollDirection.Right:
                    return Vector2.right;
                case ScrollDirection.UpRight:
                    return (Vector2.up + Vector2.right).normalized;
                case ScrollDirection.UpLeft:
                    return (Vector2.up + Vector2.left).normalized;
                case ScrollDirection.DownRight:
                    return (Vector2.down + Vector2.right).normalized;
                case ScrollDirection.DownLeft:
                    return (Vector2.down + Vector2.left).normalized;
                default:
                    return Vector2.zero;
            }
        }

        private void CleanupMaterial()
        {
            // Chỉ destroy nếu đã tạo instance
            if (createMaterialInstance && _material != null)
            {
                Destroy(_material);
            }
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (targetRenderer == null)
                Debug.LogWarning($"[TextureScroller] Missing MeshRenderer on {gameObject.name}. Assign in Inspector.");

            _texturePropertyId = Shader.PropertyToID(texturePropertyName);
            _scrollVector = GetScrollVector(direction);
        }
#endif

        #endregion
    }

    /// <summary>
    /// Enum định nghĩa hướng scroll
    /// </summary>
    public enum ScrollDirection
    {
        Up,
        Down,
        Left,
        Right,
        UpRight,
        UpLeft,
        DownRight,
        DownLeft
    }
}


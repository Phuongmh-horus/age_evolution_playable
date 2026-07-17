using UnityEngine;

namespace GamePlay.Inputs
{
    [DisallowMultipleComponent]
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Độ nhạy khi vuốt. Giá trị càng lớn, nhân vật di chuyển càng nhanh.")]
        public float sensitivity = 1.0f;

        // Lưu vị trí X của frame trước để tính toán độ lệch
        private float _lastFrameX;
        private float _moveFactorX; // Giá trị từ -1 đến 1 trả về cho Controller

        private void Awake()
        {
            // Playable-safe singleton (không DontDestroyOnLoad)
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!GameplayManager.IsGameStarted)
            {
                _moveFactorX = 0f;
                // Track finger constantly before start to prevent sudden delta jump
                if (Input.GetMouseButton(0))
                {
                    _lastFrameX = Input.mousePosition.x;
                }
                return;
            }

            HandleInput();
        }

        private void HandleInput()
        {
            if (Time.timeScale == 0f)
            {
                _moveFactorX = 0f;
                _lastFrameX = Input.mousePosition.x;
                return;
            }

            _moveFactorX = 0f;

            if (Input.GetMouseButtonDown(0))
            {
                _lastFrameX = Input.mousePosition.x;
            }
            else if (Input.GetMouseButton(0))
            {
                float currentX = Input.mousePosition.x;
                float pixelDelta = currentX - _lastFrameX;

                // Normalize theo screen width
                _moveFactorX = (pixelDelta / Mathf.Max(1f, Screen.width)) * sensitivity * 100f;

                _lastFrameX = currentX;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _moveFactorX = 0f;
            }
        }

        /// <summary>
        /// Trả về giá trị Delta X.
        /// < 0: Vuốt trái
        /// > 0: Vuốt phải
        /// = 0: Không vuốt
        /// </summary>
        public float GetMoveDelta()
        {
            return _moveFactorX;
        }
    }
}

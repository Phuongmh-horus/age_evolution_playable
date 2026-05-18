using UnityEngine;
using GamePlay.ComponentSystems;

namespace GamePlay.Components // Hoặc namespace chứa các Component của bạn
{
    public class OscillatorComponent : BaseComponent, IOscillator
    {
        [Header("Oscillation Config")]
        [SerializeField] private float leftOffset = 2.0f;
        [SerializeField] private float rightOffset = 5.0f;

        // Implement Interface Properties
        public float LeftOffset => leftOffset;
        public float RightOffset => rightOffset;

        public void Setup(float leftOffs, float rightOffs)
        {
            leftOffset = leftOffs;
            rightOffset = rightOffs;
        }

        public void Initialize()
        {
            // Logic khởi tạo nếu cần
        }

        public void Dispose()
        {
            // Logic hủy nếu cần
        }
    }
}

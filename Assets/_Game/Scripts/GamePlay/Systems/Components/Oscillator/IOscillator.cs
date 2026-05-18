namespace GamePlay.ComponentSystems
{
    // 1. Interface mới cho Dao động
    public interface IOscillator : IComponent
    {
        float LeftOffset { get; }  // Khoảng cách lệch trái tối đa
        float RightOffset { get; } // Khoảng cách lệch phải tối đa

        void Setup(float leftOffset, float rightOffset);
    }
}

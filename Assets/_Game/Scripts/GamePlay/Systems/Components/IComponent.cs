namespace GamePlay.ComponentSystems
{
    public interface IComponent
    {
        UnityEngine.Transform Transform { get; }
        void Initialize();
        void Dispose();
    }
}

using System;

namespace GamePlay.ComponentSystems
{
    public interface IJumper : IComponent
    {
        event Action<IHitable> OnJumperComplete;
        uint TargetMask { get; }
        void OnJumpSucceed(IHitable target);
    }
}

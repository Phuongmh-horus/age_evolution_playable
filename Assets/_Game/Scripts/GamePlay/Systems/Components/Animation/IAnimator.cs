using System;
using GamePlay.ComponentSystems;

namespace GamePlay.AnimationSystems
{
    public interface IAnimator : IComponent
    {
        void PlayAnimation(AnimationType animationType, float waitForAction = 0.5f, Action onComplete = null);
    }

    public enum AnimationType : byte
    {
        None,
        Idle,
        Move,
        Rotate,
        Attack,
        Jump,
        Death,
        ConveyorJump,
        Break,
    }
}

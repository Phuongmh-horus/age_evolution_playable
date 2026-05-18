using System;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public interface IMover : IComponent
    {
        event Action OnMovementComplete;
        float Duration { get; }
        Vector3 MoveDirection { get; }
        float MaxDistance { get; }
        void OnMovementFinished();
    }
}

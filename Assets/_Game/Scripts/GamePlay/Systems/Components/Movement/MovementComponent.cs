using UnityEngine;
using System;

namespace GamePlay.ComponentSystems
{
    public class MovementComponent : BaseComponent, IMover
    {
        public event Action OnMovementComplete = delegate { };

        [Header("Config")]
        [SerializeField] protected MoveVariable moveVariable;

        // --- IMoveable Implementation ---
        public Vector3 MoveDirection => CacheTransform.forward;
        public float MaxDistance => moveVariable.MaxDistance;
        public float Duration => moveVariable.Duration;

        public override void Initialize()
        {
            OnMovementComplete = delegate { };
        }

        public void OnMovementFinished()
        {
            OnMovementComplete?.Invoke();
        }

    }
}

using UnityEngine;
using System;

namespace GamePlay.ComponentSystems
{
    public class MovementComponent : BaseComponent, IMover
    {
        private static readonly Action NoMovementComplete = () => { };

        public event Action OnMovementComplete = NoMovementComplete;

        [Header("Config")]
        [SerializeField] protected MoveVariable moveVariable;

        // --- IMoveable Implementation ---
        public Vector3 MoveDirection => CacheTransform.forward;
        public float MaxDistance => moveVariable.MaxDistance;
        public float Duration => moveVariable.Duration;

        public override void Initialize()
        {
            base.Initialize();
            OnMovementComplete = NoMovementComplete;
        }

        public void OnMovementFinished()
        {
            OnMovementComplete?.Invoke();
        }

    }
}

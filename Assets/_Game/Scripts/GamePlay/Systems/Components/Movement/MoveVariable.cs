using UnityEngine;

namespace GamePlay.ComponentSystems
{
    [CreateAssetMenu(fileName = "MoveVariable", menuName = "GameVariables/Components/MoveVariable")]
    public class MoveVariable : ScriptableObject
    {
        [Header("Runtime (Read-only in design, but no custom inspector in playable)")]
        public float Duration;
        public float MaxDistance;

        [Header("Defaults")]
        public float DefaultDuration = 1.2f;
        public float DefaultMaxDistance = 30f;

        private void OnDisable()
        {
            ResetValues();
        }

        public void ResetValues()
        {
            Duration = DefaultDuration;
            MaxDistance = DefaultMaxDistance;
        }
    }
}

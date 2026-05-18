using UnityEngine;

namespace GamePlay.ComponentSystems
{
    [CreateAssetMenu(fileName = "AttackVariable", menuName = "GameVariables/Components/AttackVariable")]
    public class AttackVariable : ScriptableObject
    {
        public float ThrowDistance = 18f; // Khoảng cách ném
        public float AttackCooldown = 2f; // Khoảng cách ném
        public float ThrowDuration = 1.2f; // Thời gian bay (giây)
        public float ArcHeight = 3.0f;
        public float OffsetY = 0.5f;

        public float RotationSpeed = 5.0f; // Sau khi ném 1s thì vũ khí mới hiện lại trên tay

        public float AnimDelaySeconds = 0.3f; // Delay vung tay

        public float WeaponReloadDelay = 1.0f; // Sau khi ném 1s thì vũ khí mới hiện lại trên tay
    }
}


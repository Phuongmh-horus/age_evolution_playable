using UnityEngine;

namespace CardSystem.Data
{
    /// <summary>
    /// Cấu hình riêng cho các kỹ năng kiếm của Samurai.
    /// Gán vào BuffDefinition.SamuraiConfig; để null nếu skill không phải kiếm.
    /// </summary>
    [CreateAssetMenu(fileName = "SamuraiSkillConfig", menuName = "CardSystem/SamuraiSkillConfig", order = 3)]
    public class SamuraiSkillConfigSO : ScriptableObject
    {
        [Tooltip("Số phát bắn để kích hoạt kiếm (SamuraiSwordThrow / BoomerangSword / MythicSlash)")]
        public int ShotThreshold = 5;

        [Tooltip("Tốc độ bay của kiếm (units/s)")]
        public float ProjectileSpeed = 20f;

        [Tooltip("Chiều rộng vùng quét — chỉ dùng cho SamuraiMythicSlash (0 = không áp dụng)")]
        public float SlashWidth = 0f;
    }
}

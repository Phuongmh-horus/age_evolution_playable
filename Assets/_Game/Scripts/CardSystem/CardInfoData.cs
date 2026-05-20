using GamePlay.Data;
using GamePlay.Items;

namespace GamePlay.CardSystem
{
    /// <summary>
    /// Dữ liệu card được lưu trữ sau khi thu thập.
    /// Chỉ lưu config để truy vấn visual qua SpriteCardTypeData và StatsUpgradeIcon.
    /// </summary>
    [System.Serializable]
    public struct CardInfoData
    {
        public StatType Type;
        public int LevelCard;
        public SpriteCardTypeData SpriteCardTypeData;
        public StatsUpgradeIcon StatsUpgradeIcon;
    }
}

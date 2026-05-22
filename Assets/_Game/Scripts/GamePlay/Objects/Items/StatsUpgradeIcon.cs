using GamePlay.Items;
using UnityEngine;
using System.Collections.Generic;

namespace GamePlay.Data
{
    [System.Serializable]
    public struct StatSpriteType
    {
        public StatType statType;
        public Sprite sprite;
    }

    [CreateAssetMenu(fileName = "StatsUpgradeIcon", menuName = "GamePlay/Stats Upgrade Icon")]
    public class StatsUpgradeIcon : ScriptableObject
    {
        [SerializeField]
        private List<StatSpriteType> iconList = new List<StatSpriteType>();

        private Dictionary<StatType, Sprite> cachedIconMap;

        [ContextMenu("Build Cache")]
        private void BuildCache()
        {
            if (cachedIconMap != null)
                return;

            cachedIconMap = new Dictionary<StatType, Sprite>();
            foreach (var item in iconList)
            {
                if (!cachedIconMap.ContainsKey(item.statType))
                {
                    cachedIconMap[item.statType] = item.sprite;
                }
            }
        }

        public Sprite GetIcon(StatType statType)
        {
            return TryGetIcon(statType, out var icon) ? icon : null;
        }

        public bool TryGetIcon(StatType statType, out Sprite icon)
        {
            BuildCache();
            return cachedIconMap.TryGetValue(statType, out icon);
        }
    }
}

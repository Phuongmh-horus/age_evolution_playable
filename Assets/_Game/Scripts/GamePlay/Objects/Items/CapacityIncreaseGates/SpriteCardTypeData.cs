using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Items
{
    [System.Serializable]
    public struct SpriteCardType
    {
        public Sprite Normal;
        public Sprite Unknown;
    }

    [CreateAssetMenu(fileName = "SpriteCardTypeData", menuName = "Game/Sprite Card Type Data")]
    public class SpriteCardTypeData : ScriptableObject
    {
        public List<SpriteCardType> spriteCards = new List<SpriteCardType>();

        public bool TryGetSprite(int index, out SpriteCardType spriteCard)
        {
            if (spriteCards == null || spriteCards.Count == 0)
            {
                spriteCard = default;
                return false;
            }

            index = Mathf.Clamp(index, 0, spriteCards.Count - 1);

            spriteCard = spriteCards[index];
            return true;
        }
    }
}

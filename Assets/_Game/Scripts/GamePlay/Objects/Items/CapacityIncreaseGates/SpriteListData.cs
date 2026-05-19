using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Items
{
    [CreateAssetMenu(fileName = "SpriteListData", menuName = "Game/Sprite List Data")]
    public class SpriteListData : ScriptableObject
    {
        public List<Sprite> sprites = new List<Sprite>();
    }
}

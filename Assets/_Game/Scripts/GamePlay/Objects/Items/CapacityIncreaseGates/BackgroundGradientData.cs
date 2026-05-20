using UnityEngine;

namespace GamePlay.Items
{
    [System.Serializable]
    public class GradientColor
    {
        public Color32 from;
        public Color32 to;
    }

    [CreateAssetMenu(fileName = "BackgroundGradientData", menuName = "Game/Background Gradient Data")]
    public class BackgroundGradientData : ScriptableObject
    {
        [SerializeField] private GradientColor normal = new GradientColor();
        [SerializeField] private GradientColor active = new GradientColor();

        public GradientColor Normal => normal;
        public GradientColor Active => active;
    }
}

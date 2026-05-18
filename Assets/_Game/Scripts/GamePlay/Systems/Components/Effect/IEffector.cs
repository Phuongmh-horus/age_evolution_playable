using System;
using GamePlay.Effects;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public interface IEffector : IComponent
    {
        void PlayEffect(
            EffectType effectType,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null,
            float waitForAction = 0.5f,
            Action onComplete = null);
    }
}

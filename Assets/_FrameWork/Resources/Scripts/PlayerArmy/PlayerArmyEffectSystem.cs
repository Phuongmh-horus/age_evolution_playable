using System;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace PlayerArmy
{
    [DisallowMultipleComponent]
    public class PlayerArmyEffectSystem : MonoBehaviour
    {
        [SerializeField] private EffectComponent effectComponent;

        private void Awake()
        {
            ResolveEffectComponent();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveEffectComponent();
        }
#endif

        public void Initialize()
        {
            ResolveEffectComponent();
            effectComponent?.Initialize();
        }

        public void PlayEffect(EffectType effectType, Transform anchor = null, Action onComplete = null, float waitForAction = 0f)
        {
            var component = ResolveEffectComponent();
            if (component == null || effectType == EffectType.None)
            {
                onComplete?.Invoke();
                return;
            }

            Transform target = anchor != null ? anchor : transform;
            component.PlayEffect(effectType, target.position, target.rotation, target, waitForAction, onComplete);
        }

        public void PlayEffectAt(EffectType effectType, Vector3 position, Quaternion rotation, Transform parent = null, Action onComplete = null, float waitForAction = 0f)
        {
            var component = ResolveEffectComponent();
            if (component == null || effectType == EffectType.None)
            {
                onComplete?.Invoke();
                return;
            }

            component.PlayEffect(effectType, position, rotation, parent != null ? parent : transform, waitForAction, onComplete);
        }

        public void PlayOnUnit(ArmyUnit unit, EffectType effectType, Action onComplete = null, float waitForAction = 0f)
        {
            if (unit == null)
            {
                onComplete?.Invoke();
                return;
            }

            PlayEffect(effectType, unit.transform, onComplete, waitForAction);
        }

        private EffectComponent ResolveEffectComponent()
        {
            if (effectComponent != null)
            {
                return effectComponent;
            }

            effectComponent = GetComponentInChildren<EffectComponent>(true);
            return effectComponent;
        }
    }
}

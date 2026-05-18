using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public class JumpComponent : BaseComponent, IJumper
    {
        public event Action<IHitable> OnJumperComplete = delegate { };

        [Header("Jump Config (Active Check)")]
        [SerializeField] protected EntityType jumpTarget;

        // Serialize as basic int/mask for Playable inspector
        [SerializeField, Tooltip("Auto-calculated from EntityType")] 
        protected uint targetMask;
        
        public uint TargetMask => targetMask;

        #if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            targetMask = GetTarget();
        }
        #endif

        public override void Initialize()
        {
            base.Initialize(); // BaseComponent usually has empty Initialize but good practice
            OnJumperComplete = delegate { };
        }
        
        public override void Dispose()
        {
             base.Dispose();
             OnJumperComplete = null;
        }

        public void OnJumpSucceed(IHitable target)
        {
            OnJumperComplete?.Invoke(target);
        }

        private uint GetTarget()
        {
            uint mask = 0;
            int targetVal = (int)jumpTarget;
            // Basic safety check for enum range (assuming < 32)
            if (targetVal <= 0 || targetVal >= 32) return 0;
            
            mask |= (1u << targetVal);
            return mask;
        }
    }
}

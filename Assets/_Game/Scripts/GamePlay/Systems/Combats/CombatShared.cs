using GamePlay.AnimationSystems;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.CombatSystems
{
    [System.Flags]
    public enum CapabilityFlags : uint
    {
        None = 0,
        Move = 1 << 0,
        Attack = 1 << 1,
        Hit = 1 << 2,
        Heal = 1 << 3,
        Jump = 1 << 4,
        Animator = 1 << 5,
        Oscillate = 1 << 6,
        Effector = 1 << 7,
    }

    public struct CapabilityPack
    {
        public IAnimator Animator;

        public IMover Mover;
        public IAttacker Attacker;
        public IJumper Jumper;

        public IHitable Hitable;
        public IHealable Healable;

        public IOscillator Oscillator;
        public IEffector Effector;
    }

    public struct CombatActorData
    {
        public CapabilityFlags Flags;

        // Movement data
        public Vector3 StartPosition;
        public Vector3 Direction;
        public float Speed;
        public float StartTime;
        public float Duration; // MaxDistance / Speed

        // Attack data
        public Vector2 Size;
        public int Damage;
        public uint TargetMaskBits;
    }

    public struct CollisionResultInfo
    {
        public bool HasHit;
        public int TargetIndex;
        public bool MovementFinished;
        public int Damage;
    }
}

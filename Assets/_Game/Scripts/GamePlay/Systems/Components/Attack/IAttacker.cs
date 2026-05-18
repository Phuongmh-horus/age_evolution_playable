using System;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public interface IAttacker : IComponent
    {
        event Action<IHitable> OnAttackComplete;
        EntityType EntityType { get; }
        Vector2 Size { get; }
        int Damage { get; }
        uint TargetMask { get; }

        /// <summary>
        /// Vị trí của attacker trong world space
        /// </summary>
        Vector3 Position { get; }

        void OnAttackSucceed(IHitable target);
        void Setup(int damage);
    }
}
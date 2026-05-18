using System;
using GamePlay.CombatSystems;

namespace GamePlay.ComponentSystems
{
    public interface IHealable : IComponent
    {
        event Action<int, int> OnHealthChange; // current health - max health
        bool IsDead { get; }
        void TakeDamage(IAttacker source);

        int GetCurrentHealth();
        int GetMaxHealth();
    }
}

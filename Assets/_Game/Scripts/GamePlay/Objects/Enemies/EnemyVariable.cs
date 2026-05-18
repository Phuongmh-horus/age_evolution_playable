using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.Enemies
{
    [CreateAssetMenu(fileName = "EnemyVariable", menuName = "GameVariables/Enemies/EnemyVariable")]
    public class EnemyVariable : ScriptableObject
    {
        public AttackVariable AttackVariable;
    }
}

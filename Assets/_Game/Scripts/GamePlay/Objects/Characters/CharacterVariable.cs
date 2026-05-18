using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.Characters
{
    [CreateAssetMenu(fileName = "CharacterVariable", menuName = "GameVariables/Charactes/CharacterVariable")]
    public class CharacterVariable : ScriptableObject
    {
        public MoveVariable MoveVariable;

        public void ResetValues()
        {
            MoveVariable.ResetValues();
        }
    }
}

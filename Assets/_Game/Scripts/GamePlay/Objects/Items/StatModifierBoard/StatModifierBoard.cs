using UnityEngine;

namespace GamePlay.Items
{
    public class StatModifierBoard : StatModifierItem<StatModifierBoardData>
    {
        #if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            Data.Type = StatType.MoveSpeed;
        }
        #endif

        protected override void HandleWheelCollision()
        {
            GameplayManager.Instance.ChangeStatModifierData(Data);
        }

    }
}


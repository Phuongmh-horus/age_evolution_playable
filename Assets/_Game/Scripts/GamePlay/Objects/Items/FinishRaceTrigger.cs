using GamePlay.Items;
using GamePlay.ComponentSystems;
using UnityEngine;

/// <summary>
/// Trigger khi wheel đến gần đích
/// </summary>
public class FinishRaceTrigger : ItemUnit
{
    protected override void HandleHitComplete(IAttacker source)
    {
    }

    protected override void HandleWheelCollision()
    {
        RegisterEvents(false);
        GameplayManager.Instance.BeginFinishRace();
    }
}
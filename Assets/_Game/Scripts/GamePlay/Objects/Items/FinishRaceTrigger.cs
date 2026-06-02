using GamePlay.Items;
using GamePlay.ComponentSystems;
using UnityEngine;

/// <summary>
/// Trigger khi wheel đến gần đích
/// </summary>
public class FinishRaceTrigger : ItemUnit
{
    private bool _isTriggered;

    protected override void HandleHitComplete(IAttacker source)
    {
        if (_isTriggered) return;
        if (source == null) return;

        _isTriggered = true;
        RegisterEvents(false);
        GameplayManager.Instance.BeginFinishRace();
    }

    protected override void HandleWheelCollision()
    {
        if (_isTriggered) return;
        _isTriggered = true;
        RegisterEvents(false);
        GameplayManager.Instance.BeginFinishRace();
    }
}

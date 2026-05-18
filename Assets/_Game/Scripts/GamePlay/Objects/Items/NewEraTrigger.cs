using System.Collections;
using System.Collections.Generic;
using GamePlay.Entities;
using GamePlay.Items;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class NewEraTrigger : ItemUnit
{
    public bool WaitForTrigger;
    public UnityEvent OnTrigger;

    private void Awake()
    {
        // [FIX] Ensure EntityType is GateNewEra for collision detection
        if (_entityType == EntityType.None || _entityType == EntityType.Item)
        {
            _entityType = EntityType.GateNewEra;
        }
    }

    public override void Initialize()
    {
        // [FIX] Ensure EntityType is set before base.Initialize() registers to CollisionSystem
        if (_entityType == EntityType.None || _entityType == EntityType.Item)
        {
            _entityType = EntityType.GateNewEra;
        }

        base.Initialize();
    }

    [Header("End Game Settings")]
    [SerializeField] private AudioClipName hitByWheelSfx = AudioClipName.None;
    [SerializeField] private float endGameFallbackDelay = 0.6f;

    private bool _endGameTriggered;
    private Coroutine _fallbackRoutine;

    protected override void HandleWheelCollision()
    {
        if(!GameplayManager.IsGameStarted) return;
        GameplayManager.IsGameStarted = false;

        // [FIX] Match CashTower: Play Sound
        if (SoundManager.Instance != null && hitByWheelSfx != AudioClipName.None)
            SoundManager.Instance.PlayOneShot(hitByWheelSfx);
        else if (SoundManager.Instance != null)
             SoundManager.Instance.PlayOneShot(AudioClipName.SFX_Level_Complete); // Fallback default

        GameplayManager.Instance.PauseGame();

        // Invoke event (e.g. tracking)
        // [FIX] Restore WaitForTrigger logic to avoid double EndGame calls if the event also triggers it (e.g. after Gate Open).
        if(OnTrigger != null && WaitForTrigger)
        {
             OnTrigger.Invoke();
             if (_fallbackRoutine != null) StopCoroutine(_fallbackRoutine);
             _fallbackRoutine = StartCoroutine(FallbackEndGame());
        }
        else
        {
             // [FIX] Always call EndGame to show EndCard if NOT waiting for trigger
             OnEndGame();
        }
    }

    public void OnEndGame()
    {
        if (_endGameTriggered) return;
        _endGameTriggered = true;
        GameplayManager.Instance.EndGame(true);
    }

    private IEnumerator FallbackEndGame()
    {
        float delay = Mathf.Max(0.05f, endGameFallbackDelay);
        yield return new WaitForSeconds(delay);
        if (!_endGameTriggered)
        {
            OnEndGame();
        }
        _fallbackRoutine = null;
    }
}

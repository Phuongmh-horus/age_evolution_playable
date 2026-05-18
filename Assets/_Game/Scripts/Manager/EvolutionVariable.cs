using UnityEngine;

[CreateAssetMenu(fileName = "EvolutionVariable", menuName = "GameVariables/Evolutions/EvolutionVariable")]
public class EvolutionVariable : ScriptableObject
{
    public int Capacity;
    public int ProgressPoint;

    [Header("Defaults")]
    public int DefaultCapacity = 1;
    public int DefaultProgressPoint = 0;

    /// <summary>
    /// Reset values về mặc định
    /// </summary>
    public void ResetValues()
    {
        Capacity = DefaultCapacity;
        ProgressPoint = DefaultProgressPoint;
    }

    private void OnDisable()
    {
        ResetValues();
    }
}
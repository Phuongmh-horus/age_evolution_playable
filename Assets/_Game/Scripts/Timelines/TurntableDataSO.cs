using GamePlay.Crushers;
using UnityEngine;

[CreateAssetMenu(fileName = "NewTurntable", menuName = "Game Config/Turntable", order = 5)]
public class TurntableDataSO : ScriptableObject
{
    [Header("Turntable Information")]
    [Tooltip("ID duy nhất của turntable")]
    public int TurntableId;

    [Header("Turntable Settings")]
    [Tooltip("Prefab của bàn xoay")]
    public WheelUnit TurntablePrefab;
    
    [Tooltip("Thời gian quay hết 1 vòng 360 độ (seconds)")]
    public float SpinDuration = 1f;
}

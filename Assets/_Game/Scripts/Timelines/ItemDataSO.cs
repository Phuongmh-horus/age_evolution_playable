using UnityEngine;

/// <summary>
/// Class data dữ liệu cho từng Era
/// </summary>

[CreateAssetMenu(fileName = "ItemData", menuName = "Game Config/Item/Item Data")]
public class ItemDataSO : ScriptableObject
{
    [Header("Fire Rate")]
    public float MaxFireRatePoint;

    [Header("Fire Range")]
    public float MaxFireRangePoint;
}

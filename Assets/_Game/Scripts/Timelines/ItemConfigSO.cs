using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Class config dữ liệu cố định cho cả game - không dành riêng cho Era hay Timeline nào
/// </summary>

[CreateAssetMenu(fileName = "ItemConfig", menuName = "Game Config/Item/Item Config")]
public class ItemConfigSO : ScriptableObject
{
    [Header("Fire Rate")]
    public float MinFireRate;
    public float MaxFireRate;

    [Header("Fire Range")]
    public float MinFireRange;
    public float MaxFireRange;
}

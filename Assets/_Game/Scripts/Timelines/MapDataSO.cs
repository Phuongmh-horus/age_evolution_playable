using UnityEngine;
using GamePlay.Roads;

[CreateAssetMenu(fileName = "NewMap", menuName = "Game Config/Map", order = 3)]
public class MapDataSO : ScriptableObject
{
    [Header("Map Information")]
    [Tooltip("Tên map")]
    public string MapName;

    [Tooltip("Mô tả map")]
    [TextArea(3, 5)]
    public string Description;

    [Header("Road Segment")]
    [Tooltip("RoadSegment prefab cho map này")]
    public RoadSegment RoadSegment;
    
    [Header("Segment Dimensions")]
    [Tooltip("Độ dài của đoạn content (scale Z)")]
    public float ContentLength = 300f;

    [Tooltip("Độ dài của đoạn finish (scale Z)")]
    public float FinishLength = 50f;

    public GameObject BackGround;
}

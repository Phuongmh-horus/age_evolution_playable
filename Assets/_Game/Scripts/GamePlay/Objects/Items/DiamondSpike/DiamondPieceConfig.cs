using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "DiamondPieceConfig", menuName = "ScriptableObjects/DiamondPieceConfig", order = 1)]
public class DiamondPieceConfig : ScriptableObject
{
    [Header("Diamond Settings")]
    public int gemGainPerPiece = 1;

    [Header("Shoot Out Settings (Bắn ra)")]
    [Tooltip("Tốc độ bắn ra")]
    public Vector2 spreadRadius = new Vector2(3, 5);

    [Tooltip("Góc bắn lên (degrees)")]
    public float shootUpAngle = 60f;

    [Tooltip("Thời gian bay theo cung")]
    public float arcDuration = 0.6f;

    [Header("Dip Settings (Nhún xuống rồi lên)")]
    [Tooltip("Độ nhún xuống")]
    public float dipAmount = 0.5f;

    [Tooltip("Thời gian nhún")]
    public float dipDuration = 0.4f;

    [Header("Flight Settings")]
    [Tooltip("Delay trước khi bay đến target")]
    public float floatDelay = 0.2f;

    [Tooltip("Thời gian bay đến target")]
    public float moveToTargetDuration = 1f;

    [Tooltip("Thời gian scale animation")]
    public float scaleDuration = 1f;

    [Header("Rotation Settings")]
    [Tooltip("Xoay chậm trong arc (degrees/s)")]
    public float rotationSpeedArc = 180f;

    [Tooltip("Xoay thật chậm khi bay đến target (degrees/s)")]
    public float rotationSpeedToTarget = 90f;
}

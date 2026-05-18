using UnityEngine;

/// <summary>
/// Waypoint cho camera path trong TrackPreviewCameraState
/// Định nghĩa position, rotation và FOV tại một điểm trên đường đi
/// </summary>
[System.Serializable]
public class TrackPreviewWaypoint
{
    [Header("Path Progress")]
    [Tooltip("Vị trí của waypoint trên path (0 = end point, 1 = start point)")]
    [Range(0f, 1f)]
    public float pathProgress = 0.5f;

    [Header("Camera Offset")]
    [Tooltip("Offset từ vị trí path đến camera (Vector3.zero = nằm trên đường path)")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("Camera Rotation")]
    [Tooltip("Có sử dụng rotation tùy chỉnh không")]
    public bool useCustomRotation = false;

    [Tooltip("Rotation của camera (Euler angles)")]
    public Vector3 rotation = Vector3.zero;

    [Header("Field of View")]
    [Tooltip("Có sử dụng FOV tùy chỉnh không")]
    public bool useCustomFOV = false;

    [Tooltip("Field of View của camera tại waypoint này")]
    [Range(10f, 120f)]
    public float fieldOfView = 60f;

    [Header("Transition")]
    [Tooltip("Curve để blend vào waypoint này")]
    public AnimationCurve transitionCurve = AnimationCurve.Linear(0, 0, 1, 1);

    /// <summary>
    /// Tính toán position của camera tại waypoint này
    /// </summary>
    public Vector3 GetCameraPosition(Vector3 startWorldPos, Vector3 endWorldPos, Transform referenceTransform)
    {
        // Tính vị trí trên path
        Vector3 pathPosition = Vector3.Lerp(endWorldPos, startWorldPos, pathProgress);

        // Thêm offset (chuyển từ local space sang world space nếu có reference transform)
        Vector3 offsetWorld = referenceTransform != null
            ? referenceTransform.TransformDirection(positionOffset)
            : positionOffset;

        return pathPosition + offsetWorld;
    }

    /// <summary>
    /// Tính toán rotation của camera tại waypoint này
    /// </summary>
    public Quaternion GetCameraRotation()
    {
        if (!useCustomRotation)
            return Quaternion.identity;

        return Quaternion.Euler(rotation);
    }

    /// <summary>
    /// Lấy FOV tại waypoint này
    /// </summary>
    public float GetFieldOfView()
    {
        return useCustomFOV ? fieldOfView : 60f;
    }
}

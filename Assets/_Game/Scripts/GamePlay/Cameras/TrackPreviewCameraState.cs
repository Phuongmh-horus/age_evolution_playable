using System;
using UnityEngine;

[CreateAssetMenu(fileName = "TrackPreviewState", menuName = "Camera/Track Preview State")]
public class TrackPreviewCameraState : CameraState
{
    [Header("Track Preview Path")]
    public Transform startPoint;
    public Transform endPoint;
    public float previewSpeed = 0.2f;
    public AnimationCurve previewCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Camera Settings")]
    public Vector3 offsetFromPath = new Vector3(0, 5, -3);

    [Header("Rotation Offset")]
    public bool useRotationOffset = false;
    public Vector3 rotationOffset = Vector3.zero; // Euler angles (X, Y, Z)

    [Header("FOV Settings")]
    public bool useFOVTransition = false;
    public float fovStart = 60f;
    public float fovEnd = 60f;
    public AnimationCurve fovCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [HideInInspector] public float progress = 0f;
    [HideInInspector] public bool isRunning = false;
    [HideInInspector] private float originalFOV = 60f;

    [Space]
    public float OffsetBegin = 100;
    public float OffsetEnd = 100;
    public float PreviewDuration => 1f / Mathf.Max(previewSpeed, 0.001f);

    [Header("Waypoints")]
    [Tooltip("Các điểm trung gian trên đường path. Camera sẽ đi qua các điểm này với position, rotation và FOV riêng")]
    public TrackPreviewWaypoint[] waypoints = new TrackPreviewWaypoint[0];

    private void OnValidate()
    {
        CameraStateName = CameraFollow.CameraStateName.TrackPreview;
    }

    public override void OnEnter(CameraFollow cameraFollow, CameraFollow.TransitionMode transitionMode)
    {
        if (startPoint == null || endPoint == null)
        {
            Debug.LogWarning($"[{CameraStateName}] Track preview points not set!");
            return;
        }

        isRunning = true;
        progress = 0f;

        Camera camera = cameraFollow.GetCamera();
        if (camera == null) return;

        // Save original FOV
        originalFOV = camera.fieldOfView;

        // Set vị trí ban đầu tại waypoint[0]
        if (waypoints != null && waypoints.Length > 0)
        {
            Vector3 startWorldPos = startPoint.localPosition + startPoint.forward * OffsetBegin;
            Vector3 endWorldPos = endPoint.localPosition - endPoint.forward * OffsetEnd;

            // Đặt camera tại waypoint[0]
            camera.transform.position = waypoints[0].GetCameraPosition(startWorldPos, endWorldPos, startPoint);

            // Set rotation nếu waypoint[0] có custom rotation
            if (waypoints[0].useCustomRotation)
            {
                camera.transform.rotation = waypoints[0].GetCameraRotation();
            }

            // Set FOV
            if (waypoints[0].useCustomFOV)
            {
                camera.fieldOfView = waypoints[0].GetFieldOfView();
            }
            else if (useFOVTransition)
            {
                camera.fieldOfView = fovStart;
            }
        }
        else
        {
            // Fallback: nếu không có waypoints, dùng end point như cũ
            Vector3 endWorldPos = endPoint.position - endPoint.forward * OffsetEnd;
            Vector3 offsetWorld = endPoint.TransformDirection(offsetFromPath);
            camera.transform.position = endWorldPos + offsetWorld;

            if (useFOVTransition)
            {
                camera.fieldOfView = fovStart;
            }
        }
    }

    public override void OnUpdate(CameraFollow cameraFollow)
    {
        if (!isRunning || startPoint == null || endPoint == null)
            return;

        Camera camera = cameraFollow.GetCamera();
        if (camera == null)
            return;

        progress += Time.deltaTime * previewSpeed;
        float curvedProgress = previewCurve.Evaluate(Mathf.Clamp01(progress));

        // Tính world position của start và end (localPosition = world vì parent có scale)
        Vector3 startWorldPos = startPoint.localPosition + startPoint.forward * OffsetBegin;
        Vector3 endWorldPos = endPoint.localPosition - endPoint.forward * OffsetEnd;

        // Tính position, rotation và FOV dựa trên waypoints
        Vector3 cameraPosition = CalculateCameraPosition(curvedProgress, startWorldPos, endWorldPos);
        Quaternion cameraRotation = CalculateCameraRotation(curvedProgress);
        float cameraFOV = CalculateFOV(curvedProgress);

        camera.transform.position = cameraPosition;
        camera.transform.rotation = cameraRotation;
        camera.fieldOfView = cameraFOV;

        if (progress >= 1f)
        {
            isRunning = false;
        }
    }

    public override void OnExit(CameraFollow cameraFollow)
    {
        isRunning = false;
        progress = 0f;

        // Restore original FOV
        Camera camera = cameraFollow.GetCamera();
        if (camera != null && useFOVTransition)
        {
            camera.fieldOfView = originalFOV;
        }
    }

    public override Vector3 GetTargetPosition(CameraFollow cameraFollow)
    {
        if (startPoint == null || endPoint == null)
        {
            Camera camera = cameraFollow.GetCamera();
            return camera != null ? camera.transform.position : cameraFollow.transform.position;
        }

        float curvedProgress = previewCurve.Evaluate(Mathf.Clamp01(progress));

        // Tính world position của start và end (localPosition = world vì parent có scale)
        Vector3 startWorldPos = startPoint.localPosition + startPoint.forward * OffsetBegin;
        Vector3 endWorldPos = endPoint.localPosition - endPoint.forward * OffsetEnd;

        return CalculateCameraPosition(curvedProgress, startWorldPos, endWorldPos);
    }

    public bool IsComplete()
    {
        return progress >= 1f;
    }

    public void ResetProgress()
    {
        progress = 0f;
        isRunning = false;
    }

    /// <summary>
    /// Tính toán camera position dựa trên waypoints. Nếu không có waypoints, dùng offset mặc định
    /// </summary>
    public Vector3 CalculateCameraPosition(float normalizedProgress, Vector3 startWorldPos, Vector3 endWorldPos)
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            // Không có waypoints, dùng offset mặc định
            Vector3 pathPos = Vector3.Lerp(endWorldPos, startWorldPos, normalizedProgress);
            Vector3 offsetWorld = startPoint.TransformDirection(offsetFromPath);
            return pathPos + offsetWorld;
        }

        // Có waypoints: tìm 2 waypoints gần nhất và interpolate
        return InterpolatePosition(normalizedProgress, startWorldPos, endWorldPos);
    }

    /// <summary>
    /// Tính toán camera rotation dựa trên waypoints
    /// </summary>
    public Quaternion CalculateCameraRotation(float normalizedProgress)
    {
        if (useRotationOffset)
        {
            return Quaternion.Euler(rotationOffset);
        }

        if (waypoints == null || waypoints.Length == 0)
        {
            return Quaternion.identity;
        }

        return InterpolateRotation(normalizedProgress);
    }

    /// <summary>
    /// Tính toán FOV dựa trên waypoints
    /// </summary>
    public float CalculateFOV(float normalizedProgress)
    {
        // Ưu tiên FOV transition toàn cục
        if (useFOVTransition)
        {
            float fovProgress = fovCurve.Evaluate(normalizedProgress);
            return Mathf.Lerp(fovStart, fovEnd, fovProgress);
        }

        // Nếu không có FOV transition toàn cục, dùng waypoints
        if (waypoints == null || waypoints.Length == 0)
        {
            return 60f; // Default FOV
        }

        return InterpolateFOV(normalizedProgress);
    }

    private Vector3 InterpolatePosition(float t, Vector3 startWorldPos, Vector3 endWorldPos)
    {
        // Camera di chuyển tuần tự qua các waypoint: waypoint[0] → waypoint[1] → ... → waypoint[n]
        // t = 0: waypoint[0], t = 1: waypoint[n-1]

        if (waypoints.Length == 1)
        {
            // Chỉ có 1 waypoint, giữ nguyên vị trí
            return waypoints[0].GetCameraPosition(startWorldPos, endWorldPos, startPoint);
        }

        // Tính toán segment hiện tại
        int totalSegments = waypoints.Length - 1;
        float segmentLength = 1f / totalSegments;
        int currentSegment = Mathf.FloorToInt(t / segmentLength);

        // Clamp để tránh index out of range
        currentSegment = Mathf.Clamp(currentSegment, 0, totalSegments - 1);

        // Tính local t trong segment hiện tại (0 → 1)
        float segmentT = (t - currentSegment * segmentLength) / segmentLength;
        segmentT = Mathf.Clamp01(segmentT);

        // Lấy 2 waypoint của segment
        TrackPreviewWaypoint fromWaypoint = waypoints[currentSegment];
        TrackPreviewWaypoint toWaypoint = waypoints[currentSegment + 1];

        // Tính position
        Vector3 fromPos = fromWaypoint.GetCameraPosition(startWorldPos, endWorldPos, startPoint);
        Vector3 toPos = toWaypoint.GetCameraPosition(startWorldPos, endWorldPos, startPoint);

        // Áp dụng transition curve của waypoint đích
        AnimationCurve curve = toWaypoint.transitionCurve;
        float curvedT = curve.Evaluate(segmentT);

        return Vector3.Lerp(fromPos, toPos, curvedT);
    }

    private Quaternion InterpolateRotation(float t)
    {
        // Camera rotation tuần tự qua các waypoint có useCustomRotation
        // Tìm các waypoint có custom rotation
        int fromIndex = -1;
        int toIndex = -1;

        // Tính segment hiện tại dựa trên tổng số waypoints
        int totalSegments = waypoints.Length - 1;
        float segmentLength = 1f / totalSegments;
        int currentSegment = Mathf.Clamp(Mathf.FloorToInt(t / segmentLength), 0, totalSegments - 1);

        // Tìm waypoint có custom rotation gần nhất từ currentSegment trở về trước
        for (int i = currentSegment; i >= 0; i--)
        {
            if (waypoints[i].useCustomRotation)
            {
                fromIndex = i;
                break;
            }
        }

        // Tìm waypoint có custom rotation gần nhất từ currentSegment+1 trở đi
        for (int i = currentSegment + 1; i < waypoints.Length; i++)
        {
            if (waypoints[i].useCustomRotation)
            {
                toIndex = i;
                break;
            }
        }

        // Xử lý các trường hợp
        if (fromIndex == -1 && toIndex == -1)
            return Quaternion.identity;

        if (fromIndex == -1)
            return waypoints[toIndex].GetCameraRotation();

        if (toIndex == -1)
            return waypoints[fromIndex].GetCameraRotation();

        // Interpolate giữa 2 rotation
        float fromProgress = fromIndex * segmentLength;
        float toProgress = toIndex * segmentLength;
        float segmentT = (t - fromProgress) / (toProgress - fromProgress);
        segmentT = Mathf.Clamp01(segmentT);

        return Quaternion.Slerp(
            waypoints[fromIndex].GetCameraRotation(),
            waypoints[toIndex].GetCameraRotation(),
            segmentT
        );
    }

    private float InterpolateFOV(float t)
    {
        // Camera FOV tuần tự qua các waypoint có useCustomFOV
        int fromIndex = -1;
        int toIndex = -1;

        // Tính segment hiện tại dựa trên tổng số waypoints
        int totalSegments = waypoints.Length - 1;
        float segmentLength = 1f / totalSegments;
        int currentSegment = Mathf.Clamp(Mathf.FloorToInt(t / segmentLength), 0, totalSegments - 1);

        // Tìm waypoint có custom FOV gần nhất từ currentSegment trở về trước
        for (int i = currentSegment; i >= 0; i--)
        {
            if (waypoints[i].useCustomFOV)
            {
                fromIndex = i;
                break;
            }
        }

        // Tìm waypoint có custom FOV gần nhất từ currentSegment+1 trở đi
        for (int i = currentSegment + 1; i < waypoints.Length; i++)
        {
            if (waypoints[i].useCustomFOV)
            {
                toIndex = i;
                break;
            }
        }

        // Xử lý các trường hợp
        float fromFOV = fromIndex != -1 ? waypoints[fromIndex].GetFieldOfView() : 60f;
        float toFOV = toIndex != -1 ? waypoints[toIndex].GetFieldOfView() : 60f;

        if (fromIndex == -1 && toIndex == -1)
            return 60f;

        if (fromIndex == -1)
            return toFOV;

        if (toIndex == -1)
            return fromFOV;

        // Interpolate giữa 2 FOV
        float fromProgress = fromIndex * segmentLength;
        float toProgress = toIndex * segmentLength;
        float segmentT = (t - fromProgress) / (toProgress - fromProgress);
        segmentT = Mathf.Clamp01(segmentT);

        return Mathf.Lerp(fromFOV, toFOV, segmentT);
    }
}

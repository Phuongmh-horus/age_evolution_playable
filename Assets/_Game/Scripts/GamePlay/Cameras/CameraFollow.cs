using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Camera controller using State Pattern for managing different camera behaviors.
/// Supports smooth transitions between states in LateUpdate for dynamic target following.
/// Playable-safe: no Alchemy, no MonoSingleton.
/// </summary>
[DisallowMultipleComponent]
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [System.Serializable]
    public enum CameraStateName
    {
        TrackPreview,
        Waiting,
        FollowPlayer,
        Finish,
        FollowPlayerBeforeWin,
        LoseState,
    }

    [System.Serializable]
    public enum TransitionMode
    {
        Instant,
        Smooth
    }

    [SerializeField] private Camera mainCamera;

    [Header("Transition Settings")]
    [SerializeField] private TransitionMode transitionMode = TransitionMode.Smooth;
    [SerializeField] private float transitionDuration = 1f;

    [Header("Camera States")]
    [SerializeField] private List<CameraState> cameraStates = new List<CameraState>();

    [Header("Runtime")]
    [SerializeField] private CameraStateName defaultStateName = CameraStateName.Waiting;
    [SerializeField] private CameraState currentState;

    private bool isTransitioning = false;
    private CameraState previousState;

    private Dictionary<CameraStateName, CameraState> cameraStatesCache = new Dictionary<CameraStateName, CameraState>();

    private float transitionElapsedTime = 0f;
    private Vector3 transitionStartPosition;
    private Quaternion transitionStartRotation;
    private float transitionStartFOV;

    private Vector3 _cachedCapacityBarWorldPos;
    private int _capacityBarWorldPosFrame = -1;

    [Header("Capacity Bar Target")]
    [SerializeField] private float capacityBarPlaneY = 0f;

    [Header("Track Preview Path (Scene References)")]
    [SerializeField] private Transform trackPreviewStartPoint;
    [SerializeField] private Transform trackPreviewEndPoint;

#if UNITY_EDITOR
    [Header("Debug Visualization")]
    [SerializeField] private bool showCameraPath = true;
    [SerializeField] private bool showAllCameraPaths = false;
#endif

    private void Reset()
    {
        if (!mainCamera)
            Debug.LogWarning($"[CameraFollow] Missing mainCamera on {name}. Assign in Inspector.");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!mainCamera)
            Debug.LogWarning($"[CameraFollow] Missing mainCamera on {name}. Assign in Inspector.");
        RebuildCameraStatesCache();
    }
#endif

    private void Awake()
    {
        // Simple singleton for playable
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (!mainCamera)
        {
            Debug.LogError($"[CameraFollow] Missing mainCamera on {name}. Assign in Inspector.");
            enabled = false;
            return;
        }

        RebuildCameraStatesCache();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        InjectTrackPreviewPoints();

        // Force start in FollowPlayer instead of Waiting (Luna build requirement).
        if (currentState == null || currentState.CameraStateName == CameraStateName.Waiting)
        {
            var followState = GetStateByName(CameraStateName.FollowPlayer);
            if (followState != null)
            {
                currentState = followState;
                defaultStateName = CameraStateName.FollowPlayer;
            }
        }

        if (currentState != null)
        {
            SetCameraState(currentState, TransitionMode.Instant);
        }
        else
        {
            CameraState defaultState = GetStateByName(defaultStateName);
            if (defaultState != null)
            {
                SetCameraState(defaultState, TransitionMode.Instant);
            }
        }
    }

    private void InjectTrackPreviewPoints()
    {
        if (trackPreviewStartPoint == null || trackPreviewEndPoint == null)
            return;

        for (int i = 0; i < cameraStates.Count; i++)
        {
            var state = cameraStates[i];
            if (state is TrackPreviewCameraState trackState)
            {
                if (trackState.startPoint == null)
                    trackState.startPoint = trackPreviewStartPoint;

                if (trackState.endPoint == null)
                    trackState.endPoint = trackPreviewEndPoint;
            }
        }
    }

    private void RebuildCameraStatesCache()
    {
        cameraStatesCache.Clear();
        for (int i = 0; i < cameraStates.Count; i++)
        {
            var state = cameraStates[i];
            if (state == null) continue;

            if (!cameraStatesCache.ContainsKey(state.CameraStateName))
            {
                cameraStatesCache[state.CameraStateName] = state;
            }
        }
    }

    private void LateUpdate()
    {
        if (currentState == null) return;

        if (mainCamera == null) return;

        if (isTransitioning)
        {
            transitionElapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(transitionElapsedTime / Mathf.Max(0.0001f, transitionDuration));

            // OutQuart
            float easedT = 1f - Mathf.Pow(1f - t, 4f);

            Vector3 targetPosition = currentState.GetTargetPosition(this);
            Quaternion targetRotation = currentState.GetTargetRotation(this);
            float targetFOV = currentState.GetTargetFOV();

            mainCamera.transform.position = Vector3.Lerp(transitionStartPosition, targetPosition, easedT);
            mainCamera.transform.rotation = Quaternion.Slerp(transitionStartRotation, targetRotation, easedT);
            mainCamera.fieldOfView = Mathf.Lerp(transitionStartFOV, targetFOV, easedT);

            if (t >= 1f)
            {
                isTransitioning = false;
                transitionElapsedTime = 0f;
            }
        }
        else
        {
            currentState.OnUpdate(this);

            mainCamera.fieldOfView = Mathf.Lerp(
                mainCamera.fieldOfView,
                currentState.GetTargetFOV(),
                Time.deltaTime * 5f
            );
        }
    }

    public void SetCameraState(CameraState newState, TransitionMode mode = TransitionMode.Smooth)
    {
        if (newState == null)
        {
            Debug.LogWarning("Attempting to set null camera state!");
            return;
        }

        if (currentState == newState && !isTransitioning)
            return;

        previousState = currentState;
        previousState?.OnExit(this);

        currentState = newState;
        transitionMode = mode;

        if (mainCamera == null)
            return;

        if (mode == TransitionMode.Instant)
        {
            isTransitioning = false;
            currentState.OnEnter(this, mode);
            ApplyTargetTransform();
        }
        else
        {
            StartSmoothTransition(newState);
        }
    }

    private void ApplyTargetTransform()
    {
        if (currentState == null) return;

        if (mainCamera == null) return;

        mainCamera.transform.position = currentState.GetTargetPosition(this);
        mainCamera.transform.rotation = currentState.GetTargetRotation(this);
        mainCamera.fieldOfView = currentState.GetTargetFOV();
    }

    private void StartSmoothTransition(CameraState targetState)
    {
        if (mainCamera == null)
        {
            // No camera -> fallback instant
            isTransitioning = false;
            targetState.OnEnter(this, TransitionMode.Instant);
            return;
        }

        transitionStartPosition = mainCamera.transform.position;
        transitionStartRotation = mainCamera.transform.rotation;
        transitionStartFOV = mainCamera.fieldOfView;

        transitionElapsedTime = 0f;
        isTransitioning = true;

        targetState.OnEnter(this, transitionMode);
    }

    #region Public Helper Methods

    public void SetTransitionMode(TransitionMode mode) => transitionMode = mode;

    public void SetTransitionDuration(float duration) => transitionDuration = Mathf.Max(0.1f, duration);

    public CameraState GetCurrentState() => currentState;

    public CameraStateName? GetCurrentStateName()
    {
        if (currentState == null) return null;
        return currentState.CameraStateName;
    }

    public bool IsTransitioning() => isTransitioning;

    public Camera GetCamera() => mainCamera;

    public void SetPlayerTransform(Transform player)
    {
        bool foundFollowState = false;
        for (int i = 0; i < cameraStates.Count; i++)
        {
            if (cameraStates[i] is FollowPlayerCameraState followState)
            {
                followState.SetPlayerTransform(player);
                foundFollowState = true;
            }
        }
        if (!foundFollowState) Debug.LogWarning("[CameraFollow] No FollowPlayerCameraState found in list!");

        // If we're already in FollowPlayer, snap to the correct offset immediately.
        if (currentState is FollowPlayerCameraState && !isTransitioning)
        {
            var cam = mainCamera;
            if (cam != null)
            {
                cam.transform.position = currentState.GetTargetPosition(this);
                cam.transform.rotation = currentState.GetTargetRotation(this);
                cam.fieldOfView = currentState.GetTargetFOV();
            }
        }
    }

    /// <summary>
    /// Playable-safe:
    /// - If UI event is missing -> use screen center as fallback.
    /// </summary>
    public Vector3 GetCapacityBarWorldPosition()
    {
        if (_capacityBarWorldPosFrame == Time.frameCount)
            return _cachedCapacityBarWorldPos;

        _capacityBarWorldPosFrame = Time.frameCount;

        var cam = mainCamera;
        if (cam == null)
        {
            _cachedCapacityBarWorldPos = transform.position + transform.forward * 5f;
            return _cachedCapacityBarWorldPos;
        }

        Vector3 screenPos = Vector3.zero;
        bool hasUI = GameEventBus.GetCapacityBarPosition != null;
        
        if (hasUI)
        {
            screenPos = GameEventBus.GetCapacityBarPosition.Invoke();
             // Simple validation: If screenPos is (0,0), it's likely uninitialized. Fallback to center.
            if (screenPos == Vector3.zero) hasUI = false;
        }

        if (!hasUI)
        {
            screenPos = new Vector3(Screen.width * 0.1f, Screen.height * 0.5f, 0f); // Fallback: Left side of screen
        }

        // Playable adaptation: Fly towards the screen!
        float distance = 10f;
        if (GameplayManager.Instance != null && GameplayManager.Instance.Turnable != null)
        {
             Plane cameraPlane = new Plane(cam.transform.forward, cam.transform.position);
             distance = cameraPlane.GetDistanceToPoint(GameplayManager.Instance.Turnable.Transform.position);
             distance = Mathf.Abs(distance); 
        }

        // IMPORTANT: We must retain the Screen X/Y from the UI, but inject the Depth Z.
        // ScreenToWorldPoint(x, y, z) -> Z is depth from camera.
        screenPos.z = Mathf.Max(5f, distance); 
        
        _cachedCapacityBarWorldPos = cam.ScreenToWorldPoint(screenPos);
        
        return _cachedCapacityBarWorldPos;
    }

    #endregion

    #region State Management

    public CameraState GetStateByName(CameraStateName stateName)
    {
        if (cameraStatesCache.Count == 0)
            RebuildCameraStatesCache();

        if (cameraStatesCache.TryGetValue(stateName, out CameraState state))
            return state;

        Debug.LogWarning($"Camera state '{stateName}' not found!");
        return null;
    }

    public void SetStateByName(CameraStateName stateName, TransitionMode mode = TransitionMode.Smooth)
    {
        CameraState state = GetStateByName(stateName);
        if (state != null)
            SetCameraState(state, mode);
    }

    public bool HasState(CameraStateName stateName)
    {
        if (cameraStatesCache.Count == 0)
            RebuildCameraStatesCache();

        return cameraStatesCache.ContainsKey(stateName);
    }

    public List<CameraStateName> GetAllStateNames()
    {
        if (cameraStatesCache.Count == 0)
            RebuildCameraStatesCache();

        return new List<CameraStateName>(cameraStatesCache.Keys);
    }

    public void AddState(CameraState state)
    {
        if (state == null)
        {
            Debug.LogWarning("Cannot add null camera state!");
            return;
        }

        if (HasState(state.CameraStateName))
        {
            Debug.LogWarning($"Camera state '{state.CameraStateName}' already exists!");
            return;
        }

        cameraStates.Add(state);
        cameraStatesCache[state.CameraStateName] = state;
    }

    public void RemoveState(CameraStateName stateName)
    {
        for (int i = cameraStates.Count - 1; i >= 0; i--)
        {
            if (cameraStates[i] != null && cameraStates[i].CameraStateName == stateName)
            {
                cameraStates.RemoveAt(i);
                break;
            }
        }

        if (cameraStatesCache.ContainsKey(stateName))
            cameraStatesCache.Remove(stateName);
    }

    public bool IsCurrentStateOfType<T>() where T : CameraState => currentState is T;

    public bool IsStateComplete()
    {
        if (currentState is TrackPreviewCameraState previewState)
            return previewState.IsComplete();
        return false;
    }

#if UNITY_EDITOR
    // Giữ nguyên debug gizmos như cũ (nằm trong UNITY_EDITOR nên không ảnh hưởng Luna build)
    private void OnDrawGizmos()
    {
        if (!showCameraPath) return;

        if (currentState is TrackPreviewCameraState previewState)
        {
            DrawTrackPreviewPath(previewState, true);
        }

        if (showAllCameraPaths)
        {
            foreach (var state in cameraStates)
            {
                if (state is TrackPreviewCameraState trackState)
                {
                    DrawTrackPreviewPath(trackState, trackState == currentState);
                }
            }
        }
    }

    private void DrawTrackPreviewPath(TrackPreviewCameraState state, bool isActive)
    {
        if (state == null) return;

        Transform startPoint = state.startPoint != null ? state.startPoint : trackPreviewStartPoint;
        Transform endPoint = state.endPoint != null ? state.endPoint : trackPreviewEndPoint;

        if (startPoint == null || endPoint == null) return;

        int resolution = 50;
        Color pathColorToUse = isActive ? new Color(0f, 1f, 0.5f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);

        Vector3 previousPoint = Vector3.zero;

        Vector3 startWorldPos = startPoint.localPosition + startPoint.forward * state.OffsetBegin;
        Vector3 endWorldPos = endPoint.localPosition - endPoint.forward * state.OffsetEnd;

        for (int i = 0; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            float curvedT = state.previewCurve.Evaluate(t);

            Vector3 pathPositionWorld = Vector3.Lerp(endWorldPos, startWorldPos, curvedT);

            Vector3 offsetWorld = startPoint.TransformDirection(state.offsetFromPath);
            Vector3 cameraPositionWorld = pathPositionWorld + offsetWorld;

            if (i > 0)
            {
                Gizmos.color = pathColorToUse;
                Gizmos.DrawLine(previousPoint, cameraPositionWorld);
            }

            previousPoint = cameraPositionWorld;

            if (i % 10 == 0 && isActive)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(cameraPositionWorld, 0.2f);
            }
        }
    }
#endif

    #endregion
}

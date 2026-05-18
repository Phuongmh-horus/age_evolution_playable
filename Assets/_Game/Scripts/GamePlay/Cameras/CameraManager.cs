using UnityEngine;
using UnityEngine.Events;

public class CameraManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CameraFollow cameraFollow;

    [Header("Auto Transition Settings")]
    [SerializeField] private bool autoTransitionAfterPreview = true;
    [SerializeField] private CameraFollow.CameraStateName stateAfterTrackPreview = CameraFollow.CameraStateName.Waiting;

    [Header("Events")]
    public UnityEvent<CameraFollow.CameraStateName> OnCameraStateChanged;
    public UnityEvent OnTrackPreviewComplete;

    private Transform _pendingPlayerTransform;

    private static CameraManager instance;
    public static CameraManager Instance
    {
        get
        {
            return instance;
        }
    }

    private void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Destroy(gameObject);
    }

    private void Start()
    {
        if (cameraFollow == null)
            cameraFollow = CameraFollow.Instance;

        if (cameraFollow == null)
        {
            Debug.LogWarning("[CameraManager] CameraFollow is NULL. Assign via Inspector for best performance.");
        }

        if (cameraFollow != null && _pendingPlayerTransform != null)
        {
            cameraFollow.SetPlayerTransform(_pendingPlayerTransform);
            _pendingPlayerTransform = null;
        }
    }

    private void Update()
    {
        CheckTrackPreviewCompletion();
    }

    #region State Control Methods
    public void StartTrackPreview(CameraFollow.TransitionMode mode = CameraFollow.TransitionMode.Smooth)
    {
        SetCameraStateByName(CameraFollow.CameraStateName.TrackPreview, mode);
    }

    public void ShowWaitingView(CameraFollow.TransitionMode mode = CameraFollow.TransitionMode.Smooth)
    {
        SetCameraStateByName(CameraFollow.CameraStateName.Waiting, mode);
    }

    public void StartFollowingPlayer(CameraFollow.TransitionMode mode = CameraFollow.TransitionMode.Smooth)
    {
        SetCameraStateByName(CameraFollow.CameraStateName.FollowPlayer, mode);
    }

    public void ShowFinishPreview(CameraFollow.TransitionMode mode = CameraFollow.TransitionMode.Smooth)
    {
        SetCameraStateByName(CameraFollow.CameraStateName.Finish, mode);
    }

    public void SetCameraStateByName(CameraFollow.CameraStateName stateName, CameraFollow.TransitionMode mode = CameraFollow.TransitionMode.Smooth)
    {
        if (cameraFollow != null)
        {
            cameraFollow.SetStateByName(stateName, mode);
            OnCameraStateChanged?.Invoke(stateName);
        }
    }

    public void SetCustomCameraState(CameraState state, CameraFollow.TransitionMode mode = CameraFollow.TransitionMode.Smooth)
    {
        if (cameraFollow != null && state != null)
        {
            cameraFollow.SetCameraState(state, mode);
            var stateName = cameraFollow.GetCurrentStateName();
            if (stateName.HasValue)
            {
                OnCameraStateChanged?.Invoke(stateName.Value);
            }
        }
    }
    #endregion

    #region Utility Methods
    public void SetPlayerTransform(Transform playerTransform)
    {
        if (cameraFollow != null)
        {
            cameraFollow.SetPlayerTransform(playerTransform);
        }
        else
        {
            _pendingPlayerTransform = playerTransform;
        }
    }

    public void SetTransitionDuration(float duration)
    {
        if (cameraFollow != null)
            cameraFollow.SetTransitionDuration(duration);
    }

    public CameraFollow.CameraStateName? GetCurrentStateName()
    {
        return cameraFollow != null ? cameraFollow.GetCurrentStateName() : null;
    }

    public CameraState GetCurrentCameraState()
    {
        return cameraFollow != null ? cameraFollow.GetCurrentState() : null;
    }

    public bool IsCameraTransitioning()
    {
        return cameraFollow != null && cameraFollow.IsTransitioning();
    }

    public CameraFollow GetCameraFollow()
    {
        return cameraFollow;
    }

    public bool HasState(CameraFollow.CameraStateName stateName)
    {
        return cameraFollow != null && cameraFollow.HasState(stateName);
    }
    #endregion

    #region Auto Transition
    private void CheckTrackPreviewCompletion()
    {
        if (!autoTransitionAfterPreview)
            return;

        if (cameraFollow == null)
            return;

        if (cameraFollow.IsStateComplete() && !cameraFollow.IsTransitioning())
        {
            OnTrackPreviewComplete?.Invoke();
            SetCameraStateByName(stateAfterTrackPreview, CameraFollow.TransitionMode.Smooth);
            autoTransitionAfterPreview = false;
        }
    }

    public void EnableAutoTransitionAfterPreview(CameraFollow.CameraStateName targetStateName)
    {
        autoTransitionAfterPreview = true;
        stateAfterTrackPreview = targetStateName;
    }
    #endregion

    #region Game Flow Methods
    public void OnGameStart()
    {
        StartFollowingPlayer(CameraFollow.TransitionMode.Smooth);
    }

    public void OnGamePause()
    {
        // Camera continues in current state
    }

    public void OnGameResume()
    {
        // Camera continues in current state
    }

    public void OnGameEnd()
    {
        ShowFinishPreview(CameraFollow.TransitionMode.Smooth);
    }

    public void OnGameRestart()
    {
        ShowWaitingView(CameraFollow.TransitionMode.Instant);
    }
    #endregion
}

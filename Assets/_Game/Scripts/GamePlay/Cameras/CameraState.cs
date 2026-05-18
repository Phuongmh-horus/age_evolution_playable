using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public abstract class CameraState : ScriptableObject
{
#if UNITY_EDITOR
    [ContextMenu("Select In Project")]
    private void SelectInProject()
    {
        EditorGUIUtility.PingObject(this);
        Selection.activeObject = this;
    }
#endif

    [Header("Base Settings")]
    public CameraFollow.CameraStateName CameraStateName;

    [Header("Field of View")]
    public float fieldOfView = 60f;

    public abstract void OnEnter(CameraFollow cameraFollow, CameraFollow.TransitionMode transitionMode);
    public abstract void OnUpdate(CameraFollow cameraFollow);
    public abstract void OnExit(CameraFollow cameraFollow);

    public virtual Vector3 GetTargetPosition(CameraFollow cameraFollow)
    {
        Camera camera = cameraFollow.GetCamera();
        return camera != null ? camera.transform.position : cameraFollow.transform.position;
    }

    public virtual Quaternion GetTargetRotation(CameraFollow cameraFollow)
    {
        Camera camera = cameraFollow.GetCamera();
        return camera != null ? camera.transform.rotation : cameraFollow.transform.rotation;
    }

    public virtual float GetTargetFOV()
    {
        return fieldOfView;
    }
}

using System;
using UnityEngine;

[CreateAssetMenu(fileName = "StaticCameraState", menuName = "Camera/Static Camera State")]
public class StaticCameraState : CameraState
{
    [Header("Static Position")]
    public Vector3 position;
    public Vector3 rotation;

    [Header("Look At Settings")]
    public bool useCustomLookAt = false;
    public Transform lookAtTarget;
    public Vector3 lookAtOffset = Vector3.zero;

    public override void OnEnter(CameraFollow cameraFollow, CameraFollow.TransitionMode transitionMode)
    {
        Camera camera = cameraFollow.GetCamera();
        if (camera != null && !useCustomLookAt)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.Euler(rotation);
        }
    }

    public override void OnUpdate(CameraFollow cameraFollow)
    {
        if (useCustomLookAt && lookAtTarget != null)
        {
            Camera camera = cameraFollow.GetCamera();
            if (camera != null)
            {
                Vector3 lookPos = lookAtTarget.position + lookAtOffset;
                camera.transform.localPosition = lookPos;
                camera.transform.localRotation = Quaternion.Euler(rotation);
            }
        }
    }

    public override void OnExit(CameraFollow cameraFollow)
    {
        // No cleanup needed
    }

    public override Vector3 GetTargetPosition(CameraFollow cameraFollow)
    {
        if (useCustomLookAt && lookAtTarget != null)
        {
            Camera camera = cameraFollow.GetCamera();
            if (camera != null)
            {
                Vector3 lookPos = lookAtTarget.position + lookAtOffset;
                return lookPos;
            }
        }
        return position;
    }

    public override Quaternion GetTargetRotation(CameraFollow cameraFollow)
    {
        return Quaternion.Euler(rotation);
    }

    public void SetTargetTransform(Transform player)
    {
        lookAtTarget = player;
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

using GamePlay.Entities;

public class BrickLayer : PoolEntity
{
    public int layerIndex;
    public float radius;
    public List<CapacityBrick> bricks = new List<CapacityBrick>();

    public bool isActivated = false;
    public bool isCached = false;

    private void OnEnable()
    {
        if (!isCached) return;

        foreach (var brick in bricks)
        {
            if (brick == null || brick.isActiveAndEnabled) continue;
            brick.gameObject.SetActive(true);
        }

        isCached = false;
    }

//     private void Update()
//     {
//         if (!isActivated) return;
//         for (int index = bricks.Count - 1; index >= 0; index--)
//         {
//             if (!bricks[index].isActivated) return;
//             bricks[index].UpdateFall();
//         }
//     }

    public void ResetLayer(bool forceResetFlying = false)
    {
        foreach (var brick in bricks)
        {
            if (brick == null) continue;

            var motion = brick.brickFallMotion;
            bool isFlying = motion != null && motion.IsActivated();
            if (motion != null && (forceResetFlying || !isFlying))
                motion.ResetBrick();

            brick.isActivated = false;
            if (!isFlying && !brick.gameObject.activeSelf)
                brick.gameObject.SetActive(true);
        }
    }
}

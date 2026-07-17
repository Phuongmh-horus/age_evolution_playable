using System.Collections;
using UnityEngine;

public abstract class CullingObject : MonoBehaviour
{
    [Tooltip("If true, the system will update this object's grid cell representation as it moves.")]
    [SerializeField] protected bool isDynamic = false;
    
    [Tooltip("Override culling distance for this specific object. Set to -1 to use system default.")]
    [SerializeField] protected float customCullDistance = -1f;

    public bool IsDynamic
    {
        get => isDynamic;
        set
        {
            if (isDynamic == value) return;
            isDynamic = value;
            if (CullingSystem.Instance != null)
            {
                if (isDynamic)
                    CullingSystem.Instance.AddDynamicObject(this);
                else
                    CullingSystem.Instance.RemoveDynamicObject(this);
            }
        }
    }
    public float CustomCullDistance => customCullDistance;
    public CullingCell CurrentCell { get; set; }
    public bool IsCulled { get; private set; } = false;

    protected bool isDisablingDueToCulling = false;

    protected virtual void OnEnable()
    {
        // If we are already mapped to a cell, we don't need to re-register.
        // This prevents double registration when SetActive(true) is called.
        if (CurrentCell != null) return;

        if (CullingSystem.Instance != null)
        {
            CullingSystem.Instance.Register(this);
        }
        else StartCoroutine(TryRegister());
    }

    private IEnumerator TryRegister()
    {
        for (int i = 0, count = 3; i < count; i++)
        {
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            
            if (CullingSystem.Instance != null)
            {
                CullingSystem.Instance.Register(this);
                yield break;
            }
        }
    }

    protected virtual void OnDisable()
    {
        // If this OnDisable is triggered by the culling process deactivating the GameObject,
        // keep the registration in the cell so the system can wake it up later.
        if (isDisablingDueToCulling)
        {
            return;
        }

        if (CullingSystem.Instance != null)
        {
            CullingSystem.Instance.Unregister(this);
        }
    }

    public void SetCulled(bool culled)
    {
        if (IsCulled == culled) return;
        IsCulled = culled;
        
        isDisablingDueToCulling = culled;
        ApplyCulling(IsCulled);
        isDisablingDueToCulling = false;
    }

    public void ForceSetCulled(bool culled)
    {
        IsCulled = culled;
        
        isDisablingDueToCulling = culled;
        ApplyCulling(IsCulled);
        isDisablingDueToCulling = false;
    }

    /// <summary>
    /// Apply the culling logic: either enable/disable rendering, GameObject activity, behaviour state, or custom event logic.
    /// </summary>
    /// <param name="culled">True if the object is too far and should be hidden/culled, False if it is close and should be shown.</param>
    protected abstract void ApplyCulling(bool culled);
}

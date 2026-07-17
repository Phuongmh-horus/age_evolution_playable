using System.Collections.Generic;
using UnityEngine;

public class MeshRendererCullingObject : CullingObject
{
    [SerializeField] private List<Renderer> targetRenderers = new List<Renderer>();

    [ContextMenu("Collects")]
    private void Start()
    {
        if (targetRenderers == null || targetRenderers.Count == 0)
        {
            targetRenderers = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
        }
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    protected override void ApplyCulling(bool culled)
    {
        if (targetRenderers == null) return;

        for (int i = 0; i < targetRenderers.Count; i++)
        {
            if (targetRenderers[i] != null)
            {
                targetRenderers[i].enabled = !culled;
            }
        }
    }
}

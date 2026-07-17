using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CullingSystem))]
public class CullingSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CullingSystem system = (CullingSystem)target;

        GUILayout.Space(15);
        if (GUILayout.Button("Try Refresh Culling (Editor Detect)", GUILayout.Height(30)))
        {
            system.EditorTryRefresh();
        }
    }
}

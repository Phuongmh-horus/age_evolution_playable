using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.UI;

[AddComponentMenu("UI/Effects/Gradient")]
public class UIGradient : BaseMeshEffect
{
    public Color32 topColor = Color.white;
    public Color32 bottomColor = Color.black;

    [SerializeField] private Color _topColor = Color.white;
    [SerializeField] private Color _bottomColor = Color.black;

    public override void ModifyMesh(VertexHelper helper)
    {
        if (!IsActive() || helper.currentVertCount == 0)
            return;

        var vertices = new List<UIVertex>();
        helper.GetUIVertexStream(vertices);

        var bottomY = vertices[0].position.y;
        var topY = vertices[0].position.y;

        for (var i = 1; i < vertices.Count; i++)
        {
            var y = vertices[i].position.y;
            if (y > topY)
            {
                topY = y;
            }
            else if (y < bottomY)
            {
                bottomY = y;
            }
        }

        var uiElementHeight = topY - bottomY;

        var v = new UIVertex();

        for (var i = 0; i < helper.currentVertCount; i++)
        {
            helper.PopulateUIVertex(ref v, i);
            v.color = Color32.Lerp(_bottomColor, _topColor, (v.position.y - bottomY) / uiElementHeight);
            helper.SetUIVertex(v, i);
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(UIGradient))]
public class GradientUIEditor : Editor
{
    private SerializedProperty _topColor;
    private SerializedProperty _bottomColor;

    private void OnEnable()
    {
        _topColor = serializedObject.FindProperty("_topColor");
        _bottomColor = serializedObject.FindProperty("_bottomColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(_topColor);
        EditorGUILayout.PropertyField(_bottomColor);

        if (GUILayout.Button("Restore setup"))
        {
            foreach (var script in targets)
            {
                var s = script as UIGradient;
                if (s != null)
                {
                    _topColor.colorValue = s.topColor;
                    _bottomColor.colorValue = s.bottomColor;
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
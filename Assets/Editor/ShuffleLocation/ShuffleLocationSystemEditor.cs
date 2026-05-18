using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ShuffleLocationSystem))]
public class ShuffleLocationSystemEditor : Editor
{
    // ── Style cache ──────────────────────────────────────────────────────────
    private GUIStyle _headerStyle;
    private GUIStyle _sectionStyle;

    private GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        fontSize = 13,
        normal = { textColor = Color.white }
    };

    private GUIStyle SectionStyle => _sectionStyle ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontStyle = FontStyle.Bold,
        normal = { textColor = new Color(0.55f, 0.85f, 1f) }
    };

    // ════════════════════════════════════════════════════════════════════════
    //  Inspector
    // ════════════════════════════════════════════════════════════════════════

    public override void OnInspectorGUI()
    {
        var sys = (ShuffleLocationSystem)target;
        serializedObject.Update();

        DrawHeader("Shuffle Location System");

        // ── Enable toggle ────────────────────────────────────────────────────
        sys.enableShuffle = EditorGUILayout.Toggle("Enable System", sys.enableShuffle);

        if (!sys.enableShuffle)
        {
            serializedObject.ApplyModifiedProperties();
            MarkDirtyOnChange(sys);
            return;
        }

        EditorGUILayout.Space(5);

        // ── Base Settings ────────────────────────────────────────────────────
        DrawSectionLabel("Base Settings");
        sys.count = Mathf.Max(1, EditorGUILayout.IntField("Count", sys.count));
        sys.rangeSize = EditorGUILayout.Vector3Field("Range Size", sys.rangeSize);

        EditorGUILayout.Space(5);

        // ── Axis Lock ────────────────────────────────────────────────────────
        DrawSectionLabel("Axis Lock");
        EditorGUILayout.BeginHorizontal();
        sys.lockX = EditorGUILayout.ToggleLeft("Lock X", sys.lockX, GUILayout.Width(80));
        sys.lockY = EditorGUILayout.ToggleLeft("Lock Y", sys.lockY, GUILayout.Width(80));
        sys.lockZ = EditorGUILayout.ToggleLeft("Lock Z", sys.lockZ, GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // ── Row Alignment ────────────────────────────────────────────────────
        DrawSectionLabel("Row Alignment");
        sys.enableRowAlignment = EditorGUILayout.Toggle("Enable", sys.enableRowAlignment);
        if (sys.enableRowAlignment)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Row Axes");
            bool hasX = (sys.rowAxes & ShuffleLocationSystem.RowAxis.X) != 0;
            bool hasY = (sys.rowAxes & ShuffleLocationSystem.RowAxis.Y) != 0;
            bool hasZ = (sys.rowAxes & ShuffleLocationSystem.RowAxis.Z) != 0;
            hasX = EditorGUILayout.ToggleLeft("X", hasX, GUILayout.Width(55));
            hasY = EditorGUILayout.ToggleLeft("Y", hasY, GUILayout.Width(55));
            hasZ = EditorGUILayout.ToggleLeft("Z", hasZ, GUILayout.Width(55));
            sys.rowAxes = (hasX ? ShuffleLocationSystem.RowAxis.X : ShuffleLocationSystem.RowAxis.None)
                         | (hasY ? ShuffleLocationSystem.RowAxis.Y : ShuffleLocationSystem.RowAxis.None)
                         | (hasZ ? ShuffleLocationSystem.RowAxis.Z : ShuffleLocationSystem.RowAxis.None);
            EditorGUILayout.EndHorizontal();
            sys.rowChaos = EditorGUILayout.Vector3Field(
                new GUIContent("Row Chaos", "Per-axis chaos: 0 = clean grid, 1 = full random within cell. Only affects selected axes."),
                sys.rowChaos);
            sys.minAxisSpacing = EditorGUILayout.Vector3Field(
                new GUIContent("Min Axis Spacing", "Minimum distance between rows/cols per axis."),
                sys.minAxisSpacing);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(5);

        // ── Minimum Distance ─────────────────────────────────────────────────
        DrawSectionLabel("Minimum Distance");
        sys.enableMinDistance = EditorGUILayout.Toggle("Enable", sys.enableMinDistance);
        if (sys.enableMinDistance)
        {
            EditorGUI.indentLevel++;
            sys.minDistance = Mathf.Max(0.01f, EditorGUILayout.FloatField("Min Distance", sys.minDistance));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(5);

        // ── Prefab Random ────────────────────────────────────────────────────
        DrawSectionLabel("Prefab Random");
        sys.enablePrefabRandom = EditorGUILayout.Toggle("Enable", sys.enablePrefabRandom);
        if (sys.enablePrefabRandom)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("prefabList"),
                new GUIContent("Prefab List"),
                includeChildren: true);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(5);

        // ── Content Override ─────────────────────────────────────────────────
        DrawSectionLabel("Content Override");
        EditorGUILayout.PropertyField(serializedObject.FindProperty("contentData"), new GUIContent("Content Data"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("overrideRange"), new GUIContent("Override Range"));

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);

        // ── Actions ──────────────────────────────────────────────────────────
        DrawSectionLabel("Actions");

        if (GUILayout.Button("Generate", GUILayout.Height(30)))
        {
            Undo.RecordObject(sys, "Generate Shuffle Locations");
            sys.Generate();
            EditorUtility.SetDirty(sys);
            SceneView.RepaintAll();
        }

        using (new EditorGUI.DisabledGroupScope(!sys.CanOverrideContent()))
        {
            if (GUILayout.Button("Override Content Range", GUILayout.Height(30)))
            {
                Undo.RecordObject(sys.contentData, "Override Content Range");
                sys.OverrideContentRange();
                EditorUtility.SetDirty(sys.contentData);
                AssetDatabase.SaveAssets();
            }
        }

        bool canSpawn = sys.enablePrefabRandom
            && sys.prefabList != null
            && sys.prefabList.Count > 0;

        using (new EditorGUI.DisabledGroupScope(!canSpawn))
        {
            if (GUILayout.Button("Spawn Prefabs", GUILayout.Height(30)))
            {
                Undo.RecordObject(sys, "Spawn Shuffle Prefabs");
                sys.SpawnPrefabs();
                EditorUtility.SetDirty(sys);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Clear Spawned", GUILayout.Height(28)))
            {
                Undo.RecordObject(sys, "Clear Spawned");
                sys.ClearSpawned();
                EditorUtility.SetDirty(sys);
                SceneView.RepaintAll();
            }
        }

        EditorGUILayout.Space(4);

        // ── Status ───────────────────────────────────────────────────────────
        bool complete = sys.generatedLocations.Count == sys.count;
        MessageType msgType = complete ? MessageType.Info : MessageType.Warning;
        EditorGUILayout.HelpBox(
            $"Generated: {sys.generatedLocations.Count} / {sys.count}",
            msgType);

        MarkDirtyOnChange(sys);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void DrawHeader(string title)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 26);
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
        EditorGUI.LabelField(rect, title, HeaderStyle);
        EditorGUILayout.Space(2);
    }

    private void DrawSectionLabel(string label)
    {
        EditorGUILayout.LabelField(label, SectionStyle);
    }

    private static void MarkDirtyOnChange(UnityEngine.Object obj)
    {
        if (GUI.changed)
            EditorUtility.SetDirty(obj);
    }
}

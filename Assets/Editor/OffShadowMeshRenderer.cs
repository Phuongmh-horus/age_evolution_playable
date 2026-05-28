using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor Tool: Batch disable Cast Shadow / Receive Shadow on all
/// MeshRenderer & SkinnedMeshRenderer inside selected Prefab assets.
///
/// Safe approach:
///   - Uses PrefabUtility.LoadPrefabContents / SaveAsPrefabAsset
///     (edits the prefab file directly without instantiating into scene)
///   - Uses SerializedObject so Unity tracks dirty state correctly
///   - Supports nested prefabs and prefab variants
///   - Full Undo support via AssetDatabase
/// </summary>
public class ShadowSettingsTool : EditorWindow
{
    // ── UI state ──────────────────────────────────────────────────────────────
    private bool _disableCastShadow = true;
    private bool _disableReceiveShadow = true;
    private bool _includeChildren = true;
    private bool _includeVariants = false;
    private ShadowCastingMode _castMode = ShadowCastingMode.Off;

    private Vector2 _scroll;
    private List<string> _log = new();
    private bool _showLog = true;

    private GUIStyle _headerStyle;
    private GUIStyle _logStyle;
    private bool _stylesInit;

    // ── Menu entry ────────────────────────────────────────────────────────────
    [MenuItem("Tools/Shadow Settings Tool")]
    public static void ShowWindow()
    {
        var w = GetWindow<ShadowSettingsTool>("Shadow Settings Tool");
        w.minSize = new Vector2(400, 480);
        w.Show();
    }

    // ── GUI ───────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        InitStyles();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Shadow Settings Tool", _headerStyle);
        EditorGUILayout.LabelField("Batch edit Cast/Receive Shadow on Prefab assets", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space(10);

        DrawOptions();
        EditorGUILayout.Space(6);
        DrawTargetInfo();
        EditorGUILayout.Space(8);
        DrawActions();
        EditorGUILayout.Space(6);
        DrawLog();
    }

    private void DrawOptions()
    {
        using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        _disableCastShadow = EditorGUILayout.Toggle(
            new GUIContent("Cast Shadow", "Apply to Cast Shadows setting"),
            _disableCastShadow);

        if (_disableCastShadow)
        {
            EditorGUI.indentLevel++;
            _castMode = (ShadowCastingMode)EditorGUILayout.EnumPopup(
                new GUIContent("Cast Mode", "ShadowCastingMode to set"),
                _castMode);
            EditorGUI.indentLevel--;
        }

        _disableReceiveShadow = EditorGUILayout.Toggle(
            new GUIContent("Receive Shadows", "Apply to Receive Shadows setting"),
            _disableReceiveShadow);

        EditorGUILayout.Space(4);
        _includeChildren = EditorGUILayout.Toggle(
            new GUIContent("Include Children", "Process all renderers in hierarchy, not just root"),
            _includeChildren);

        _includeVariants = EditorGUILayout.Toggle(
            new GUIContent("Include Prefab Variants", "Also process Prefab Variants that reference selected prefabs"),
            _includeVariants);
    }

    private void DrawTargetInfo()
    {
        var prefabs = GetSelectedPrefabPaths();
        using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);

        if (prefabs.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Select one or more Prefab assets in the Project window.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField($"Selected Prefabs: {prefabs.Count}", EditorStyles.boldLabel);
            foreach (var p in prefabs.Take(6))
                EditorGUILayout.LabelField("  • " + Path.GetFileName(p), EditorStyles.miniLabel);
            if (prefabs.Count > 6)
                EditorGUILayout.LabelField($"  … and {prefabs.Count - 6} more", EditorStyles.miniLabel);
        }
    }

    private void DrawActions()
    {
        var prefabs = GetSelectedPrefabPaths();
        bool canRun = prefabs.Count > 0 && (_disableCastShadow || _disableReceiveShadow);

        GUI.enabled = canRun;
        if (GUILayout.Button("Apply to Selected Prefabs", GUILayout.Height(32)))
            Run(prefabs);
        GUI.enabled = true;

        if (!canRun && prefabs.Count > 0)
            EditorGUILayout.HelpBox("Enable at least one option (Cast or Receive Shadow).", MessageType.Warning);
    }

    private void DrawLog()
    {
        _showLog = EditorGUILayout.Foldout(_showLog, $"Log ({_log.Count} entries)", true);
        if (!_showLog) return;

        using var scrollView = new EditorGUILayout.ScrollViewScope(_scroll,
            GUILayout.Height(Mathf.Clamp(_log.Count * 16 + 20, 60, 200)));
        _scroll = scrollView.scrollPosition;

        foreach (var line in _log)
            EditorGUILayout.LabelField(line, _logStyle);

        if (_log.Count > 0)
        {
            EditorGUILayout.Space(2);
            if (GUILayout.Button("Clear Log", GUILayout.Width(80)))
                _log.Clear();
        }
    }

    // ── Core logic ────────────────────────────────────────────────────────────
    private void Run(List<string> prefabPaths)
    {
        _log.Clear();
        int modifiedPrefabs = 0;
        int modifiedRenderers = 0;

        // Collect variant paths if requested
        List<string> allPaths = new(prefabPaths);
        if (_includeVariants)
        {
            var variantPaths = FindVariantsOf(prefabPaths);
            foreach (var vp in variantPaths)
                if (!allPaths.Contains(vp))
                    allPaths.Add(vp);
        }

        // Register undo group so user can Ctrl+Z the whole batch
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Shadow Settings Tool - Batch Apply");

        AssetDatabase.StartAssetEditing(); // batch I/O for performance
        try
        {
            foreach (var path in allPaths)
            {
                int count = ProcessPrefab(path);
                if (count > 0)
                {
                    modifiedRenderers += count;
                    modifiedPrefabs++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Log($"Done — {modifiedPrefabs} prefab(s) modified, {modifiedRenderers} renderer(s) updated.");
        Repaint();
    }

    /// <summary>
    /// Load prefab contents in isolation, edit renderers, save back.
    /// Returns number of renderers modified.
    /// </summary>
    private int ProcessPrefab(string assetPath)
    {
        // LoadPrefabContents gives us an isolated root — no scene involvement
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        if (root == null)
        {
            Log($"[SKIP] Cannot load: {assetPath}");
            return 0;
        }

        try
        {
            var renderers = _includeChildren
                ? root.GetComponentsInChildren<Renderer>(includeInactive: true)
                : root.GetComponents<Renderer>();

            // Filter to only MeshRenderer and SkinnedMeshRenderer
            var targets = renderers
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                .ToArray();

            if (targets.Length == 0)
            {
                Log($"[SKIP] No MeshRenderer/SkinnedMeshRenderer in: {Path.GetFileName(assetPath)}");
                return 0;
            }

            int changed = 0;
            foreach (var r in targets)
            {
                // Use SerializedObject to safely modify — Unity tracks dirty correctly
                var so = new SerializedObject(r);

                if (_disableCastShadow)
                {
                    var castProp = so.FindProperty("m_CastShadows");
                    if (castProp != null && castProp.intValue != (int)_castMode)
                    {
                        castProp.intValue = (int)_castMode;
                        changed++;
                    }
                }

                if (_disableReceiveShadow)
                {
                    var receiveProp = so.FindProperty("m_ReceiveShadows");
                    if (receiveProp != null && receiveProp.boolValue)
                    {
                        receiveProp.boolValue = false;
                        if (!_disableCastShadow) changed++; // count only once per renderer
                    }
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (changed > 0)
            {
                // Save the prefab — this writes the .prefab file safely
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                Log($"[OK] {Path.GetFileName(assetPath)} — {targets.Length} renderer(s)");
            }
            else
            {
                Log($"[SKIP] Already correct: {Path.GetFileName(assetPath)}");
            }

            return changed > 0 ? targets.Length : 0;
        }
        catch (System.Exception e)
        {
            Log($"[ERROR] {Path.GetFileName(assetPath)}: {e.Message}");
            return 0;
        }
        finally
        {
            // MUST always unload to avoid memory leaks
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static List<string> GetSelectedPrefabPaths()
    {
        return Selection.objects
            .Where(o => o != null)
            .Select(o => AssetDatabase.GetAssetPath(o))
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab"))
            .ToList();
    }

    /// <summary>Find all prefab variant assets whose base is one of the given paths.</summary>
    private static List<string> FindVariantsOf(List<string> basePaths)
    {
        var baseGuids = new HashSet<string>(
            basePaths.Select(AssetDatabase.AssetPathToGUID));

        var result = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant) continue;

            var parent = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (parent == null) continue;

            var parentPath = AssetDatabase.GetAssetPath(parent);
            var parentGuid = AssetDatabase.AssetPathToGUID(parentPath);
            if (baseGuids.Contains(parentGuid))
                result.Add(path);
        }
        return result;
    }

    private void Log(string msg)
    {
        _log.Add(msg);
        Debug.Log($"[ShadowTool] {msg}");
    }

    private void InitStyles()
    {
        if (_stylesInit) return;
        _stylesInit = true;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter
        };

        _logStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            richText = true
        };
    }
}
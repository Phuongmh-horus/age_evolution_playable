using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

public class PlayableAdsCheckerWindow : EditorWindow
{
    private Vector2 tabScrollPos;
    private Vector2 contentScrollPos;

    // --- Asset & Scene Issues ---
    private List<GameObject> missingScriptObjs = new List<GameObject>();
    private string[] assetBundleNames = new string[0];

    // --- Obj Missing (Gộp Mesh, Material, Sprite, Spine...) ---
    private Dictionary<GameObject, List<string>> missingDataMap = new Dictionary<GameObject, List<string>>();

    // --- Texture Management ---
    private Dictionary<Texture, List<Object>> sceneTextureUsageMap = new Dictionary<Texture, List<Object>>();
    private Dictionary<Texture, List<Object>> projectTextureUsageMap = new Dictionary<Texture, List<Object>>();
    private Dictionary<Texture, bool> textureFoldouts = new Dictionary<Texture, bool>();
    private string textureSearchQuery = "";
    private Texture textureSearchObj = null;
    private int textureSubTabSelected = 0;

    // --- Audio Management ---
    private Dictionary<AudioClip, List<Object>> audioUsageMap = new Dictionary<AudioClip, List<Object>>();
    private Dictionary<AudioClip, bool> audioFoldouts = new Dictionary<AudioClip, bool>();
    private string audioSearchQuery = "";
    private AudioClip audioSearchObj = null;

    // --- TextMeshPro Management ---
    private List<TextMeshProUsage> sceneTextMeshProUsages = new List<TextMeshProUsage>();
    private List<TextMeshProUsage> prefabTextMeshProUsages = new List<TextMeshProUsage>();
    private Dictionary<string, bool> textMeshProFoldouts = new Dictionary<string, bool>();
    private string textMeshProSearchQuery = "";
    private TMP_FontAsset textMeshProSearchObj = null;
    private int textMeshProSubTabSelected = 0;

    // --- Script Management ---
    private Dictionary<MonoScript, List<Object>> sceneScriptMap = new Dictionary<MonoScript, List<Object>>();
    private Dictionary<MonoScript, List<Object>> prefabScriptMap = new Dictionary<MonoScript, List<Object>>();
    private Dictionary<MonoScript, List<Object>> staticScriptMap = new Dictionary<MonoScript, List<Object>>();
    private Dictionary<MonoScript, bool> scriptFoldouts = new Dictionary<MonoScript, bool>();
    private string scriptSearchQuery = "";
    private MonoScript scriptSearchObj = null;
    private int scriptSubTabSelected = 0;

    // --- Code Issues ---
    public class CodeIssue
    {
        public string ScriptPath;
        public int LineNumber;
        public string ErrorType;
        public string LineContent;
        public Object AssetRef;
    }

    private Dictionary<string, List<CodeIssue>> groupedCodeIssues = new Dictionary<string, List<CodeIssue>>();

    private Object finderTargetObj;
    private List<Object> finderFoundAssets = new List<Object>();
    private List<GameObject> finderFoundInScene = new List<GameObject>();

    private class TextMeshProUsage
    {
        public TMP_Text TextComponent;
        public TMP_FontAsset FontAsset;
        public Material SharedMaterial;
        public GameObject Owner;
        public string SourcePath;
        public string TextPreview;
        public string TypeName;
        public bool IsPrefabAsset;
    }


    private string selectedTabName = "All Scripts"; // Đổi default tab sang All Scripts cho tiện test

    [MenuItem("Tools/Sp Unity/Playable Ads Checker")]
    public static void ShowWindow()
    {
        GetWindow<PlayableAdsCheckerWindow>("Ads Project Checker");
    }

    private void OnGUI()
    {
        GUILayout.Label("QUẢN LÝ TÀI NGUYÊN, TEXTURE, AUDIO & QUÉT CODE CẤM", EditorStyles.boldLabel);
        GUILayout.Space(5);

        if (GUILayout.Button("🔄 QUÉT RUNTIME DEPENDENCIES", GUILayout.Height(35)))
        {
            ScanProject();
        }

        GUILayout.Space(10);

        // ================= 1. TẠO DANH SÁCH TABS NGANG =================
        List<string> tabs = new List<string>();
        Dictionary<string, string> tabDisplayNames = new Dictionary<string, string>();

        tabs.Add("Missing Scripts");
        tabDisplayNames["Missing Scripts"] = $"❌ Missing Scripts ({missingScriptObjs.Count})";

        tabs.Add("GameObj :Prefab");
        tabDisplayNames["GameObj :Prefab"] = $"🔍 GameObj :Prefab";

        tabs.Add("Obj Missing");
        tabDisplayNames["Obj Missing"] = $"⚠️ Obj Missing ({missingDataMap.Count})";

        tabs.Add("AssetBundles");
        tabDisplayNames["AssetBundles"] = $"📦 AssetBundles ({assetBundleNames.Length})";

        HashSet<Texture> totalTextures = new HashSet<Texture>(sceneTextureUsageMap.Keys);
        totalTextures.UnionWith(projectTextureUsageMap.Keys);
        tabs.Add("Textures");
        tabDisplayNames["Textures"] = $"🖼 Textures ({totalTextures.Count})";

        tabs.Add("Audio");
        tabDisplayNames["Audio"] = $"🎵 Audio ({audioUsageMap.Count})";

        int totalTextMeshPro = sceneTextMeshProUsages.Count + prefabTextMeshProUsages.Count;
        tabs.Add("TextMeshPro");
        tabDisplayNames["TextMeshPro"] = $"TextMeshPro ({totalTextMeshPro})";

        HashSet<MonoScript> totalScripts = new HashSet<MonoScript>(sceneScriptMap.Keys);
        totalScripts.UnionWith(prefabScriptMap.Keys);
        totalScripts.UnionWith(staticScriptMap.Keys);
        tabs.Add("All Scripts");
        tabDisplayNames["All Scripts"] = $"📜 All Scripts ({totalScripts.Count})";

        foreach (var key in groupedCodeIssues.Keys)
        {
            tabs.Add(key);
            tabDisplayNames[key] = $"⚠️ {key} ({groupedCodeIssues[key].Count})";
        }

        if (!tabs.Contains(selectedTabName) && tabs.Count > 0)
        {
            selectedTabName = tabs[0];
        }

        // ================= 2. VẼ THANH TABS (CUỘN NGANG) =================
        tabScrollPos = GUILayout.BeginScrollView(tabScrollPos, GUILayout.Height(50));
        GUILayout.BeginHorizontal();

        foreach (string tab in tabs)
        {
            GUI.backgroundColor = (selectedTabName == tab) ? new Color(0.2f, 0.8f, 0.2f) : Color.white;
            if (GUILayout.Button(tabDisplayNames[tab], GUILayout.Width(200), GUILayout.Height(30)))
            {
                selectedTabName = tab;
            }
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();

        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
        GUILayout.Space(5);

        // ================= 3. VẼ NỘI DUNG CỦA TAB ĐANG ĐƯỢC CHỌN =================
        contentScrollPos = GUILayout.BeginScrollView(contentScrollPos);

        switch (selectedTabName)
        {
            case "Missing Scripts": DrawMissingScriptsContent(); break;
            case "GameObj :Prefab": DrawGameObjPrefabContent(); break; // <-- THÊM DÒNG NÀY
            case "Obj Missing": DrawObjMissingContent(); break;
            case "AssetBundles": DrawAssetBundlesContent(); break;
            case "Textures": DrawTexturesContent(); break;
            case "Audio": DrawAudioContent(); break;
            case "TextMeshPro": DrawTextMeshProContent(); break;
            case "All Scripts": DrawAllScriptsContent(); break;
            default:
                if (groupedCodeIssues.ContainsKey(selectedTabName)) DrawCodeGroupContent(selectedTabName);
                break;
        }
        GUILayout.EndScrollView();
    }

    // ================= HÀM QUÉT CHÍNH =================
    private void ScanProject()
    {
        missingScriptObjs.Clear();
        missingDataMap.Clear();
        groupedCodeIssues.Clear();
        sceneScriptMap.Clear();
        prefabScriptMap.Clear();
        staticScriptMap.Clear();
        scriptFoldouts.Clear();

        // 🔥 CHỈ GỌI DEEP SCAN TỪ SCENE. Nó sẽ tự bới Prefab & SO ra.
        DeepScanDependencies();
        ScanAllPrefabMissingScripts();
        ScanTextMeshProUsage();

        assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
        ScanAllScripts();

        Debug.Log("<color=green>Hoàn tất quét! Missing Scripts đã quét cả Scene và toàn bộ Prefab trong Assets.</color>");
    }

    // ================= TAB: GAMEOBJ :PREFAB (SPAWNER FINDER) =================

    private void DrawGameObjPrefabContent()
    {
        GUILayout.Label("Kéo Object / Prefab / ScriptableObject vào đây để truy vết 'Ai đang gọi nó':", EditorStyles.boldLabel);
        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("🎯 Mục tiêu:", GUILayout.Width(75));
        finderTargetObj = EditorGUILayout.ObjectField(finderTargetObj, typeof(Object), true);

        if (GUILayout.Button("⚡ Tìm 'Kẻ Chủ Mưu'", GUILayout.Width(150), GUILayout.Height(22)))
        {
            RunFinderScan();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(15);

        // HIỂN THỊ KẾT QUẢ TỪ PROJECT
        if (finderFoundAssets.Count > 0)
        {
            GUILayout.Label($"📁 TÌM THẤY TRONG PROJECT ({finderFoundAssets.Count}):", EditorStyles.boldLabel);
            foreach (var asset in finderFoundAssets)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.ObjectField(asset, typeof(Object), false);
                if (GUILayout.Button("Mở", GUILayout.Width(60)))
                {
                    AssetDatabase.OpenAsset(asset);
                    EditorGUIUtility.PingObject(asset);
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(10);
        }

        // HIỂN THỊ KẾT QUẢ TỪ SCENE
        if (finderFoundInScene.Count > 0)
        {
            GUILayout.Label($"🌍 TÌM THẤY TRONG SCENE ({finderFoundInScene.Count}):", EditorStyles.boldLabel);
            foreach (var go in finderFoundInScene)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                if (GUILayout.Button("Ping", GUILayout.Width(60)))
                {
                    EditorGUIUtility.PingObject(go);
                    Selection.activeGameObject = go;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // THÔNG BÁO TRẮNG TAY
        if (finderFoundAssets.Count == 0 && finderFoundInScene.Count == 0 && finderTargetObj != null)
        {
            EditorGUILayout.HelpBox("Sạch sẽ! Không có file hay object nào móc vào mục tiêu này.", MessageType.Info);
        }
    }

    private void RunFinderScan()
    {
        finderFoundAssets.Clear();
        finderFoundInScene.Clear();
        if (finderTargetObj == null) return;

        // --- 1. QUÉT PROJECT BẰNG RAW GUID (CỰC NHANH) ---
        string targetPath = AssetDatabase.GetAssetPath(finderTargetObj);
        string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);

        if (!string.IsNullOrEmpty(targetGuid)) // Nếu nó là 1 file Asset thật sự
        {
            string[] allGuids = AssetDatabase.FindAssets("t:Prefab t:ScriptableObject t:Scene");
            int total = allGuids.Length;

            for (int i = 0; i < total; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(allGuids[i]);
                if (path == targetPath) continue;

                // Chỉ update UI mỗi 200 file để tránh bị lag
                if (i % 200 == 0)
                {
                    EditorUtility.DisplayProgressBar("Thợ Săn Prefab", $"Đang quét Text: {path}", (float)i / total);
                }

                try
                {
                    string fileContent = File.ReadAllText(path);
                    if (fileContent.Contains(targetGuid))
                    {
                        Object ownerAsset = AssetDatabase.LoadMainAssetAtPath(path);
                        if (ownerAsset != null && !finderFoundAssets.Contains(ownerAsset))
                        {
                            finderFoundAssets.Add(ownerAsset);
                        }
                    }
                }
                catch { }
            }
            EditorUtility.ClearProgressBar();
        }

        // --- 2. QUÉT TRONG SCENE ---
        GameObject targetGO = null;
        if (finderTargetObj is GameObject go) targetGO = go;
        else if (finderTargetObj is Component comp) targetGO = comp.gameObject;

        //MonoBehaviour[] sceneScripts = SceneObjectFinder.FindIncludingInactive<MonoBehaviour>();
        // foreach (var script in sceneScripts)
        // {
        //     if (script == null) continue;
        //     if (targetGO != null && script.gameObject == targetGO) continue;

        //     SerializedObject so = new SerializedObject(script);
        //     SerializedProperty sp = so.GetIterator();

        //     while (sp.Next(true))
        //     {
        //         if (sp.propertyType == SerializedPropertyType.ObjectReference)
        //         {
        //             Object refObj = sp.objectReferenceValue;
        //             if (refObj == null) continue;

        //             bool isMatch = false;
        //             if (refObj == finderTargetObj) isMatch = true;
        //             else if (targetGO != null && refObj is GameObject refGo && refGo == targetGO) isMatch = true;
        //             else if (targetGO != null && refObj is Component refComp && refComp.gameObject == targetGO) isMatch = true;

        //             if (isMatch && !finderFoundInScene.Contains(script.gameObject))
        //             {
        //                 finderFoundInScene.Add(script.gameObject);
        //             }
        //         }
        //     }
        // }
    }

    private void ScanTextMeshProUsage()
    {
        sceneTextMeshProUsages.Clear();
        prefabTextMeshProUsages.Clear();
        textMeshProFoldouts.Clear();

        // TMP_Text[] sceneTexts = SceneObjectFinder.FindIncludingInactive<TMP_Text>();
        // foreach (var text in sceneTexts)
        // {
        //     if (text == null || text.gameObject == null) continue;
        //     RegisterTextMeshProUsage(text, false, "Scene");
        // }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);

                if (i % 50 == 0)
                {
                    EditorUtility.DisplayProgressBar("Scan TextMeshPro", $"Dang quet prefab: {prefabPath}", (float)i / prefabGuids.Length);
                }

                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabRoot == null) continue;

                TMP_Text[] texts = prefabRoot.GetComponentsInChildren<TMP_Text>(true);
                foreach (var text in texts)
                {
                    RegisterTextMeshProUsage(text, true, prefabPath);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void RegisterTextMeshProUsage(TMP_Text text, bool isPrefabAsset, string sourcePath)
    {
        if (text == null) return;

        List<TextMeshProUsage> targetList = isPrefabAsset ? prefabTextMeshProUsages : sceneTextMeshProUsages;
        for (int i = 0; i < targetList.Count; i++)
        {
            if (targetList[i].TextComponent == text)
            {
                return;
            }
        }

        targetList.Add(new TextMeshProUsage
        {
            TextComponent = text,
            FontAsset = text.font,
            SharedMaterial = text.fontSharedMaterial,
            Owner = text.gameObject,
            SourcePath = sourcePath,
            TextPreview = BuildTextMeshProPreview(text.text),
            TypeName = text.GetType().Name,
            IsPrefabAsset = isPrefabAsset
        });
    }

    private static string BuildTextMeshProPreview(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        string preview = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return preview.Length > 90 ? preview.Substring(0, 90) + "..." : preview;
    }

    private void DrawTextMeshProContent()
    {
        string[] subTabs =
        {
            $"In Scene ({sceneTextMeshProUsages.Count})",
            $"In Prefab Assets ({prefabTextMeshProUsages.Count})"
        };

        textMeshProSubTabSelected = GUILayout.Toolbar(textMeshProSubTabSelected, subTabs, GUILayout.Height(30));
        GUILayout.Space(10);

        List<TextMeshProUsage> currentList = textMeshProSubTabSelected == 0 ? sceneTextMeshProUsages : prefabTextMeshProUsages;

        if (currentList.Count == 0)
        {
            GUILayout.Label(textMeshProSubTabSelected == 0
                ? "Khong tim thay TextMeshPro nao dang nam trong Scene."
                : "Khong tim thay TextMeshPro nao trong Prefab asset.");
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Search:", GUILayout.Width(55));
        textMeshProSearchQuery = EditorGUILayout.TextField(textMeshProSearchQuery);
        GUILayout.Label("Font:", GUILayout.Width(40));
        textMeshProSearchObj = (TMP_FontAsset)EditorGUILayout.ObjectField(textMeshProSearchObj, typeof(TMP_FontAsset), false, GUILayout.Width(180));

        if (GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            textMeshProSearchQuery = "";
            textMeshProSearchObj = null;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        List<TextMeshProUsage> filteredList = currentList.Where(MatchesTextMeshProUsage).ToList();
        if (filteredList.Count == 0)
        {
            GUILayout.Label("Khong tim thay TextMeshPro phu hop.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var groups = filteredList
            .GroupBy(usage => usage.FontAsset)
            .OrderBy(group => GetTextMeshProGroupName(group.Key))
            .ToList();

        foreach (var group in groups)
        {
            TMP_FontAsset fontAsset = group.Key;
            List<TextMeshProUsage> usages = group
                .OrderBy(usage => usage.SourcePath)
                .ThenBy(usage => usage.Owner != null ? usage.Owner.name : "")
                .ToList();

            string foldoutKey = GetTextMeshProFoldoutKey(fontAsset);
            if (!textMeshProFoldouts.ContainsKey(foldoutKey))
            {
                textMeshProFoldouts[foldoutKey] = false;
            }

            EditorGUILayout.BeginVertical("box");
            textMeshProFoldouts[foldoutKey] = EditorGUILayout.Foldout(
                textMeshProFoldouts[foldoutKey],
                $"{GetTextMeshProGroupName(fontAsset)} (Dang dung boi {usages.Count} TMP)",
                true,
                EditorStyles.foldoutHeader);

            if (textMeshProFoldouts[foldoutKey])
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);

                if (fontAsset != null)
                {
                    EditorGUILayout.ObjectField(fontAsset, typeof(TMP_FontAsset), false, GUILayout.Width(180));
                }
                else
                {
                    EditorGUILayout.HelpBox("Missing Font Asset", MessageType.Warning);
                }

                EditorGUILayout.BeginVertical();
                foreach (var usage in usages)
                {
                    DrawTextMeshProUsageRow(usage);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }

            EditorGUILayout.EndVertical();
        }
    }

    private bool MatchesTextMeshProUsage(TextMeshProUsage usage)
    {
        if (usage == null) return false;
        if (textMeshProSearchObj != null && usage.FontAsset != textMeshProSearchObj) return false;
        if (string.IsNullOrEmpty(textMeshProSearchQuery)) return true;

        string query = textMeshProSearchQuery.ToLowerInvariant();
        return ContainsTextMeshProSearch(usage.FontAsset != null ? usage.FontAsset.name : "Missing Font Asset", query) ||
               ContainsTextMeshProSearch(usage.SharedMaterial != null ? usage.SharedMaterial.name : "", query) ||
               ContainsTextMeshProSearch(usage.Owner != null ? usage.Owner.name : "", query) ||
               ContainsTextMeshProSearch(usage.TextPreview, query) ||
               ContainsTextMeshProSearch(usage.TypeName, query) ||
               ContainsTextMeshProSearch(usage.SourcePath, query);
    }

    private static bool ContainsTextMeshProSearch(string value, string query)
    {
        return !string.IsNullOrEmpty(value) && value.ToLowerInvariant().Contains(query);
    }

    private static string GetTextMeshProGroupName(TMP_FontAsset fontAsset)
    {
        return fontAsset != null ? fontAsset.name : "Missing Font Asset";
    }

    private static string GetTextMeshProFoldoutKey(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null) return "Missing Font Asset";

        string assetPath = AssetDatabase.GetAssetPath(fontAsset);
        return !string.IsNullOrEmpty(assetPath) ? assetPath : fontAsset.GetInstanceID().ToString();
    }

    private void DrawTextMeshProUsageRow(TextMeshProUsage usage)
    {
        if (usage == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.ObjectField(usage.Owner, typeof(GameObject), true);
        GUILayout.Label(usage.TypeName, GUILayout.Width(120));

        if (GUILayout.Button("Ping", GUILayout.Width(55)))
        {
            PingTextMeshProUsage(usage);
        }

        if (usage.IsPrefabAsset && GUILayout.Button("Open", GUILayout.Width(55)))
        {
            OpenTextMeshProUsage(usage);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(15);
        EditorGUILayout.ObjectField("Component", usage.TextComponent, typeof(TMP_Text), true);
        EditorGUILayout.EndHorizontal();

        if (usage.SharedMaterial != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            EditorGUILayout.ObjectField("Material", usage.SharedMaterial, typeof(Material), false);
            EditorGUILayout.EndHorizontal();
        }

        if (!string.IsNullOrEmpty(usage.SourcePath) && usage.IsPrefabAsset)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            EditorGUILayout.LabelField("Prefab", usage.SourcePath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        if (!string.IsNullOrEmpty(usage.TextPreview))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            EditorGUILayout.LabelField("Text", usage.TextPreview, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void PingTextMeshProUsage(TextMeshProUsage usage)
    {
        if (usage == null) return;

        Object target = usage.TextComponent != null ? (Object)usage.TextComponent : usage.Owner;
        if (target == null && usage.IsPrefabAsset && !string.IsNullOrEmpty(usage.SourcePath))
        {
            target = AssetDatabase.LoadMainAssetAtPath(usage.SourcePath);
        }

        if (target == null) return;

        Selection.activeObject = target;
        if (!usage.IsPrefabAsset && usage.Owner != null)
        {
            Selection.activeGameObject = usage.Owner;
        }

        EditorGUIUtility.PingObject(target);
    }

    private void OpenTextMeshProUsage(TextMeshProUsage usage)
    {
        if (usage == null || string.IsNullOrEmpty(usage.SourcePath)) return;

        Object prefabAsset = AssetDatabase.LoadMainAssetAtPath(usage.SourcePath);
        if (prefabAsset == null) return;

        AssetDatabase.OpenAsset(prefabAsset);
        EditorGUIUtility.PingObject(prefabAsset);
    }

    private void DeepScanDependencies()
    {
        sceneTextureUsageMap.Clear();
        projectTextureUsageMap.Clear();
        audioUsageMap.Clear();
        textureFoldouts.Clear();
        audioFoldouts.Clear();

        HashSet<Object> globalVisited = new HashSet<Object>();
        Queue<Object> queue = new Queue<Object>();

        // 1. CHỈ LẤY RỄ TỪ SCENE (GameObjects đang nằm trên Hierarchy)
        // GameObject[] sceneObjects = SceneObjectFinder.FindIncludingInactive<GameObject>();
        // foreach (var go in sceneObjects)
        // {
        //     if (!globalVisited.Contains(go))
        //     {
        //         globalVisited.Add(go);
        //         queue.Enqueue(go);
        //     }
        //     CheckGameObject(go);
        // }

        // ĐÃ XÓA vòng lặp quét toàn bộ ScriptableObject thừa thãi ở đây!

        while (queue.Count > 0)
        {
            Object current = queue.Dequeue();

            if (current is GameObject go)
            {
                ExtractDirectTextures(go);
                ExtractDirectAudio(go);
                ExtractDirectScripts(go);

                Component[] comps = go.GetComponents<Component>();
                foreach (var c in comps)
                {
                    if (c != null && !globalVisited.Contains(c))
                    {
                        globalVisited.Add(c);
                        queue.Enqueue(c);
                    }
                }
            }
            else if (current is Component comp || current is ScriptableObject soObj)
            {
                if (current is ScriptableObject soComponent)
                {
                    MonoScript ms = MonoScript.FromScriptableObject(soComponent);
                    if (ms != null) RegisterScript(ms, soComponent);
                }

                SerializedObject so = new SerializedObject(current);
                SerializedProperty sp = so.GetIterator();

                Object owner = (current is Component c) ? c.gameObject : current;

                // X-Quang soi mọi biến: Tự lôi Prefab & SO vào hàng đợi NẾU bị reference
                while (sp.Next(true))
                {
                    if (sp.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        Object refObj = sp.objectReferenceValue;
                        if (refObj == null) continue;

                        if (refObj is Texture tex) RegisterTexture(tex, owner);
                        else if (refObj is Sprite spr && spr.texture != null) RegisterTexture(spr.texture, owner);
                        else if (refObj is AudioClip clip) RegisterAudio(clip, owner);
                        else if (refObj is ScriptableObject scriptable)
                        {
                            if (!globalVisited.Contains(scriptable))
                            {
                                globalVisited.Add(scriptable);
                                queue.Enqueue(scriptable);
                            }
                        }
                        else if (refObj is GameObject || refObj is Component)
                        {
                            GameObject refGo = refObj as GameObject;
                            if (refGo == null) refGo = (refObj as Component).gameObject;

                            if (PrefabUtility.IsPartOfPrefabAsset(refGo))
                            {
                                GameObject root = refGo.transform.root.gameObject;
                                Transform[] children = root.GetComponentsInChildren<Transform>(true);
                                foreach (var child in children)
                                {
                                    if (!globalVisited.Contains(child.gameObject))
                                    {
                                        globalVisited.Add(child.gameObject);
                                        queue.Enqueue(child.gameObject);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // ================= HÀM HỖ TRỢ ĐĂNG KÝ =================
    private void RegisterTexture(Texture tex, Object owner)
    {
        if (tex == null) return;
        bool isInScene = true;
        if (owner is GameObject go) isInScene = !PrefabUtility.IsPartOfPrefabAsset(go);
        else if (owner is Component comp) isInScene = !PrefabUtility.IsPartOfPrefabAsset(comp.gameObject);
        else if (owner is ScriptableObject) isInScene = false;

        if (isInScene)
        {
            if (!sceneTextureUsageMap.ContainsKey(tex)) { sceneTextureUsageMap[tex] = new List<Object>(); textureFoldouts[tex] = false; }
            if (!sceneTextureUsageMap[tex].Contains(owner)) sceneTextureUsageMap[tex].Add(owner);
        }
        else
        {
            if (!projectTextureUsageMap.ContainsKey(tex)) { projectTextureUsageMap[tex] = new List<Object>(); textureFoldouts[tex] = false; }
            if (!projectTextureUsageMap[tex].Contains(owner)) projectTextureUsageMap[tex].Add(owner);
        }
    }

    private void RegisterAudio(AudioClip clip, Object owner)
    {
        if (clip == null) return;
        if (!audioUsageMap.ContainsKey(clip)) { audioUsageMap[clip] = new List<Object>(); audioFoldouts[clip] = false; }
        if (!audioUsageMap[clip].Contains(owner)) audioUsageMap[clip].Add(owner);
    }

    private void RegisterScript(MonoScript script, Object owner)
    {
        if (script == null) return;

        string scriptPath = AssetDatabase.GetAssetPath(script);
        // Lọc ngay từ vòng gửi xe: Chỉ lấy code trong thư mục Assets của bạn
        if (string.IsNullOrEmpty(scriptPath) ||
            !scriptPath.StartsWith("Assets/") ||
            scriptPath.Contains("PlayableAdsCheckerWindow") ||
            scriptPath.Contains("Plugins/") ||
            scriptPath.Contains("Toony") ||
            scriptPath.Contains("DOTween") ||
            scriptPath.Contains("Demigiant"))
        {
            return;
        }

        bool isInScene = true;
        if (owner is GameObject go) isInScene = !PrefabUtility.IsPartOfPrefabAsset(go);
        else if (owner is Component comp) isInScene = !PrefabUtility.IsPartOfPrefabAsset(comp.gameObject);
        else if (owner is ScriptableObject) isInScene = false;

        if (isInScene)
        {
            if (!sceneScriptMap.ContainsKey(script)) { sceneScriptMap[script] = new List<Object>(); scriptFoldouts[script] = false; }
            if (!sceneScriptMap[script].Contains(owner)) sceneScriptMap[script].Add(owner);
        }
        else
        {
            if (!prefabScriptMap.ContainsKey(script)) { prefabScriptMap[script] = new List<Object>(); scriptFoldouts[script] = false; }
            if (!prefabScriptMap[script].Contains(owner)) prefabScriptMap[script].Add(owner);
        }
    }

    private void ExtractDirectTextures(GameObject go)
    {
        Renderer[] renderers = go.GetComponents<Renderer>();
        foreach (var r in renderers)
        {
            if (r is SpriteRenderer sr && sr.sprite != null) RegisterTexture(sr.sprite.texture, go);

            if (r.sharedMaterials != null)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    string[] texPropertyNames = mat.GetTexturePropertyNames();
                    foreach (string propName in texPropertyNames) RegisterTexture(mat.GetTexture(propName), go);
                }
            }
        }
        UnityEngine.UI.Image[] uiImages = go.GetComponents<UnityEngine.UI.Image>();
        foreach (var img in uiImages) if (img.sprite != null) RegisterTexture(img.sprite.texture, go);

        UnityEngine.UI.RawImage[] rawImages = go.GetComponents<UnityEngine.UI.RawImage>();
        foreach (var raw in rawImages) if (raw.texture != null) RegisterTexture(raw.texture, go);
    }

    private void ExtractDirectAudio(GameObject go)
    {
        AudioSource[] sources = go.GetComponents<AudioSource>();
        foreach (var s in sources)
        {
            if (s.clip != null) RegisterAudio(s.clip, go);
        }
    }

    private void ExtractDirectScripts(GameObject go)
    {
        Component[] comps = go.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (c is MonoBehaviour mb)
            {
                MonoScript ms = MonoScript.FromMonoBehaviour(mb);
                if (ms != null) RegisterScript(ms, go);
            }
        }
    }

    private void ScanAllPrefabMissingScripts()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);

                if (i % 50 == 0)
                {
                    EditorUtility.DisplayProgressBar("Quét Missing Scripts trong Assets", $"Đang quét prefab: {prefabPath}", (float)i / prefabGuids.Length);
                }

                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabRoot == null) continue;

                Transform[] children = prefabRoot.GetComponentsInChildren<Transform>(true);
                foreach (var child in children)
                {
                    CheckGameObject(child.gameObject, false);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void CheckGameObject(GameObject go, bool includeMissingData = true)
    {
        void AddMissingReason(string reason)
        {
            if (!missingDataMap.ContainsKey(go)) missingDataMap[go] = new List<string>();
            if (!missingDataMap[go].Contains(reason)) missingDataMap[go].Add(reason);
        }

        var components = go.GetComponents<Component>();
        bool hasMissingScript = false;

        foreach (var comp in components)
        {
            if (comp == null)
            {
                hasMissingScript = true;
                continue;
            }

            if (!includeMissingData) continue;

            string typeName = comp.GetType().Name;
            if (typeName.Contains("SkeletonGraphic") || typeName.Contains("SkeletonAnimation") || typeName.Contains("SkeletonMecanim"))
            {
                SerializedObject so = new SerializedObject(comp);
                SerializedProperty sp = so.FindProperty("skeletonDataAsset");
                if (sp != null && sp.objectReferenceValue == null)
                {
                    AddMissingReason($"{typeName}: None (Skeleton Data Asset)");
                }
            }
        }

        if (hasMissingScript && !missingScriptObjs.Contains(go)) missingScriptObjs.Add(go);
        if (!includeMissingData) return;

        Renderer[] renderers = go.GetComponents<Renderer>();
        foreach (var r in renderers)
        {
            if (r is SpriteRenderer sr)
            {
                if (sr.sprite == null) AddMissingReason("SpriteRenderer: None (Sprite)");
            }
            else if (r is ParticleSystemRenderer psr)
            {
                if (psr.renderMode != ParticleSystemRenderMode.None && psr.sharedMaterial == null)
                    AddMissingReason("ParticleSystemRenderer: Missing Main Material");

                ParticleSystem ps = go.GetComponent<ParticleSystem>();
                if (ps != null && ps.trails.enabled && psr.trailMaterial == null)
                    AddMissingReason("ParticleSystemRenderer: Missing Trail Material (Trails Module is ON)");
            }
            else
            {
                if (r.sharedMaterials == null || r.sharedMaterials.Length == 0)
                {
                    AddMissingReason($"{r.GetType().Name}: Materials list is Empty");
                }
                else
                {
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                    {
                        if (r.sharedMaterials[i] == null)
                        {
                            AddMissingReason($"{r.GetType().Name}: Missing Material ở Element {i}");
                        }
                    }
                }
            }
        }

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh == null) AddMissingReason("MeshFilter: None (Mesh)");

        SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh == null) AddMissingReason("SkinnedMeshRenderer: None (Mesh)");
    }

    private void ScanAllScripts()
    {
        string[] scriptGuids = AssetDatabase.FindAssets("t:Script");
        foreach (var guid in scriptGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // 🔥 BỘ LỌC CỰC GẮT: Chỉ lấy Code của bạn, loại trừ Unity/Plugins/Editor/SDK
            if (path.StartsWith("Packages/") || path.Contains("/Editor/") ||
                path.Contains("PlayableAdsCheckerWindow") || path.Contains("Plugins/") ||
                path.Contains("Toony") || path.Contains("DOTween") || path.Contains("Demigiant") ||
                path.Contains("Luna") || path.Contains("TextMesh Pro") ||
                IsInFolderNamed(path, "Spine"))
                continue;

            MonoScript scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (scriptAsset == null) continue;

            bool isUsedInScene = sceneScriptMap.ContainsKey(scriptAsset);
            bool isUsedInPrefabSO = prefabScriptMap.ContainsKey(scriptAsset);
            bool isPureStaticOrUtility = false;

            // Xử lý nhóm Code Thuần (Interface, Generic, Enum, Struct, Data Class)
            if (!isUsedInScene && !isUsedInPrefabSO)
            {
                System.Type scriptType = scriptAsset.GetClass();

                // LỖI ĐÃ ĐƯỢC SỬA Ở ĐÂY:
                // Unity GetClass() trả về null cho Interface Generic. Nên ta check ngược lại:
                // Nó chỉ là code rác (bị bỏ qua) NẾU nó là MonoBehaviour/SO mà không ai gọi tới.
                bool isMonoBehaviour = scriptType != null && scriptType.IsSubclassOf(typeof(MonoBehaviour));
                bool isScriptableObject = scriptType != null && scriptType.IsSubclassOf(typeof(ScriptableObject));

                // Nếu KHÔNG PHẢI Mono và KHÔNG PHẢI SO -> Chắc chắn là Code thuần/Interface của bạn! Bắt luôn!
                if (!isMonoBehaviour && !isScriptableObject)
                {
                    isPureStaticOrUtility = true;
                    if (!staticScriptMap.ContainsKey(scriptAsset))
                    {
                        staticScriptMap[scriptAsset] = new List<Object>();
                        scriptFoldouts[scriptAsset] = false;
                    }
                    if (!staticScriptMap[scriptAsset].Contains(scriptAsset)) staticScriptMap[scriptAsset].Add(scriptAsset);
                }
            }

            // CHỈ QUÉT LỖI CODE cho những Script thực sự ĐANG ĐƯỢC DÙNG (In Scene, In Prefab/SO Reference, hoặc Pure/Interface)
            if (isUsedInScene || isUsedInPrefabSO || isPureStaticOrUtility)
            {
                string[] lines = File.ReadAllLines(path);
                int braceDepth = 0;
                int interfaceDepth = -1;
                bool pendingInterface = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    bool isInsideInterface = interfaceDepth >= 0 && braceDepth > interfaceDepth;
                    CheckLineForIssues(path, i + 1, line, scriptAsset, isInsideInterface);
                    UpdateInterfaceScope(line, ref braceDepth, ref interfaceDepth, ref pendingInterface);
                }
            }
            else
            {
                CheckCodeNewSyntaxOnly(path, scriptAsset);
            }
        }
    }

    // ================= TÁCH CÁC RULE CHECK CODE Ở ĐÂY CHO GỌN =================
    // ================= TÁCH CÁC RULE CHECK CODE Ở ĐÂY CHO GỌN =================
    private static bool IsInFolderNamed(string path, string folderName)
    {
        string normalizedPath = path.Replace('\\', '/');
        string pattern = $@"(^|/){Regex.Escape(folderName)}(/|$)";
        return Regex.IsMatch(normalizedPath, pattern, RegexOptions.IgnoreCase);
    }

    private static bool HasIndexFromEndSyntax(string line)
    {
        return Regex.IsMatch(line, @"\[\s*\^\s*\d+\s*\]");
    }

    private static bool HasCollectionQueryUsage(string line)
    {
        return Regex.IsMatch(line, @"\.FindAll\s*\(");
    }

    private static bool HasPublicInterfaceMember(string line)
    {
        return line.StartsWith("public ");
    }

    private static void UpdateInterfaceScope(string line, ref int braceDepth, ref int interfaceDepth, ref bool pendingInterface)
    {
        string codeLine = line.Split(new[] { "//" }, System.StringSplitOptions.None)[0];
        if (interfaceDepth < 0 && Regex.IsMatch(codeLine, @"\binterface\s+\w+"))
            pendingInterface = true;

        for (int i = 0; i < codeLine.Length; i++)
        {
            char c = codeLine[i];
            if (c == '{')
            {
                if (pendingInterface && interfaceDepth < 0)
                {
                    interfaceDepth = braceDepth;
                    pendingInterface = false;
                }

                braceDepth++;
            }
            else if (c == '}')
            {
                braceDepth = Mathf.Max(0, braceDepth - 1);
                if (interfaceDepth >= 0 && braceDepth <= interfaceDepth)
                    interfaceDepth = -1;
            }
        }
    }

    private void AddCodeIssue(string errorType, string path, int lineNum, string line, Object scriptAsset)
    {
        if (!groupedCodeIssues.ContainsKey(errorType))
            groupedCodeIssues[errorType] = new List<CodeIssue>();

        groupedCodeIssues[errorType].Add(new CodeIssue
        {
            ScriptPath = path,
            LineNumber = lineNum,
            ErrorType = errorType,
            LineContent = line.Trim(),
            AssetRef = scriptAsset
        });
    }

    private void CheckCodeNewSyntaxOnly(string path, Object scriptAsset)
    {
        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;
            if (HasIndexFromEndSyntax(trimmed) || HasCollectionQueryUsage(trimmed))
                AddCodeIssue("Code: New", path, i + 1, line, scriptAsset);
        }
    }

    private void CheckLineForIssues(string path, int lineNum, string line, Object scriptAsset, bool isInsideInterface = false)
    {
        void AddIssue(string errorType)
        {
            AddCodeIssue(errorType, path, lineNum, line, scriptAsset);
        }

        string trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) return;

        // --- RULE 1: CÁC LỖI NGHIÊM TRỌNG TÁCH RIÊNG TAB ---
        var separateIssues = new Dictionary<string, string> {
            { "Code: PlayerPrefs", "PlayerPrefs." },
            { "Code: Resources.Load", "Resources.Load" }
        };

        foreach (var rule in separateIssues)
        {
            if (trimmed.Contains(rule.Value)) AddIssue(rule.Key);
        }

        // --- RULE 2: GOM TẤT CẢ TỘI ĐỒ VÀO TAB "Code: New" ---
        bool isCodeNewIssue = false;
        if (isInsideInterface && HasPublicInterfaceMember(trimmed))
        {
            AddIssue("Interface");
            return;
        }

        // 1. Nhóm từ khóa Package & Hàm bị cấm (Đã gom theo yêu cầu)
        string[] forbiddenKeywords = {
            "RuntimeInitializeOnLoadMethod", // RuntimeInit
            "void OnMouseDown",              // OnMouse Events
            "OdinInspector",                 // Package: Odin
            "using Firebase",                // Package: Firebase
            "using com.adjust",              // Package: Adjust
            "UniTask",                       // Code: UniTask
            "JsonUtility.",                  // Code: Json
            "goto "                          // Code: Goto
        };

        foreach (var kw in forbiddenKeywords)
        {
            if (trimmed.Contains(kw)) isCodeNewIssue = true;
        }

        // 2. Quét Cú pháp C# mới (Target-typed new, Indexer, Pattern Matching, Try-catch)
        if (Regex.IsMatch(trimmed, @"=\s*new\s*\(\s*\)\s*;") ||
            HasIndexFromEndSyntax(trimmed) ||
            HasCollectionQueryUsage(trimmed) ||
            (trimmed.Contains("^") && !trimmed.Contains("=>")) ||
            Regex.IsMatch(trimmed, @"\bis\b.*\b(or|and)\b") ||
            Regex.IsMatch(trimmed, @"\bis\s+not\b"))
        {
            isCodeNewIssue = true;
        }

        // 3. Quét Runtime Find / Lambda Find (Lỗi DataProvider / Performance)
        if (trimmed.Contains("GameObject.Find") ||
            trimmed.Contains("FindObjectOfType") ||
            (trimmed.Contains(".Find(") && trimmed.Contains("=>")))
        {
            isCodeNewIssue = true;
        }

        // 4. Ép chuẩn Interface IShotReceiver (CHỈ BẮT LÚC KHAI BÁO)
        if (trimmed.EndsWith(";"))
        {
            if (trimmed.StartsWith("public ") &&
               (trimmed.Contains("CanReceiveShot") || trimmed.Contains("ShotVisual") || trimmed.Contains("ResolveShot") || trimmed.Contains("ShotResult")))
            {
                isCodeNewIssue = true;
            }
        }

        // 5. Các hàm không hỗ trợ khác gom chung
        if (trimmed.Contains("Unity.VisualScripting") || trimmed.Contains("Array.Empty<") || trimmed.Contains("AsyncWaitForCompletion"))
        {
            isCodeNewIssue = true;
        }

        // Chốt đơn: Nếu dính 1 trong 5 tội trên -> Cho vào khám "Code: New"
        if (isCodeNewIssue)
        {
            AddIssue("Code: New");
        }
    }
    // ================= VẼ NỘI DUNG TỪNG TAB =================

    private void DrawAllScriptsContent()
    {
        string[] subTabs = { $"In Scene ({sceneScriptMap.Count})", $"In Prefab/SO ({prefabScriptMap.Count})", $"Static / Unattached ({staticScriptMap.Count})" };
        scriptSubTabSelected = GUILayout.Toolbar(scriptSubTabSelected, subTabs, GUILayout.Height(30));
        GUILayout.Space(10);

        var currentMap = scriptSubTabSelected == 0 ? sceneScriptMap : (scriptSubTabSelected == 1 ? prefabScriptMap : staticScriptMap);

        if (currentMap.Count == 0)
        {
            GUILayout.Label("✔️ Rất Sạch Sẽ! Không có script nào thuộc nhóm này.");
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("🔍 Nhập Tên:", GUILayout.Width(70));
        scriptSearchQuery = EditorGUILayout.TextField(scriptSearchQuery);
        GUILayout.Label("Kéo thả File:", GUILayout.Width(80));
        scriptSearchObj = (MonoScript)EditorGUILayout.ObjectField(scriptSearchObj, typeof(MonoScript), false, GUILayout.Width(180));

        if (GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            scriptSearchQuery = "";
            scriptSearchObj = null;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        int displayedCount = 0;

        foreach (var kvp in currentMap)
        {
            MonoScript script = kvp.Key;
            List<Object> objs = kvp.Value;

            if (scriptSearchObj != null && script != scriptSearchObj) continue;
            if (!string.IsNullOrEmpty(scriptSearchQuery) && (script == null || !script.name.ToLower().Contains(scriptSearchQuery.ToLower()))) continue;

            displayedCount++;
            EditorGUILayout.BeginVertical("box");

            string prefix = scriptSubTabSelected == 2 ? "⚡ Static" : $"📜 Đang dùng bởi {objs.Count} tham chiếu";
            scriptFoldouts[script] = EditorGUILayout.Foldout(scriptFoldouts[script], $"{script.name} ({prefix})", true, EditorStyles.foldoutHeader);

            if (scriptFoldouts[script])
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                EditorGUILayout.ObjectField(script, typeof(MonoScript), false, GUILayout.Width(150));

                if (scriptSubTabSelected != 2) // Ko vẽ object refer cho đồ static vì nó ko dính vào ai
                {
                    EditorGUILayout.BeginVertical();
                    foreach (var obj in objs)
                    {
                        EditorGUILayout.ObjectField(obj, typeof(Object), true);
                    }
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
            EditorGUILayout.EndVertical();
        }

        if (displayedCount == 0)
        {
            GUILayout.Label($"Không tìm thấy Script phù hợp.", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawAudioContent()
    {
        if (audioUsageMap.Count == 0)
        {
            GUILayout.Label("✔️ Không tìm thấy Âm thanh (AudioClip) nào đang được sử dụng.");
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("🔍 Nhập Tên:", GUILayout.Width(70));
        audioSearchQuery = EditorGUILayout.TextField(audioSearchQuery);
        GUILayout.Label("Kéo thả File:", GUILayout.Width(80));
        audioSearchObj = (AudioClip)EditorGUILayout.ObjectField(audioSearchObj, typeof(AudioClip), false, GUILayout.Width(180));

        if (GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            audioSearchQuery = "";
            audioSearchObj = null;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        GUILayout.Label("Danh sách AudioClip (Dò từ Scene -> Code -> ScriptableObject -> Prefabs):", EditorStyles.helpBox);
        GUILayout.Space(5);

        int displayedCount = 0;

        foreach (var kvp in audioUsageMap)
        {
            AudioClip clip = kvp.Key;
            List<Object> objs = kvp.Value;

            if (audioSearchObj != null && clip != audioSearchObj) continue;
            if (!string.IsNullOrEmpty(audioSearchQuery) && (clip == null || !clip.name.ToLower().Contains(audioSearchQuery.ToLower()))) continue;

            displayedCount++;

            EditorGUILayout.BeginVertical("box");
            audioFoldouts[clip] = EditorGUILayout.Foldout(audioFoldouts[clip], $"🎵 {clip.name} (Đang dùng bởi {objs.Count} tham chiếu)", true, EditorStyles.foldoutHeader);

            if (audioFoldouts[clip])
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(150));

                EditorGUILayout.BeginVertical();
                foreach (var obj in objs) EditorGUILayout.ObjectField(obj, typeof(Object), true);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
            EditorGUILayout.EndVertical();
        }

        if (displayedCount == 0)
        {
            GUILayout.Label($"Không tìm thấy Âm thanh phù hợp.", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawTexturesContent()
    {
        string[] subTabs = { $"In Scene ({sceneTextureUsageMap.Count})", $"In Project (Prefabs/SO) ({projectTextureUsageMap.Count})" };
        textureSubTabSelected = GUILayout.Toolbar(textureSubTabSelected, subTabs, GUILayout.Height(30));
        GUILayout.Space(10);

        var currentMap = textureSubTabSelected == 0 ? sceneTextureUsageMap : projectTextureUsageMap;

        if (currentMap.Count == 0)
        {
            GUILayout.Label(textureSubTabSelected == 0 ? "✔️ Không có Texture nào gắn trực tiếp trên Scene." : "✔️ Không có Texture nào dùng trong Prefabs/SO liên quan.");
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("🔍 Nhập Tên:", GUILayout.Width(70));
        textureSearchQuery = EditorGUILayout.TextField(textureSearchQuery);
        GUILayout.Label("Kéo thả File:", GUILayout.Width(80));
        textureSearchObj = (Texture)EditorGUILayout.ObjectField(textureSearchObj, typeof(Texture), false, GUILayout.Width(180));

        if (GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            textureSearchQuery = "";
            textureSearchObj = null;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        int displayedCount = 0;

        foreach (var kvp in currentMap)
        {
            Texture tex = kvp.Key;
            List<Object> objs = kvp.Value;

            if (textureSearchObj != null && tex != textureSearchObj) continue;
            if (!string.IsNullOrEmpty(textureSearchQuery) && (tex == null || !tex.name.ToLower().Contains(textureSearchQuery.ToLower()))) continue;

            displayedCount++;

            EditorGUILayout.BeginVertical("box");

            textureFoldouts[tex] = EditorGUILayout.Foldout(textureFoldouts[tex], $"🖼 {tex.name} (Đang dùng bởi {objs.Count} tham chiếu)", true, EditorStyles.foldoutHeader);

            if (textureFoldouts[tex])
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                EditorGUILayout.ObjectField(tex, typeof(Texture), false, GUILayout.Width(60), GUILayout.Height(60));

                EditorGUILayout.BeginVertical();
                foreach (var obj in objs)
                {
                    EditorGUILayout.ObjectField(obj, typeof(Object), true);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
            EditorGUILayout.EndVertical();
        }

        if (displayedCount == 0)
        {
            GUILayout.Label($"Không tìm thấy Texture phù hợp.", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawObjMissingContent()
    {
        if (missingDataMap.Count == 0)
        {
            GUILayout.Label("✔️ Sạch sẽ! Không có Object nào bị thiếu Material, Mesh, Sprite hay Spine Data.");
            return;
        }

        GUILayout.Label("Danh sách các Object bị thiếu dữ liệu cấu thành:", EditorStyles.helpBox);
        GUILayout.Space(5);

        foreach (var kvp in missingDataMap)
        {
            GameObject obj = kvp.Key;
            List<string> reasons = kvp.Value;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.ObjectField(obj, typeof(GameObject), true);

            GUI.color = new Color(1f, 0.6f, 0.2f);
            foreach (string reason in reasons)
            {
                GUILayout.Label($"   • {reason}", EditorStyles.wordWrappedMiniLabel);
            }
            GUI.color = Color.white;

            EditorGUILayout.EndVertical();
        }
    }

    private void DrawMissingScriptsContent()
    {
        if (missingScriptObjs.Count == 0) { GUILayout.Label("✔️ Sạch sẽ! Không có Missing Scripts."); return; }

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("🗑 XÓA TẤT CẢ MISSING SCRIPTS (SCENE & PREFABS)", GUILayout.Height(30))) RemoveAllMissingScripts();
        GUI.backgroundColor = Color.white;
        GUILayout.Space(10);

        foreach (var go in missingScriptObjs)
        {
            if (go == null) continue;

            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.ObjectField(go, typeof(GameObject), true);

            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                string assetPath = AssetDatabase.GetAssetPath(go.transform.root.gameObject);
                GUILayout.Label("Prefab Asset", GUILayout.Width(85));
                GUILayout.Label(assetPath, EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("Scene", GUILayout.Width(85));
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawAssetBundlesContent()
    {
        if (assetBundleNames.Length == 0) { GUILayout.Label("✔️ Sạch sẽ! Không có AssetBundles."); return; }

        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button("🗑 XÓA TẤT CẢ ASSET BUNDLES", GUILayout.Height(30))) RemoveAllAssetBundles();
        GUI.backgroundColor = Color.white;
        GUILayout.Space(10);

        foreach (var ab in assetBundleNames) EditorGUILayout.LabelField(" - " + ab);
    }

    private void DrawCodeGroupContent(string errorType)
    {
        var issues = groupedCodeIssues[errorType];

        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        /*     if (GUILayout.Button($"🛠 TỰ ĐỘNG SỬA (Thêm // để tắt) CHO TẤT CẢ [{errorType}]", GUILayout.Height(30)))
             {
                 AutoFixCodeGroup(errorType);
             }*/
        GUI.backgroundColor = Color.white;
        GUILayout.Space(10);

        foreach (var issue in issues)
        {
            EditorGUILayout.BeginHorizontal("box");
            if (GUILayout.Button($"Line {issue.LineNumber}: {issue.LineContent}", EditorStyles.linkLabel))
            {
                AssetDatabase.OpenAsset(issue.AssetRef, issue.LineNumber);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // ================= CÁC HÀM XỬ LÝ (XÓA / FIX) =================

    private void RemoveAllMissingScripts()
    {
        int total = 0;
        HashSet<string> prefabsToFix = new HashSet<string>();

        foreach (var go in missingScriptObjs)
        {
            if (go == null) continue;

            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                string assetPath = AssetDatabase.GetAssetPath(go.transform.root.gameObject);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    prefabsToFix.Add(assetPath);
                }
            }
            else
            {
                int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                if (removed > 0)
                {
                    total += removed;
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }
            }
        }

        foreach (string path in prefabsToFix)
        {
            using (var editingScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                var prefabRoot = editingScope.prefabContentsRoot;
                int removed = RemoveMissingScriptsRecursive(prefabRoot);
                if (removed > 0)
                {
                    total += removed;
                    EditorUtility.SetDirty(prefabRoot);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"<color=orange>Đã xóa thành công {total} missing scripts trên Scene và trong Prefab!</color>");
        ScanProject();
    }

    private int RemoveMissingScriptsRecursive(GameObject go)
    {
        int count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        foreach (Transform child in go.transform)
        {
            count += RemoveMissingScriptsRecursive(child.gameObject);
        }
        return count;
    }

    private void RemoveAllAssetBundles()
    {
        string[] bundles = AssetDatabase.GetAllAssetBundleNames();

        if (bundles.Length == 0)
        {
            EditorUtility.DisplayDialog("Thông báo", "Project sạch sẽ! Không tìm thấy file nào dính AssetBundle tag.", "OK");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Phát hiện AssetBundle!",
            $"Tìm thấy {bundles.Length} nhóm bundle đang tồn tại.\n\nĐiều này làm Luna bị lỗi build.\nBạn có muốn gỡ tag tất cả không?",
            "Gỡ sạch cho tao",
            "Để xem lại"
        );

        if (!confirm) return;

        int count = 0;
        foreach (var bundleName in bundles)
        {
            string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);

            foreach (var assetPath in assetPaths)
            {
                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer != null)
                {
                    importer.assetBundleName = "";
                    importer.assetBundleVariant = "";
                    count++;
                    Debug.Log($"🧹 Đã gỡ bundle tag khỏi file: {assetPath}");
                }
            }
            AssetDatabase.RemoveAssetBundleName(bundleName, true);
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Hoàn tất", $"Đã gỡ tag khỏi {count} files. Giờ project đã sạch!", "Ngon");
        ScanProject();
    }

    private void AutoFixCodeGroup(string errorType)
    {
        var issues = groupedCodeIssues[errorType];
        var issuesByFile = issues.GroupBy(i => i.ScriptPath);

        foreach (var fileGroup in issuesByFile)
        {
            string path = fileGroup.Key;
            string[] lines = File.ReadAllLines(path);
            bool isModified = false;

            foreach (var issue in fileGroup)
            {
                int idx = issue.LineNumber - 1;
                if (!lines[idx].TrimStart().StartsWith("//"))
                {
                    lines[idx] = "// [Auto-Fix PlayableAds] " + lines[idx];
                    isModified = true;
                }
            }
            if (isModified) File.WriteAllLines(path, lines);
        }
        AssetDatabase.Refresh();
        ScanProject();
    }
}

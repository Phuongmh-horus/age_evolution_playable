#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using GamePlay.Entities;
using UnityEngine;
using UnityEditor;
using GamePlay.Items;

[CustomEditor(typeof(ContentDataSO))]
[CanEditMultipleObjects]
public class ContentDataSOEditor : Editor
{
    private const int ItemsPerPage = 20;

    private ContentDataSO _target;
    private Vector2 _scrollPos;
    private string _searchFilter = "";
    private bool _showHelp = true;
    private EntityType _filterType = EntityType.All;

    private int _currentPage = 0;
    private readonly HashSet<int> _expandedItems = new HashSet<int>();
    private readonly Dictionary<EntityType, int> _typeCounts = new Dictionary<EntityType, int>();

    private int _totalItemCount;
    private int _overrideItemCount;

    private void OnEnable()
    {
        _target = (ContentDataSO)target;
        RebuildStatistics();
        Undo.undoRedoPerformed += HandleUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
    }

    private void HandleUndoRedo()
    {
        _expandedItems.Clear();
        serializedObject.Update();
        RebuildStatistics();
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // If multiple objects are selected, only allow editing Metadata
        if (targets.Length > 1)
        {
            DrawHeaderSection();
            DrawMetadata();

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Multi-object editing is only supported for Metadata.\nPlease select a single ContentDataSO to edit its Spawnable Objects list.", MessageType.Warning);

            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (var t in targets)
                {
                    EditorUtility.SetDirty(t);
                }
            }
            return;
        }

        // --- Single Object GUI ---
        DrawHeaderSection();
        DrawMetadata();
        DrawToolbar();
        DrawStatistics();
        GUILayout.Space(10);
        DrawObjectsList();
        GUILayout.Space(10);
        DrawFooter();

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(_target);
            RebuildStatistics();
        }
    }

    private void ApplyChanges()
    {
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildStatistics();
    }

    private void DrawHeaderSection()
    {
        Rect headerRect = EditorGUILayout.GetControlRect(false, 60);
        EditorGUI.DrawRect(headerRect, new Color(0.2f, 0.3f, 0.4f));

        GUILayout.Space(-60);

        EditorGUILayout.BeginVertical();
        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(10);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            normal = { textColor = Color.white }
        };

        EditorGUILayout.LabelField("Content Data Editor", titleStyle);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(10);
        GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label);
        subtitleStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        EditorGUILayout.LabelField($"Map Content Configuration", subtitleStyle);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);
        EditorGUILayout.EndVertical();
    }

    private void DrawMetadata()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField("Metadata", EditorStyles.boldLabel);

        SerializedProperty contentIdProp = serializedObject.FindProperty("ContentId");
        SerializedProperty contentNameProp = serializedObject.FindProperty("ContentName");
        SerializedProperty descriptionProp = serializedObject.FindProperty("Description");

        if (contentIdProp != null)
        {
            EditorGUILayout.PropertyField(contentIdProp, new GUIContent("Content ID"));
        }
        if (contentNameProp != null)
        {
            EditorGUILayout.PropertyField(contentNameProp, new GUIContent("Content Name"));
        }
        if (descriptionProp != null)
        {
            EditorGUILayout.LabelField("Description:");
            descriptionProp.stringValue = EditorGUILayout.TextArea(descriptionProp.stringValue, GUILayout.Height(40));
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawToolbar()
    {
        string previousSearch = _searchFilter;
        EntityType previousFilter = _filterType;

        EditorGUILayout.BeginHorizontal();
        {
            // Search
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            _searchFilter = EditorGUILayout.TextField(_searchFilter);

            GUILayout.Space(10);

            // Filter by type
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(45));

            var enumValues = System.Enum.GetValues(typeof(EntityType))
                .Cast<EntityType>()
                .Where(e => e != EntityType.All)
                .OrderBy(e => (int)e)
                .ToArray();

            string[] filterOptions = new[] { "All" }
                .Concat(enumValues.Select(e => e.ToString()))
                .ToArray();

            int filterIndex = _filterType == EntityType.All ? 0 : System.Array.IndexOf(enumValues, _filterType) + 1;
            int newFilterIndex = EditorGUILayout.Popup(filterIndex, filterOptions, GUILayout.Width(100));
            _filterType = newFilterIndex == 0 ? EntityType.All : enumValues[newFilterIndex - 1];

            GUILayout.FlexibleSpace();

            // Sort button
            GUI.backgroundColor = new Color(0.7f, 0.9f, 1f);
            if (GUILayout.Button("Sort by Position", GUILayout.Height(25), GUILayout.Width(130)))
            {
                if (EditorUtility.DisplayDialog("Sort Items",
                    $"Sort {_totalItemCount} items by PositionOnMap?\n\n",
                    "Sort", "Cancel"))
                {
                    Undo.RecordObject(_target, "Sort by Position");
                    _target.SortByPosition();
                    serializedObject.Update();
                    GUIUtility.ExitGUI();
                }
            }
            GUI.backgroundColor = Color.white;
        }
        EditorGUILayout.EndHorizontal();

        if (!string.Equals(previousSearch, _searchFilter) || previousFilter != _filterType)
        {
            _currentPage = 0;
        }
    }

    private void DrawStatistics()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        {
            EditorGUILayout.LabelField($"Total: {_totalItemCount}", GUILayout.Width(80));

            if (_overrideItemCount > 0)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
                GUILayout.Label($"Overrides: {_overrideItemCount}", EditorStyles.miniButton, GUILayout.Width(100));
                GUI.backgroundColor = Color.white;
            }

            foreach (EntityType type in System.Enum.GetValues(typeof(EntityType)))
            {
                int count = _typeCounts.TryGetValue(type, out int cachedCount) ? cachedCount : 0;
                if (count > 0)
                {
                    Color typeColor = GetTypeColor(type);
                    GUI.backgroundColor = typeColor;
                    GUILayout.Label($"{type}: {count}", EditorStyles.miniButton, GUILayout.Width(100));
                    GUI.backgroundColor = Color.white;
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void RebuildStatistics()
    {
        _totalItemCount = 0;
        _overrideItemCount = 0;
        _typeCounts.Clear();

        if (_target?.SpawnableObjects == null) return;

        foreach (var item in _target.SpawnableObjects)
        {
            if (item == null) continue;

            _totalItemCount++;
            if (item.propertyOverrides != null && item.propertyOverrides.Count > 0)
                _overrideItemCount++;

            if (item.Prefab == null) continue;

            EntityType type = item.Prefab.EntityType;
            _typeCounts[type] = _typeCounts.TryGetValue(type, out int count) ? count + 1 : 1;
        }
    }

    private void DrawObjectsList()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Spawnable Objects", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button(new GUIContent("+ Add Object", "Thêm một object mới vào danh sách"),
                GUILayout.Height(22), GUILayout.Width(130)))
            {
                AddNewObject();
            }
            GUI.backgroundColor = Color.white;
        }

        if (_target.SpawnableObjects == null || _target.SpawnableObjects.Count == 0)
        {
            EditorGUILayout.HelpBox("No objects. Click '+ Add Object' to create one.", MessageType.Info);
            return;
        }

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MinHeight(200), GUILayout.MaxHeight(500));
        {
            List<int> itemsToDelete = new List<int>();

            int filteredCount = GetFilteredItemCount(_target.SpawnableObjects);
            DrawPageControls(filteredCount);

            int firstFilteredIndex = _currentPage * ItemsPerPage;
            int lastFilteredIndex = Mathf.Min(firstFilteredIndex + ItemsPerPage, filteredCount);
            int filteredIndex = 0;

            for (int i = 0; i < _target.SpawnableObjects.Count; i++)
            {
                var obj = _target.SpawnableObjects[i];
                if (obj == null) continue;

                if (!PassesFilters(obj)) continue;

                if (filteredIndex >= firstFilteredIndex && filteredIndex < lastFilteredIndex)
                {
                    if (DrawSpawnableObjectEntry(i, obj))
                        itemsToDelete.Add(i);
                }

                filteredIndex++;
                if (filteredIndex >= lastFilteredIndex)
                    break;
            }

            if (filteredCount == 0)
                EditorGUILayout.HelpBox("No objects match the current search/filter.", MessageType.Info);

            // Process deletions
            foreach (var delIdx in itemsToDelete.OrderByDescending(d => d))
            {
                DeleteObject(delIdx);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private int GetFilteredItemCount(List<SpawnableObject> items)
    {
        if (items == null) return 0;
        return items.Count(item => item != null && PassesFilters(item));
    }

    private void DrawPageControls(int filteredCount)
    {
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(filteredCount / (float)ItemsPerPage));
        _currentPage = Mathf.Clamp(_currentPage, 0, pageCount - 1);

        if (filteredCount <= ItemsPerPage) return;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUI.enabled = _currentPage > 0;
            if (GUILayout.Button("Previous", EditorStyles.toolbarButton, GUILayout.Width(70)))
                _currentPage--;

            GUI.enabled = _currentPage < pageCount - 1;
            if (GUILayout.Button("Next", EditorStyles.toolbarButton, GUILayout.Width(50)))
                _currentPage++;

            GUI.enabled = true;
            GUILayout.FlexibleSpace();

            int firstItem = _currentPage * ItemsPerPage + 1;
            int lastItem = Mathf.Min((_currentPage + 1) * ItemsPerPage, filteredCount);
            GUILayout.Label($"Items {firstItem}-{lastItem} / {filteredCount}  |  Page {_currentPage + 1}/{pageCount}",
                EditorStyles.miniLabel);
        }
    }

    private bool PassesFilters(SpawnableObject obj)
    {
        if (obj.Prefab == null)
        {
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                if (!("null".IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                      "none".IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0))
                    return false;
            }
            if (_filterType != EntityType.All)
                return false;
            return true;
        }

        if (_filterType != EntityType.All && obj.Prefab.EntityType != _filterType)
            return false;

        if (!string.IsNullOrEmpty(_searchFilter))
        {
            string prefabName = obj.Prefab.name;
            string typeName = obj.Prefab.EntityType.ToString();

            if (prefabName.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                typeName.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    private bool DrawSpawnableObjectEntry(int itemIdx, SpawnableObject obj)
    {
        bool shouldDelete = false;
        bool itemExpanded = _expandedItems.Contains(itemIdx);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            // Header
            EditorGUILayout.BeginHorizontal();
            {
                string typeLabel = obj.Prefab != null ? obj.Prefab.EntityType.ToString() : "None";
                Color typeColor = obj.Prefab != null ? GetTypeColor(obj.Prefab.EntityType) : Color.gray;

                GUI.backgroundColor = typeColor;
                bool newItemExpanded = EditorGUILayout.Foldout(
                    itemExpanded,
                    $"#{itemIdx} [{typeLabel}] - Pos: {obj.PositionOnMap:F1}",
                    true);
                GUI.backgroundColor = Color.white;

                if (newItemExpanded != itemExpanded)
                {
                    itemExpanded = newItemExpanded;
                    if (itemExpanded)
                        _expandedItems.Add(itemIdx);
                    else
                        _expandedItems.Remove(itemIdx);
                }

                if (obj.propertyOverrides != null && obj.propertyOverrides.Count > 0)
                {
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
                    GUILayout.Label($"Override ({obj.propertyOverrides.Count})", EditorStyles.miniButtonMid, GUILayout.Width(100));
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.FlexibleSpace();

                // Move up/down buttons
                int itemCount = _target.SpawnableObjects?.Count ?? 0;
                GUI.enabled = itemIdx > 0;
                if (GUILayout.Button("↑", GUILayout.Width(25)))
                {
                    MoveObject(itemIdx, itemIdx - 1);
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = itemIdx < itemCount - 1;
                if (GUILayout.Button("↓", GUILayout.Width(25)))
                {
                    MoveObject(itemIdx, itemIdx + 1);
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = true;

                if (GUILayout.Button("Duplicate", GUILayout.Width(70)))
                {
                    DuplicateObject(itemIdx);
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("Delete", GUILayout.Width(25)))
                {
                    shouldDelete = true;
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            if (itemExpanded)
            {
                SerializedProperty spawnablesProp = serializedObject.FindProperty("SpawnableObjects");
                if (spawnablesProp != null && itemIdx < spawnablesProp.arraySize)
                {
                    SerializedProperty itemProperty = spawnablesProp.GetArrayElementAtIndex(itemIdx);

                    EditorGUI.indentLevel++;

                    SerializedProperty prefabProp = itemProperty.FindPropertyRelative("Prefab");
                    SerializedProperty prefabAddressProp = itemProperty.FindPropertyRelative("PrefabAddress");
                    SerializedProperty positionOnMapProp = itemProperty.FindPropertyRelative("PositionOnMap");
                    SerializedProperty LocalPositionProp = itemProperty.FindPropertyRelative("LocalPosition");
                    SerializedProperty rotationProp = itemProperty.FindPropertyRelative("Rotation");
                    SerializedProperty scaleProp = itemProperty.FindPropertyRelative("Scale");
                    SerializedProperty overridesProp = itemProperty.FindPropertyRelative("propertyOverrides");

                    if (prefabProp != null) EditorGUILayout.PropertyField(prefabProp);

                    if (prefabAddressProp != null)
                    {
                        bool hasAddress = !string.IsNullOrEmpty(prefabAddressProp.stringValue);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(prefabAddressProp, new GUIContent("Prefab Address"));
                        if (hasAddress)
                        {
                            GUI.backgroundColor = new Color(0.3f, 1f, 0.5f);
                            GUILayout.Label("Addressable", EditorStyles.miniButton, GUILayout.Width(100));
                            GUI.backgroundColor = Color.white;
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    if (positionOnMapProp != null) EditorGUILayout.PropertyField(positionOnMapProp, new GUIContent("Position On Map (Z)"));
                    // Fallback to LocalPosition if it exists structurally alongside PositionOnMap
                    if (LocalPositionProp != null) EditorGUILayout.PropertyField(LocalPositionProp, new GUIContent("LocalPosition (X,Y,Z)"));
                    if (rotationProp != null) EditorGUILayout.PropertyField(rotationProp);
                    if (scaleProp != null) EditorGUILayout.PropertyField(scaleProp);

                    if (overridesProp != null)
                    {
                        EditorGUILayout.Space(5);
                        EditorGUILayout.PropertyField(overridesProp, new GUIContent("Property Overrides"), true);
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }
        EditorGUILayout.EndVertical();

        return shouldDelete;
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Total Objects: {_totalItemCount}");
        }
        EditorGUILayout.EndVertical();

        if (_showHelp)
        {
            EditorGUILayout.HelpBox(
                "Tips:\n" +
                "- Objects are spawned based on their PositionOnMap value.\n" +
                "- Use 'Sort by Position' to order the list correctly for gameplay.\n" +
                "- Search by prefab name or EntityType.\n" +
                "- Use Property Overrides to customize variables on spawn.",
                MessageType.Info
            );
        }
    }

    private SerializedProperty GetSpawnablesProp()
    {
        return serializedObject.FindProperty("SpawnableObjects");
    }

    private void AddNewObject()
    {
        Undo.RecordObject(_target, "Add Spawnable Object");
        _expandedItems.Clear();

        SerializedProperty spawnablesProp = GetSpawnablesProp();
        if (spawnablesProp == null) return;

        int newIdx = spawnablesProp.arraySize;
        spawnablesProp.arraySize++;
        SerializedProperty newItem = spawnablesProp.GetArrayElementAtIndex(newIdx);

        InitSpawnableSerializedProperties(newItem, prefab: null, position: 0f, rotation: Vector3.zero, scale: Vector3.one);

        // Auto expand
        _expandedItems.Add(newIdx);

        ApplyChanges();
        GUIUtility.ExitGUI();
    }

    private static void InitSpawnableSerializedProperties(SerializedProperty item,
        ItemUnit prefab, float position, Vector3 rotation, Vector3 scale)
    {
        if (item == null) return;

        SerializedProperty prefabProp = item.FindPropertyRelative("Prefab");
        SerializedProperty prefabAddressProp = item.FindPropertyRelative("PrefabAddress");
        SerializedProperty positionOnMapProp = item.FindPropertyRelative("PositionOnMap");
        SerializedProperty LocalPositionProp = item.FindPropertyRelative("LocalPosition");
        SerializedProperty rotationProp = item.FindPropertyRelative("Rotation");
        SerializedProperty scaleProp = item.FindPropertyRelative("Scale");
        SerializedProperty overridesProp = item.FindPropertyRelative("propertyOverrides");

        if (prefabProp != null) prefabProp.objectReferenceValue = prefab;
        if (prefabAddressProp != null) prefabAddressProp.stringValue = string.Empty;
        if (positionOnMapProp != null) positionOnMapProp.floatValue = position;
        if (LocalPositionProp != null) LocalPositionProp.vector3Value = new Vector3(0, 0, position);
        if (rotationProp != null) rotationProp.vector3Value = rotation;
        if (scaleProp != null) scaleProp.vector3Value = scale;
        if (overridesProp != null) overridesProp.arraySize = 0;
    }

    private static void CopySpawnableSerializedProperties(SerializedProperty src, SerializedProperty dst)
    {
        if (src == null || dst == null) return;

        SerializedProperty srcPrefab = src.FindPropertyRelative("Prefab");
        SerializedProperty srcAddress = src.FindPropertyRelative("PrefabAddress");
        SerializedProperty srcPosOnMap = src.FindPropertyRelative("PositionOnMap");
        SerializedProperty srcPosLocal = src.FindPropertyRelative("LocalPosition");
        SerializedProperty srcRot = src.FindPropertyRelative("Rotation");
        SerializedProperty srcScale = src.FindPropertyRelative("Scale");
        SerializedProperty srcOverrides = src.FindPropertyRelative("propertyOverrides");

        SerializedProperty dstPrefab = dst.FindPropertyRelative("Prefab");
        SerializedProperty dstAddress = dst.FindPropertyRelative("PrefabAddress");
        SerializedProperty dstPosOnMap = dst.FindPropertyRelative("PositionOnMap");
        SerializedProperty dstPosLocal = dst.FindPropertyRelative("LocalPosition");
        SerializedProperty dstRot = dst.FindPropertyRelative("Rotation");
        SerializedProperty dstScale = dst.FindPropertyRelative("Scale");
        SerializedProperty dstOverrides = dst.FindPropertyRelative("propertyOverrides");

        if (dstPrefab != null) dstPrefab.objectReferenceValue = srcPrefab?.objectReferenceValue;
        if (dstAddress != null) dstAddress.stringValue = srcAddress?.stringValue ?? string.Empty;
        if (dstPosOnMap != null) dstPosOnMap.floatValue = srcPosOnMap?.floatValue ?? 0f;
        if (dstPosLocal != null) dstPosLocal.vector3Value = srcPosLocal?.vector3Value ?? Vector3.zero;
        if (dstRot != null) dstRot.vector3Value = srcRot?.vector3Value ?? Vector3.zero;
        if (dstScale != null) dstScale.vector3Value = srcScale?.vector3Value ?? Vector3.one;

        if (dstOverrides != null)
        {
            dstOverrides.arraySize = srcOverrides?.arraySize ?? 0;
            if (srcOverrides != null)
            {
                for (int i = 0; i < srcOverrides.arraySize; i++)
                {
                    SerializedProperty srcEntry = srcOverrides.GetArrayElementAtIndex(i);
                    SerializedProperty dstEntry = dstOverrides.GetArrayElementAtIndex(i);
                    if (srcEntry == null || dstEntry == null) continue;

                    var srcOverride = ReadManagedReference<ItemUnitPropertyOverride>(srcEntry);
                    if (srcOverride == null)
                    {
                        dstEntry.managedReferenceValue = null;
                        continue;
                    }
                    var copy = ClonePropertyOverride(srcOverride);
                    dstEntry.managedReferenceValue = copy;
                }
            }
        }
    }

    private static T ReadManagedReference<T>(SerializedProperty property) where T : class
    {
        if (property == null) return null;
        return property.managedReferenceValue as T;
    }

    private void DuplicateObject(int itemIdx)
    {
        SerializedProperty spawnablesProp = GetSpawnablesProp();
        if (spawnablesProp == null || itemIdx < 0 || itemIdx >= spawnablesProp.arraySize) return;

        Undo.RecordObject(_target, "Duplicate Spawnable Object");
        _expandedItems.Clear();

        SerializedProperty src = spawnablesProp.GetArrayElementAtIndex(itemIdx);
        int insertAt = itemIdx + 1;
        spawnablesProp.InsertArrayElementAtIndex(insertAt);
        SerializedProperty dst = spawnablesProp.GetArrayElementAtIndex(insertAt);

        CopySpawnableSerializedProperties(src, dst);

        // Adjust duplicated position slightly
        SerializedProperty posOnMapProp = dst.FindPropertyRelative("PositionOnMap");
        if (posOnMapProp != null) posOnMapProp.floatValue += 5f;

        SerializedProperty posLocalProp = dst.FindPropertyRelative("LocalPosition");
        if (posLocalProp != null)
        {
            var v = posLocalProp.vector3Value;
            v.z += 5f;
            posLocalProp.vector3Value = v;
        }

        ApplyChanges();
        GUIUtility.ExitGUI();
    }

    private static ItemUnitPropertyOverride ClonePropertyOverride(ItemUnitPropertyOverride original)
    {
        if (original == null) return null;
        System.Type type = original.GetType();
        string json = JsonUtility.ToJson(original);
        return JsonUtility.FromJson(json, type) as ItemUnitPropertyOverride;
    }

    private void DeleteObject(int itemIdx)
    {
        SerializedProperty spawnablesProp = GetSpawnablesProp();
        if (spawnablesProp == null || itemIdx < 0 || itemIdx >= spawnablesProp.arraySize) return;

        Undo.RecordObject(_target, "Delete Spawnable Object");
        _expandedItems.Clear();
        spawnablesProp.DeleteArrayElementAtIndex(itemIdx);

        if (itemIdx < spawnablesProp.arraySize)
        {
            SerializedProperty slot = spawnablesProp.GetArrayElementAtIndex(itemIdx);
            if (slot.propertyType == SerializedPropertyType.ManagedReference && slot.managedReferenceValue == null)
                spawnablesProp.DeleteArrayElementAtIndex(itemIdx);
        }

        ApplyChanges();
        GUIUtility.ExitGUI();
    }

    private void MoveObject(int fromItem, int toItem)
    {
        SerializedProperty spawnablesProp = GetSpawnablesProp();
        if (spawnablesProp == null) return;
        if (fromItem < 0 || fromItem >= spawnablesProp.arraySize) return;

        toItem = Mathf.Clamp(toItem, 0, spawnablesProp.arraySize - 1);
        if (fromItem == toItem) return;

        Undo.RecordObject(_target, "Move Spawnable Object");
        _expandedItems.Clear();

        // Use built-in MoveArrayElement
        spawnablesProp.MoveArrayElement(fromItem, toItem);

        ApplyChanges();
        GUIUtility.ExitGUI();
    }

    private static readonly Dictionary<EntityType, Color> entityTypeColors = new Dictionary<EntityType, Color>
    {
        { EntityType.Wheel, new Color(0.9f, 0.5f, 1f) },
        { EntityType.Character, new Color(1f, 0.7f, 0.3f) },
        { EntityType.Enemy, new Color(0.8f, 0.3f, 1f) },
        { EntityType.ResourceTower, new Color(0.7f, 0.9f, 0.3f) },
        { EntityType.CapacityFactory, new Color(0.3f, 0.6f, 1f) },
        { EntityType.CapacityGate, new Color(0.7f, 0.2f, 0.2f) },
        { EntityType.PowerGate, new Color(1f, 0.9f, 0.2f) },
        { EntityType.SpeedBoard, new Color(0.2f, 1f, 0.6f) },
        { EntityType.Obstacle, new Color(1f, 0.3f, 0.3f) },
        { EntityType.FinishTrigger, new Color(0.3f, 1f, 0.9f) },
        { EntityType.FinishTower, new Color(0.5f, 0.9f, 1f) },
        { EntityType.TowerZone, new Color()},
        { EntityType.GateNewEra, new Color(1f, 0.5f, 0.7f) }
    };

    private Color GetTypeColor(EntityType type)
    {
        if (entityTypeColors.TryGetValue(type, out Color color))
            return color;

        int hash = type.GetHashCode();
        float hue = (hash % 360) / 360f;
        return Color.HSVToRGB(hue, 0.5f, 0.9f);
    }
}
#endif
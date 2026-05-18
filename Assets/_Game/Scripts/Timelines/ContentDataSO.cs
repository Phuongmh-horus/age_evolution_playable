using System.Collections.Generic;
using GamePlay.Entities;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewContent", menuName = "Game Config/Content", order = 4)]
public class ContentDataSO : ScriptableObject
{
    [Header("Content Information")]
    [Tooltip("ID duy nhất của content")]
    public int ContentId;

    [Tooltip("Tên content (để dễ quản lý)")]
    public string ContentName;

    [Tooltip("Mô tả content")]
    [TextArea(2, 4)]
    public string Description;

    [FormerlySerializedAs("Objects")]
    [Header("Spawnable Objects")]
    [Tooltip("Danh sách các object được sắp xếp từ đầu đến cuối map")]
    public List<SpawnableObject> SpawnableObjects = new List<SpawnableObject>();

    /// <summary>
    /// Lấy tất cả objects theo loại
    /// </summary>
    public List<SpawnableObject> GetObjectsByType(EntityType type)
    {
        return SpawnableObjects.FindAll(obj => obj.Prefab.EntityType == type);
    }

    /// <summary>
    /// Lấy object tại vị trí gần nhất
    /// </summary>
    public SpawnableObject GetObjectAtPosition(float position, float tolerance = 1f)
    {
        return SpawnableObjects.Find(obj => Mathf.Abs(obj.PositionOnMap - position) <= tolerance);
    }

    private void OnValidate()
    {
        // AUTO-SORT DISABLED: Prevents items from reordering during editing
        // This was causing DataIndex mismatches when editing property overrides
        // Use the "Sort by Position" button in the inspector to manually sort

        // SpawnableObjects.Sort((a, b) => a.PositionOnMap.CompareTo(b.PositionOnMap));

#if UNITY_EDITOR
        // Validate and fix shared property override references
        ValidatePropertyOverrideReferences();
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Manually sort SpawnableObjects by PositionOnMap
    /// Call this when you want to reorder items
    /// </summary>
    [ContextMenu("Sort by Position")]
    public void SortByPosition()
    {
        SpawnableObjects.Sort((a, b) => a.PositionOnMap.CompareTo(b.PositionOnMap));
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

#if UNITY_EDITOR
    /// <summary>
    /// Validate that no property overrides are shared between multiple SpawnableObjects
    /// </summary>
    private void ValidatePropertyOverrideReferences()
    {
        var seenReferences = new System.Collections.Generic.HashSet<ItemUnitPropertyOverride>();
        bool foundShared = false;

        foreach (var obj in SpawnableObjects)
        {
            if (obj.propertyOverrides != null)
            {
                foreach (var propertyOverride in obj.propertyOverrides)
                {
                    if (propertyOverride != null)
                    {
                        if (!seenReferences.Add(propertyOverride))
                        {
                            // Shared reference detected!
                            foundShared = true;
                            Debug.LogWarning($"[ContentDataSO] Shared property override reference detected! This can cause unexpected behavior.");
                        }
                    }
                }
            }
        }

        if (foundShared)
        {
            Debug.LogWarning($"[ContentDataSO] Use the 'Fix Shared References' button in the inspector to resolve this issue.");
        }
    }
#endif
}

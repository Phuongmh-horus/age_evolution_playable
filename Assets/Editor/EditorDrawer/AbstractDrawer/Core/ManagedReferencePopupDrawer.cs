using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public abstract class ManagedReferencePopupDrawer<TBase> : PropertyDrawer
{
    private static readonly Dictionary<string, string> backupStore = new Dictionary<string, string>();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        EnsureValue(property);

        var currentType = property.managedReferenceValue != null
            ? property.managedReferenceValue.GetType()
            : null;

        int currentIndex = ManagedReferenceTypeCache<TBase>.IndexOf(currentType);
        var popupRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        int selectedIndex = EditorGUI.Popup(popupRect, label, currentIndex, ManagedReferenceTypeCache<TBase>.DisplayNames);

        if (selectedIndex != currentIndex)
        {
            SwitchType(property, selectedIndex);
            currentType = property.managedReferenceValue != null
                ? property.managedReferenceValue.GetType()
                : null;
        }

        if (ShouldDrawChildren(currentType))
        {
            DrawManagedReferenceChildren(position, property, popupRect.yMax + EditorGUIUtility.standardVerticalSpacing);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        var currentType = property.managedReferenceValue != null
            ? property.managedReferenceValue.GetType()
            : null;

        if (!ShouldDrawChildren(currentType))
            return height;

        var child = property.Copy();
        var end = child.GetEndProperty();
        bool enterChildren = true;

        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            height += EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(child, true);
        }

        return height;
    }

    protected virtual bool ShouldDrawChildren(Type currentType)
    {
        return currentType != null && currentType != typeof(TBase);
    }

    private static void EnsureValue(SerializedProperty property)
    {
        if (property.managedReferenceValue != null)
            return;

        var defaultType = ManagedReferenceTypeCache<TBase>.Types.Length > 0
            ? ManagedReferenceTypeCache<TBase>.Types[0]
            : null;

        if (defaultType == null)
            return;

        property.managedReferenceValue = Activator.CreateInstance(defaultType, true);
        property.serializedObject.ApplyModifiedProperties();
    }

    private static void SwitchType(SerializedProperty property, int selectedIndex)
    {
        BackupCurrentValue(property);

        var selectedType = ManagedReferenceTypeCache<TBase>.Types[selectedIndex];
        object newValue = null;

        if (selectedType != null)
        {
            newValue = Activator.CreateInstance(selectedType, true);
            RestoreValue(property, selectedType, newValue);
        }

        property.managedReferenceValue = newValue;
        property.serializedObject.ApplyModifiedProperties();
    }

    private static void BackupCurrentValue(SerializedProperty property)
    {
        var currentValue = property.managedReferenceValue;
        if (currentValue == null)
            return;

        var currentType = currentValue.GetType();
        backupStore[GetBackupKey(property, currentType)] = JsonUtility.ToJson(currentValue);
    }

    private static void RestoreValue(SerializedProperty property, Type selectedType, object newValue)
    {
        if (!backupStore.TryGetValue(GetBackupKey(property, selectedType), out string json))
            return;

        JsonUtility.FromJsonOverwrite(json, newValue);
    }

    private static string GetBackupKey(SerializedProperty property, Type type)
    {
        int targetId = property.serializedObject.targetObject != null
            ? property.serializedObject.targetObject.GetInstanceID()
            : 0;

        return $"{targetId}:{property.propertyPath}:{typeof(TBase).AssemblyQualifiedName}:{type.AssemblyQualifiedName}";
    }

    private static void DrawManagedReferenceChildren(Rect position, SerializedProperty property, float y)
    {
        var child = property.Copy();
        var end = child.GetEndProperty();
        bool enterChildren = true;

        EditorGUI.indentLevel++;

        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;

            float height = EditorGUI.GetPropertyHeight(child, true);
            var childRect = new Rect(position.x, y, position.width, height);
            EditorGUI.PropertyField(childRect, child, true);
            y += height + EditorGUIUtility.standardVerticalSpacing;
        }

        EditorGUI.indentLevel--;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ManagedReferenceTypeCache<TBase>
{
    private static Type[] types;
    private static GUIContent[] displayNames;

    public static Type[] Types
    {
        get
        {
            EnsureInitialized();
            return types;
        }
    }

    public static GUIContent[] DisplayNames
    {
        get
        {
            EnsureInitialized();
            return displayNames;
        }
    }

    public static int IndexOf(Type type)
    {
        EnsureInitialized();

        for (int i = 0; i < types.Length; i++)
        {
            if (types[i] == type)
                return i;
        }

        return 0;
    }

    private static void EnsureInitialized()
    {
        if (types != null)
            return;

        var discoveredTypes = new List<Type>();
        var baseType = typeof(TBase);

        discoveredTypes.Add(baseType.IsAbstract || baseType.IsGenericTypeDefinition ? null : baseType);

        discoveredTypes.AddRange(TypeCache.GetTypesDerivedFrom<TBase>()
            .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition)
            .OrderBy(type => type.Name));

        types = discoveredTypes.ToArray();
        displayNames = new GUIContent[types.Length];

        for (int i = 0; i < types.Length; i++)
        {
            displayNames[i] = new GUIContent(i == 0
                ? "None"
                : ObjectNames.NicifyVariableName(types[i].Name));
        }
    }
}

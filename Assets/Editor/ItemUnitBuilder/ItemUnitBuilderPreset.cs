using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Editor.ItemUnitBuilder
{
    [CreateAssetMenu(fileName = "ItemUnitBuilderPreset", menuName = "Tools/Item Unit Builder Preset")]
    public class ItemUnitBuilderPreset : ScriptableObject
    {
        public string prefabName;
        public string outputFolder = "Assets/_Game/Resources/Prefabs/Items";
        public string coreItemUnitTypeName;
        public List<string> subComponentTypeNames = new List<string>();
        public GameObject sourcePrefab;
        public GameObject visualPrefab;
        public Vector3 visualLocalPosition;
        public Vector3 visualLocalRotationEuler;
        public Vector3 visualLocalScale = Vector3.one;
        public bool createRootNode = true;
        public bool createVirsualNode = true;
        public bool overwriteIfExists;
    }

    public class ItemUnitBuilderRequest
    {
        public string PrefabName;
        public string OutputFolder;
        public System.Type CoreItemUnitType;
        public List<System.Type> SubComponentTypes = new List<System.Type>();
        public GameObject SourcePrefab;
        public Component CoreDataSource;
        public List<Component> SubComponentDataSources = new List<Component>();
        public GameObject VisualPrefab;
        public Vector3 VisualLocalPosition;
        public Vector3 VisualLocalRotationEuler;
        public Vector3 VisualLocalScale = Vector3.one;
        public bool CreateRootNode = true;
        public bool CreateVirsualNode = true;
        public bool OverwriteIfExists;
    }

    public class ItemUnitBuilderValidationResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    public class ItemUnitBuilderBuildResult
    {
        public bool Success;
        public string PrefabPath;
        public readonly List<string> Messages = new List<string>();
        public ItemUnitBuilderValidationResult Validation;
    }
}

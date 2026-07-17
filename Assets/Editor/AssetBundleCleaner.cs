#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
public class AssetBundleCleaner
{
    [MenuItem("Tools/Clear AssetBundle Name From All Assets")]
    static void ClearAllAssetBundleNamesFromAssets()
    {
        string[] allAssetGUIDs = AssetDatabase.FindAssets(""); // tìm tất cả asset
        int clearedCount = 0;

        foreach (string guid in allAssetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);

            if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName))
            {
                Debug.Log($"🧹 Đã xoá AssetBundle name \"{importer.assetBundleName}\" từ asset: {assetPath}");
                importer.assetBundleName = null;
                clearedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"✅ Đã xoá AssetBundle name từ {clearedCount} asset.");
    }

    [MenuItem("Tools/Clear All AssetBundle Names (From Settings)")]
    static void ClearAllAssetBundleNamesFromSettings()
    {
        string[] allBundleNames = AssetDatabase.GetAllAssetBundleNames();
        if (allBundleNames.Length == 0)
        {
            Debug.Log("ℹ️ Không có AssetBundle name nào trong settings.");
            return;
        }

        foreach (string bundle in allBundleNames)
        {
            Debug.Log($"🗑 Đang xoá AssetBundle name trong settings: {bundle}");
            AssetDatabase.RemoveAssetBundleName(bundle, true); // remove assets using this bundle name
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"✅ Đã xoá {allBundleNames.Length} AssetBundle name trong settings.");
    }
}
#endif
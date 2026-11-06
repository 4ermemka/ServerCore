using System;
using System.IO;
using System.Linq;
using ServerCore.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Миграция типа ScrollRect с сохранением fileID и всех serialized ссылок prefab-а.</summary>
public static class InventoryInputSetup
{
    [MenuItem("Tools/Inventory/Upgrade input contexts")]
    public static void Setup()
    {
        var probe = new GameObject("ContextScrollRect script", typeof(RectTransform));
        var script = MonoScript.FromMonoBehaviour(probe.AddComponent<ContextScrollRect>());
        string newGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
        UnityEngine.Object.DestroyImmediate(probe);
        var oldGuids = AssetDatabase.FindAssets("t:MonoScript")
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => Path.GetFileName(path) == "ScrollRect.cs")
            .Select(AssetDatabase.AssetPathToGUID).ToArray();
        if (oldGuids.Length != 1) throw new InvalidOperationException("Expected the installed UGUI ScrollRect script.");
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Inventory", "Assets/Prefabs/UI" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            string before = File.ReadAllText(path);
            string after = before.Replace("guid: " + oldGuids[0] + ",", "guid: " + newGuid + ",");
            if (after != before) { File.WriteAllText(path, after); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
        }
        const string demoPath = "Assets/Prefabs/Inventory/InventoryDemo.prefab";
        var demo = PrefabUtility.LoadPrefabContents(demoPath);
        try
        {
            if (demo.GetComponent<InventoryInputCoordinator>() == null) demo.AddComponent<InventoryInputCoordinator>();
            PrefabUtility.SaveAsPrefabAsset(demo, demoPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(demo); }
        AssetDatabase.SaveAssets();
        Debug.Log("Inventory contexts: ScrollRect links preserved; InventoryDemo coordinator installed.");
    }
}

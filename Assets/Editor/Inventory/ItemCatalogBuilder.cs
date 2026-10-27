using System.IO;
using System.Linq;
using ServerCore.Inventory;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Автопоиск всех ItemDefinition и их подклассов в Data; ссылки сохраняются для runtime/build.</summary>
[InitializeOnLoad]
public sealed class ItemCatalogBuilder : AssetPostprocessor, IPreprocessBuildWithReport
{
    public const string CatalogPath = "Assets/Resources/Inventory/ItemCatalog.asset";
    private static bool _pending;
    public int callbackOrder => 0;

    static ItemCatalogBuilder() => Schedule();

    [MenuItem("Tools/Inventory/Refresh Item Catalog")]
    public static ItemCatalog Refresh()
    {
        var items = AssetDatabase.IsValidFolder("Assets/Data")
            ? AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, System.StringComparer.Ordinal)
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<ItemDefinition>().Distinct().ToArray()
            : new ItemDefinition[0];
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        if (catalog == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            AssetDatabase.Refresh();
            catalog = UnityEngine.ScriptableObject.CreateInstance<ItemCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        if (catalog.Items == null || !catalog.Items.SequenceEqual(items))
        {
            catalog.SetItems(items);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
        return catalog;
    }

    public void OnPreprocessBuild(BuildReport report) => Refresh();

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(path => path.StartsWith("Assets/Data/"))) Schedule();
    }

    private static void Schedule()
    {
        if (_pending) return;
        _pending = true;
        EditorApplication.delayCall += () =>
        {
            _pending = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Schedule(); return; }
            Refresh();
        };
    }
}

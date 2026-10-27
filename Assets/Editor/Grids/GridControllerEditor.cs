using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(GridGenerator))]
public class GridGeneratorEditor : Editor
{
    // Undo восстанавливает GameObject-ы и сериализованных соседей, но не runtime-словари провайдера.
    [InitializeOnLoadMethod]
    private static void ConnectUndo()
    {
        Undo.undoRedoPerformed -= RebuildAfterUndo;
        Undo.undoRedoPerformed += RebuildAfterUndo;
    }

    private static void RebuildAfterUndo()
    {
        foreach (var provider in Object.FindObjectsByType<GridProvider>(FindObjectsInactive.Include))
            provider.RebuildIndex();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var generator = (GridGenerator)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Grid", GUILayout.Height(30)))
        {
            generator.GenerateGrid();
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
        if (GUILayout.Button("Clear Grid", GUILayout.Height(20)))
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clear Grid");
            generator.ClearExisting();
            Undo.CollapseUndoOperations(group);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    static void DrawConnections(GridProvider holder, GizmoType type)
    {
        if (holder != null) holder.DrawConnections();
    }
}

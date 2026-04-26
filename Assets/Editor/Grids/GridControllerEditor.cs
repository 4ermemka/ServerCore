using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridGenerator))]
public class GridGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var generator = (GridGenerator)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Grid", GUILayout.Height(30)))
        {
            Undo.RecordObject(generator, "Generate Grid");
            generator.GenerateGrid();
            EditorUtility.SetDirty(generator);
        }
        if (GUILayout.Button("Clear Grid", GUILayout.Height(20)))
        {
            Undo.RecordObject(generator, "Clear Grid");
            generator.ClearExisting();
            EditorUtility.SetDirty(generator);
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    static void DrawConnections(GridHolder holder, GizmoType type)
    {
        if (holder != null) holder.DrawConnections();
    }
}
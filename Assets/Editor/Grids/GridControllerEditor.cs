using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridController))]
public class GridControllerEditor : Editor
{
    private GridController controller;

    private void OnEnable()
    {
        controller = (GridController)target;
        EditorApplication.update += Repaint;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Grid", GUILayout.Height(30)))
        {
            Undo.RecordObject(controller, "Generate Grid");
            controller.GenerateGrid();
            EditorUtility.SetDirty(controller);
        }

        if (GUILayout.Button("Clear Grid", GUILayout.Height(20)))
        {
            Undo.RecordObject(controller, "Clear Grid");
            controller.ClearGrid();
            EditorUtility.SetDirty(controller);
        }

        if (controller.AllCells != null)
        {
            EditorGUILayout.HelpBox($"Total cells: {controller.AllCells.Count}", MessageType.Info);
        }
    }

    private void OnSceneGUI()
    {
        if (controller != null)
        {
            controller.DrawEditorConnections();
        }
    }
}
using UnityEditor;
using UnityEngine;

// Кастомный инспектор для кисти
[CustomEditor(typeof(LevelBrush))]
public class LevelBrushEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelBrush brush = (LevelBrush)target;

        if (GUILayout.Button("Brush Mode: " + (brush.isPainting ? "ON" : "OFF")))
        {
            brush.isPainting = !brush.isPainting;
        }

        if (brush.isPainting)
        {
            EditorGUILayout.HelpBox(
                "Paint Mode Active:\n" +
                "LMB - Place object\n" +
                "RMB - Remove object\n" +
                "Mouse Wheel - Rotate\n" +
                "Shift + Wheel - Scale",
                MessageType.Info
            );
        }
    }
}

[ExecuteInEditMode]
public class LevelBrush : MonoBehaviour
{
    public bool isPainting = false;
    public GameObject brushPrefab;
    public LayerMask paintSurface;

    void OnSceneGUI()
    {
        if (!isPainting) return;

        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction);

        if (hit.collider != null && (paintSurface.value & (1 << hit.collider.gameObject.layer)) != 0)
        {
            // Рисуем превью кисти
            Handles.color = Color.green;
            Handles.DrawWireDisc(hit.point, Vector3.forward, 0.5f);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                GameObject newObj = PrefabUtility.InstantiatePrefab(brushPrefab) as GameObject;
                newObj.transform.position = hit.point;
                newObj.transform.SetParent(transform);

                // Случайный поворот для натуральности
                newObj.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0, 360));

                Undo.RegisterCreatedObjectUndo(newObj, "Paint Object");
            }
        }

        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
    }
}
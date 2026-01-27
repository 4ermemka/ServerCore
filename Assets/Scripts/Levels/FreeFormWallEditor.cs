using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Assets.Scripts.Levels
{
    #if UNITY_EDITOR
    [CustomEditor(typeof(FreeformWall))]
    public class FreeformWallEditor : Editor
    {
        private FreeformWall wall;
        private bool editMode = false;

        private void OnEnable()
        {
            wall = (FreeformWall)target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            editMode = GUILayout.Toggle(editMode, "Edit Points", "Button");

            if (editMode)
            {
                EditorGUILayout.HelpBox("Enter point editing mode", MessageType.Info);

                if (GUILayout.Button("Add Point"))
                {
                    wall.AddPoint(new Vector2(0, 0));
                }

                if (GUILayout.Button("Reset to Rectangle"))
                {
                    wall.UpdateWall(wall.GetDefaultPoints());
                }
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(wall);
            }
        }

        void OnSceneGUI()
        {
            if (!editMode || wall == null) return;

            var points = wall.GetPoints();
            for (int i = 0; i < points.Count; i++)
            {
                // Преобразуем локальные координаты в мировые
                Vector3 worldPoint = wall.transform.TransformPoint(points[i]);

                // Рисуем ручки для редактирования точек
                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPoint = Handles.PositionHandle(worldPoint, Quaternion.identity);

                if (EditorGUI.EndChangeCheck())
                {
                    // Преобразуем обратно в локальные координаты
                    Vector2 newLocalPoint = wall.transform.InverseTransformPoint(newWorldPoint);
                    List<Vector2> newPoints = new List<Vector2>(points);
                    newPoints[i] = newLocalPoint;
                    wall.UpdateWall(newPoints);
                }

                // Подписываем точки
                Handles.Label(worldPoint, $"Point {i}");
            }
        }
    }
#endif
}
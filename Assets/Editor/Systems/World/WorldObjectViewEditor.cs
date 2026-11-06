using ServerCore.World;
using UnityEditor;

namespace ServerCore.Editor.World
{
    [CustomEditor(typeof(WorldObjectView), true)]
    public sealed class WorldObjectViewEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var view = (WorldObjectView)target;
            if (view.Data == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Data / Object Id", view.Data.Id.Value);
            EditorGUILayout.LabelField("Data / Cell (X, Y, Z)", view.Data.Cell.Value.ToString());
        }
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;
    }
}

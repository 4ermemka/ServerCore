using System.IO;
using System.Linq;
using Assets.Scripts.DebugAndTest;
using ServerCore.World;
using ServerCore.World.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ServerCore.Editor.World
{
    public static class GameSessionSetup
    {
        [MenuItem("Tools/World/Set up game session")]
        public static void Setup()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var session = Object.FindAnyObjectByType<GameSessionController>();
            if (session == null)
            {
                Directory.CreateDirectory("Assets/Data/Planning");
                var palette = AssetDatabase.LoadAssetAtPath<PlanPalette>("Assets/Data/Planning/PlanPalette.asset");
                if (palette == null)
                {
                    palette = ScriptableObject.CreateInstance<PlanPalette>();
                    AssetDatabase.CreateAsset(palette, "Assets/Data/Planning/PlanPalette.asset");
                }
                var root = new GameObject("Game Session");
                var holder = root.AddComponent<WorldDataHolder>();
                var network = root.AddComponent<DmNetworkController>();
                session = root.AddComponent<GameSessionController>();
                var serialized = new SerializedObject(session);
                serialized.FindProperty("dataHolder").objectReferenceValue = holder;
                serialized.FindProperty("networkSource").objectReferenceValue = network;
                serialized.FindProperty("palette").objectReferenceValue = palette;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == scene.path)) scenes.Insert(0, new EditorBuildSettingsScene(scene.path, true));
            else scenes.First(s => s.path == scene.path).enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }
}

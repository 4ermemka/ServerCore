using System;
using System.Linq;
using ServerCore.World;
using Shared.CameraSystem;
using Shared.Inputs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ServerCore.Editor.CameraSetup
{
    /// <summary>Однократная миграция Test.unity; повторный запуск сохраняет авторские настройки новой камеры.</summary>
    public static class CameraSceneSetup
    {
        [MenuItem("Tools/Camera/Set up Test scene")]
        public static void Setup()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var manager = UnityEngine.Object.FindAnyObjectByType<CameraManager>();
            var camera = Camera.main;
            var world = UnityEngine.Object.FindAnyObjectByType<WorldSceneController>();
            if (manager == null || camera == null || world == null)
                throw new InvalidOperationException("Не найдены CameraManager, Main Camera или World.");

            var behaviour = manager.GetComponent<IsometricCamera>();
            // RequireComponent может добавить behaviour ещё при загрузке старой сцены.
            bool migrate = new SerializedObject(manager).FindProperty("defaultCamera").objectReferenceValue == null
                || world.transform.Find("Furniture Camera Focus") != null;
            if (behaviour == null) behaviour = manager.gameObject.AddComponent<IsometricCamera>();
            if (migrate)
            {
                var marker = world.transform.Find("Player Camera Focus");
                var player = UnityEngine.Object.FindObjectsByType<WorldObjectView>().First(v => v.SceneId == "scene-player");
                if (marker == null)
                {
                    marker = new GameObject("Player Camera Focus").transform;
                    marker.SetParent(world.transform, false);
                }
                marker.position = player.transform.position;
                var adapter = marker.GetComponent<WorldCameraFocus>();
                if (adapter == null) adapter = marker.gameObject.AddComponent<WorldCameraFocus>();
                var adapterSo = new SerializedObject(adapter);
                adapterSo.FindProperty("world").objectReferenceValue = world;
                adapterSo.FindProperty("objectId").stringValue = "scene-player";
                adapterSo.ApplyModifiedPropertiesWithoutUndo();
                behaviour.Focus = marker;
                var grid = new SerializedObject(world).FindProperty("gridProvider").objectReferenceValue as Component;
                behaviour.GridFrame = grid != null ? grid.transform : null;
                camera.orthographic = true;
                camera.orthographicSize = 5;
                var oldFocus = world.transform.Find("Furniture Camera Focus");
                if (oldFocus != null) UnityEngine.Object.DestroyImmediate(oldFocus.gameObject);
            }

            // Старый физический контроллер ещё доступен при первой миграции. Определяем
            // по GUID, чтобы этот editor-скрипт компилировался и после удаления legacy-файла.
            foreach (var component in camera.GetComponents<MonoBehaviour>())
            {
                if (component == null) continue;
                var script = MonoScript.FromMonoBehaviour(component);
                if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)) != "d2b2018857d34384f87a91180e7fcb65") continue;
                UnityEngine.Object.DestroyImmediate(component);
                foreach (var sphere in camera.GetComponents<SphereCollider>())
                    if (sphere.isTrigger) UnityEngine.Object.DestroyImmediate(sphere);
            }

            manager.Configure(camera, behaviour);
            camera.transform.SetParent(manager.transform, true);
            manager.Tick(0);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(behaviour);
            EditorUtility.SetDirty(camera);

            var profile = AssetDatabase.LoadAssetAtPath<GameInputProfile>("Assets/Data/Input/MainInputProfile.asset");
            var inputSo = new SerializedObject(profile);
            var actions = inputSo.FindProperty("actions");
            for (int i = actions.arraySize - 1; i >= 0; i--)
            {
                var action = actions.GetArrayElementAtIndex(i);
                string id = action.FindPropertyRelative("id").stringValue;
                if (id == "Camera.ZoomIn" || id == "Camera.ZoomOut") actions.DeleteArrayElementAtIndex(i);
                else if (id == "Camera.RotateLeft" || id == "Camera.RotateRight")
                    action.FindPropertyRelative("trigger").enumValueIndex = (int)ActionTrigger.Hold;
            }
            inputSo.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            foreach (var path in new[] {
                "Assets/Shared/Scripts/Systems/CameraSystem/EllipticalOrbit.cs",
                "Assets/Shared/Scripts/Systems/CameraSystem/EllipticalCameraSettings.cs",
                "Assets/Shared/Scripts/Systems/CameraSystem/CameraFocusTarget.cs",
                "Assets/Shared/Scripts/Systems/CameraSystem/CameraSphereConstraint.cs",
                "Assets/Shared/Scripts/Systems/CameraSystem/Editor",
                "Assets/Data/Camera/TestCameraSettings.asset"
            }) if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) AssetDatabase.DeleteAsset(path);
            Debug.Log("Camera: CameraDirector, один фокус, изометрия 40/45°, Q/E Hold, без зума.");
        }
    }
}

using System;
using System.IO;
using System.Linq;
using ServerCore.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace ServerCore.Editor.World
{
    /// <summary>Однократная миграция тестовой сцены; повторный запуск не перезаписывает настроенные префабы.</summary>
    public static class WorldSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/Test.unity";
        private const string PrefabFolder = "Assets/Prefabs/World";

        [MenuItem("Tools/World/Set up Test scene")]
        public static void Setup()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var existing = UnityEngine.Object.FindAnyObjectByType<WorldSceneController>();
            if (existing != null)
            {
                EnsureCameraFocus(existing);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("World: сцена уже настроена; изменяйте параметры в Inspector.");
                return;
            }
            Directory.CreateDirectory(PrefabFolder + "/Meshes");
            AssetDatabase.Refresh();
            var player = scene.GetRootGameObjects().Single(go => go.name == "Player");
            var objects = scene.GetRootGameObjects().Single(go => go.name == "Objects");
            var piles = objects.GetComponentsInChildren<Transform>().Where(t => t.name == "PileOfBoxes" && t.parent == objects.transform).ToArray();
            if (piles.Length == 0) throw new InvalidOperationException("Objects/PileOfBoxes не найден.");

            var views = new WorldObjectView[piles.Length + 1];
            var prefabs = new WorldObjectView[views.Length];
            (views[0], prefabs[0]) = MakePrefab<PlayerObjectView>(player, "Player", "player", "scene-player");
            for (int i = 0; i < piles.Length; i++)
            {
                string suffix = i == 0 ? "" : "Variant" + i;
                (views[i + 1], prefabs[i + 1]) = MakePrefab<FurnitureObjectView>(piles[i].gameObject,
                    "PileOfBoxes" + suffix, "furniture.pile" + suffix, "scene-pile-" + (i + 1));
            }

            var root = new GameObject("World");
            var holder = root.AddComponent<WorldDataHolder>();
            var controller = root.AddComponent<WorldSceneController>();
            var grid = UnityEngine.Object.FindFirstObjectByType<GridProvider>();
            if (grid == null) throw new InvalidOperationException("GridProvider не найден.");
            var so = new SerializedObject(controller);
            so.FindProperty("dataHolder").objectReferenceValue = holder;
            so.FindProperty("gridProvider").objectReferenceValue = grid;
            so.FindProperty("spawnedObjectsRoot").objectReferenceValue = objects.transform;
            SetArray(so.FindProperty("sceneObjects"), views);
            SetArray(so.FindProperty("prefabs"), prefabs);
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureCameraFocus(controller);

            var controls = root.AddComponent<WorldDemoControls>();
            var inventory = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "InventoryDemo");
            if (inventory != null)
            {
                var controlsSo = new SerializedObject(controls);
                controlsSo.FindProperty("inventoryDemo").objectReferenceValue = inventory;
                controlsSo.ApplyModifiedPropertiesWithoutUndo();
                inventory.SetActive(false);
            }
            if (Camera.main == null) throw new InvalidOperationException("Не найдена MainCamera.");
            if (Camera.main.GetComponent<PhysicsRaycaster>() == null) Camera.main.gameObject.AddComponent<PhysicsRaycaster>();
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"World: подготовлены Player, {piles.Length} Furniture и WorldState v2.");
        }

        private static (WorldObjectView view, WorldObjectView prefab) MakePrefab<T>(GameObject go, string filename, string definition, string id)
            where T : WorldObjectView
        {
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException($"{go.name}: отсутствует геометрия.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var anchor = go.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            var view = go.AddComponent<T>();
            var so = new SerializedObject(view);
            so.FindProperty("definitionId").stringValue = definition;
            so.FindProperty("anchorLocal").vector3Value = anchor;
            so.ApplyModifiedPropertiesWithoutUndo();
            // FBX игрока не содержит физического hit target; существующие collider мебели сохраняем.
            if (go.GetComponentsInChildren<Collider>().Length == 0)
            {
                var collider = go.AddComponent<BoxCollider>();
                collider.center = go.transform.InverseTransformPoint(bounds.center);
                collider.size = new Vector3(bounds.size.x / Mathf.Abs(go.transform.lossyScale.x),
                    bounds.size.y / Mathf.Abs(go.transform.lossyScale.y), bounds.size.z / Mathf.Abs(go.transform.lossyScale.z));
            }
            // ProBuilder mesh может быть вложенным объектом сцены. Сохраняем отдельный asset,
            // иначе prefab потерял бы геометрию вне исходной сцены.
            int meshIndex = 0;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                string path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                var copy = UnityEngine.Object.Instantiate(mesh);
                copy.name = filename + "_" + meshIndex;
                AssetDatabase.CreateAsset(copy, $"{PrefabFolder}/Meshes/{filename}_{meshIndex++}.asset");
                filter.sharedMesh = copy;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider != null) collider.sharedMesh = copy;
            }
            var asset = PrefabUtility.SaveAsPrefabAssetAndConnect(go, $"{PrefabFolder}/{filename}.prefab", InteractionMode.AutomatedAction);
            so = new SerializedObject(view);
            so.FindProperty("sceneId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            return (view, asset.GetComponent<T>());
        }

        private static void SetArray(SerializedProperty property, WorldObjectView[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void EnsureCameraFocus(WorldSceneController controller)
        {
            if (UnityEngine.Object.FindAnyObjectByType<WorldCameraFocus>() != null) return;
            var cameraManager = UnityEngine.Object.FindAnyObjectByType<CameraManager>();
            if (cameraManager == null) return;
            var go = new GameObject("Player Camera Focus");
            go.transform.SetParent(controller.transform, false);
            var focus = go.AddComponent<WorldCameraFocus>();
            var so = new SerializedObject(focus);
            so.FindProperty("world").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
            cameraManager.SetFocusPoint(go.transform);
            var provider = new SerializedObject(controller).FindProperty("gridProvider").objectReferenceValue as Component;
            cameraManager.DefaultCamera.GridFrame = provider != null ? provider.transform : null;
            EditorUtility.SetDirty(cameraManager.DefaultCamera);
            EditorUtility.SetDirty(cameraManager);
        }
    }
}

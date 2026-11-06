using System;
using System.IO;
using System.Linq;
using Assets.Scripts.DebugAndTest;
using ServerCore.World;
using ServerCore.World.Entities;
using ServerCore.World.Presentation;
using Shared.CameraSystem;
using Shared.Inputs;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ServerCore.Editor.Entities
{
    /// <summary>Builds the entity example once. Existing authored assets are never overwritten.</summary>
    public static class EntitySceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/EntityTestScene.unity";
        private const string PrefabFolder = "Assets/Prefabs/Entities";
        private const string ArtFolder = "Assets/Art/Entities";
        private const string MaterialFolder = "Assets/Data/Entities";

        [MenuItem("Tools/World/Create EntityTestScene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                Debug.Log("EntityTestScene already exists; preserving its authored contents.");
                return;
            }
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var groundMaterial = Material("Ground", new Color(.23f, .29f, .32f));
            var playerMaterial = Material("Player", new Color(.72f, .79f, .80f));
            var baseMaterial = Material("PlayerBase", new Color(.09f, .42f, .50f));
            var player = BuildPlayer(playerMaterial, baseMaterial);
            var furniture = BuildFurniture();

            var environment = new GameObject("Environment");
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.SetParent(environment.transform, false);
            plane.transform.localScale = new Vector3(1.8f, 1, 1.4f);
            plane.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            var gridObject = new GameObject("Grid");
            var cells = new GameObject("Cells").transform;
            cells.SetParent(gridObject.transform, false);
            var provider = gridObject.AddComponent<GridProvider>();
            Set(provider, "cellsContainer", cells);
            var generator = gridObject.AddComponent<GridGenerator>();
            Set(generator, "targetsContainer", environment.transform);
            Set(generator, "gridHolder", provider);
            Set(generator, "cellsContainer", cells);
            Set(generator, "cellPrefab", AssetDatabase.LoadAssetAtPath<GridCell>("Assets/Shared/Prefabs/Grid/Cell.prefab"));
            generator.GenerateGrid();
            if (provider.AllCells.Count == 0) throw new InvalidOperationException("The generated entity grid is empty.");

            var entities = new GameObject("Entities").transform;
            var dataObject = new GameObject("Data");
            dataObject.layer = EnsureDataLayer();
            var data = dataObject.AddComponent<WorldDataObject>();
            Set(data, "_gridProvider", provider);
            Set(data, "_entitiesRoot", entities);
            var dataSo = new SerializedObject(data);
            var catalog = dataSo.FindProperty("_prefabs");
            catalog.arraySize = 2;
            catalog.GetArrayElementAtIndex(0).objectReferenceValue = player;
            catalog.GetArrayElementAtIndex(1).objectReferenceValue = furniture;
            dataSo.ApplyModifiedPropertiesWithoutUndo();

            var sessionRoot = new GameObject("Game Session");
            var holder = sessionRoot.AddComponent<WorldDataHolder>();
            var network = sessionRoot.AddComponent<DmNetworkController>();
            var session = sessionRoot.AddComponent<GameSessionController>();
            Set(network, "_worldDataHolder", holder);
            Set(session, "dataHolder", holder);
            Set(session, "networkSource", network);
            Set(session, "palette", AssetDatabase.LoadAssetAtPath<PlanPalette>("Assets/Data/Planning/PlanPalette.asset"));
            var world = dataObject.AddComponent<WorldSceneController>();
            Set(world, "dataHolder", holder);
            Set(world, "entityData", data);
            Set(world, "gridProvider", provider);
            Set(world, "spawnedObjectsRoot", entities);
            var spawner = dataObject.AddComponent<PlayerEntitySpawner>();
            Set(spawner, "_playerPrefab", player);

            foreach (var position in new[] { new Vector3(-5, 0, -3), new Vector3(4, 0, 3), new Vector3(-3, 0, 4), new Vector3(5, 0, -3), new Vector3(-6, 0, 2) })
            {
                var entity = (GameObject)PrefabUtility.InstantiatePrefab(furniture.gameObject, scene);
                entity.transform.SetParent(entities, false);
                var view = entity.GetComponent<FurnitureEntity>();
                var cell = provider.GetClosestCell(position);
                entity.transform.position = cell.GetWorldPosition(provider.CellSize);
                entity.transform.localRotation = Quaternion.Euler(0, (data.Entities.Count % 4) * 90, 0);
                string id = "furniture:" + Guid.NewGuid().ToString("N");
                SetString(view, "sceneId", id);
                var record = EntityRecord.FromData(view.CreateData(id, UnityGridSpace.Address(cell)));
                record.DisplayName = "Pile of boxes " + (data.Entities.Count + 1);
                record.Rotation = entity.transform.localEulerAngles;
                entity.name = record.DisplayName;
                data.Entities.Add(record);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                PrefabUtility.RecordPrefabInstancePropertyModifications(entity.transform);
            }
            EditorUtility.SetDirty(data);
            SetUpCameraAndInput(provider);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            RenderSettings.ambientLight = new Color(.55f, .6f, .68f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.sun = sun;
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(item => item.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"EntityTestScene: {provider.AllCells.Count} generated cells, {data.Entities.Count} furniture entities, player joins enabled.");
        }

        private static PlayerEntity BuildPlayer(Material modelMaterial, Material baseMaterial)
        {
            var saved = AssetDatabase.LoadAssetAtPath<PlayerEntity>(PrefabFolder + "/Player.prefab");
            if (saved != null) return saved;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtFolder + "/PSX_Char_Male_Base.fbx");
            if (model == null) throw new InvalidOperationException("Import PSX_Char_Male_Base.fbx into Assets/Art/Entities first.");
            var root = new GameObject("Player");
            try
            {
                var figure = (GameObject)PrefabUtility.InstantiatePrefab(model);
                figure.name = "Model";
                figure.transform.SetParent(root.transform, false);
                var bounds = BoundsOf(figure);
                if (bounds.size.y <= .001f) throw new InvalidOperationException("Player model has no visible height.");
                figure.transform.localScale *= 1.7f / bounds.size.y;
                bounds = BoundsOf(figure);
                figure.transform.position += new Vector3(-bounds.center.x, .12f - bounds.min.y, -bounds.center.z);
                foreach (var renderer in figure.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => modelMaterial).ToArray();
                var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stand.name = "Circular Base";
                stand.transform.SetParent(root.transform, false);
                stand.transform.localPosition = new Vector3(0, .06f, 0);
                stand.transform.localScale = new Vector3(.85f, .06f, .85f);
                stand.GetComponent<Renderer>().sharedMaterial = baseMaterial;
                var collider = root.AddComponent<CapsuleCollider>();
                collider.center = new Vector3(0, .96f, 0);
                collider.height = 1.92f;
                collider.radius = .35f;
                var entity = root.AddComponent<PlayerEntity>();
                SetString(entity, "definitionId", "entity.player");
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/Player.prefab").GetComponent<PlayerEntity>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static FurnitureEntity BuildFurniture()
        {
            var saved = AssetDatabase.LoadAssetAtPath<FurnitureEntity>(PrefabFolder + "/Furniture.prefab");
            if (saved != null) return saved;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/World/PileOfBoxes.prefab");
            var root = new GameObject("Furniture");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.transform.SetParent(root.transform, false);
                // Keep the original prefab as artwork. Only its old behavioral wrapper is removed locally.
                foreach (var view in model.GetComponentsInChildren<WorldObjectView>(true)) Object.DestroyImmediate(view);
                foreach (var drag in model.GetComponentsInChildren<GridObjectDrag>(true)) Object.DestroyImmediate(drag);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * .65f;
                var bounds = BoundsOf(model);
                model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                var entity = root.AddComponent<FurnitureEntity>();
                SetString(entity, "definitionId", "entity.furniture.boxes");
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/Furniture.prefab").GetComponent<FurnitureEntity>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void SetUpCameraAndInput(GridProvider grid)
        {
            var root = new GameObject("Managers");
            var holder = root.AddComponent<ManagersHolder>();
            var input = root.AddComponent<InputManager>();
            Set(input, "profile", AssetDatabase.LoadAssetAtPath<GameInputProfile>("Assets/Data/Input/MainInputProfile.asset"));
            var cameraObject = new GameObject("Camera Controller");
            cameraObject.transform.SetParent(root.transform, false);
            var manager = cameraObject.AddComponent<CameraManager>();
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(PhysicsRaycaster)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.SetParent(cameraObject.transform, false);
            camera.orthographic = true;
            camera.orthographicSize = 10.5f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.backgroundColor = new Color(.07f, .09f, .12f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            manager.DefaultCamera.Distance = 28;
            manager.DefaultCamera.GridFrame = grid.transform;
            manager.DefaultCamera.FocusOffset = Vector3.zero;
            manager.Configure(camera, manager.DefaultCamera);
            manager.Tick(0);
            holder._managers = new Manager[] { input, grid, manager };
            AddOrbitListener(root, "Camera.RotateLeft", manager, -1);
            AddOrbitListener(root, "Camera.RotateRight", manager, 1);
        }

        private static void AddOrbitListener(GameObject root, string id, CameraManager manager, float direction)
        {
            var listener = root.AddComponent<GameActionListener>();
            var serialized = new SerializedObject(listener);
            serialized.FindProperty("actionId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // Match the existing profile's hold action through its serialized UnityEvent.
            var eventProperty = serialized.FindProperty("onPerformed");
            if (eventProperty != null)
            {
                var field = typeof(GameActionListener).GetField("onPerformed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field?.GetValue(listener) is UnityEngine.Events.UnityEvent callback)
                    UnityEventTools.AddFloatPersistentListener(callback, manager.Orbit, direction);
            }
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException(go.name + " contains no renderers.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        private static Material Material(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static int EnsureDataLayer()
        {
            int layer = LayerMask.NameToLayer("Data");
            if (layer >= 0) return layer;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
                layers.GetArrayElementAtIndex(i).stringValue = "Data";
                tags.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            throw new InvalidOperationException("No free Unity layer for Data.");
        }
        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ServerCore.World.Entities.Editor
{
    public static class EntityAuthoringMenus
    {
        [MenuItem("GameObject/Entity/Player", false, 10)]
        private static void CreatePlayer(MenuCommand command) => Create(command, EntityKind.Player);

        [MenuItem("GameObject/Entity/Furniture", false, 11)]
        private static void CreateFurniture(MenuCommand command) => Create(command, EntityKind.Furniture);

        [MenuItem("CONTEXT/WorldDataObject/Create Player")]
        private static void ContextPlayer(MenuCommand command) => Create(command, EntityKind.Player);

        [MenuItem("CONTEXT/WorldDataObject/Create Furniture")]
        private static void ContextFurniture(MenuCommand command) => Create(command, EntityKind.Furniture);

        public static void Create(WorldDataObject data, EntityKind kind)
        {
            try
            {
                if (data == null) throw new InvalidOperationException("Add a WorldDataObject to the scene and assign Grid Provider, Entities Root and Prefabs, then select it.");
                string path = kind == EntityKind.Player ? "Assets/Prefabs/Entities/Player.prefab" : "Assets/Prefabs/Entities/Furniture.prefab";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var prefab = asset != null ? asset.GetComponent<BaseEntity>() : null;
                if (prefab == null) throw new InvalidOperationException($"Entity prefab missing: {path}. Build the Entities scene assets first.");
                Vector3 position = Selection.activeTransform != null ? Selection.activeTransform.position :
                    SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                EntityAuthoringSync.CreateEntity(data, prefab, position);
            }
            catch (Exception exception) { Debug.LogError("Create entity: " + exception.Message, data); }
        }

        private static void Create(MenuCommand command, EntityKind kind)
        {
            var data = command.context as WorldDataObject;
            if (data == null && command.context is GameObject context)
                data = context.GetComponentInParent<WorldDataObject>();
            if (data == null && Selection.activeGameObject != null)
                data = Selection.activeGameObject.GetComponentInParent<WorldDataObject>();
            if (data == null)
                data = Resources.FindObjectsOfTypeAll<WorldDataObject>().FirstOrDefault(d =>
                    !EditorUtility.IsPersistent(d) && d.gameObject.scene == SceneManager.GetActiveScene());
            Create(data, kind);
        }
    }
}

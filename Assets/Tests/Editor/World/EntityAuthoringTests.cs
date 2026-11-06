using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ServerCore.World.Data;
using ServerCore.World.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ServerCore.World.Tests
{
    public sealed class EntityAuthoringTests
    {
        private GameObject root;
        private GameObject template;
        private WorldDataObject data;
        private BaseEntity prefab;
        private string temporaryFolder;
        private Scene persistenceScene;
        private static Type SyncType => Type.GetType("ServerCore.World.Entities.Editor.EntityAuthoringSync, Assembly-CSharp-Editor", true);

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Authoring test");
            data = root.AddComponent<WorldDataObject>();
            var grid = new GameObject("Grid").AddComponent<GridProvider>();
            grid.transform.SetParent(root.transform);
            Set(grid, "cellsContainer", grid.transform);
            for (int x = 0; x < 2; x++)
            {
                var cell = new GameObject("Cell").AddComponent<GridCell>();
                cell.transform.SetParent(grid.transform);
                cell.transform.position = new Vector3(x, 0, 0);
                cell.Initialize(new Vector3Int(x, 0, 0), 0, Quaternion.identity, true);
                grid.RegisterCell(cell);
            }
            template = new GameObject("Player template");
            prefab = template.AddComponent<PlayerEntity>();
            Set(prefab, "definitionId", "test.player");
            Set(data, "_gridProvider", grid);
            Set(data, "_entitiesRoot", root.transform);
            var serialized = new SerializedObject(data);
            var prefabs = serialized.FindProperty("_prefabs");
            prefabs.arraySize = 1;
            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (template != null) Object.DestroyImmediate(template);
            if (persistenceScene.IsValid() && persistenceScene.isLoaded)
            {
                if (EditorSceneManager.IsPreviewScene(persistenceScene)) EditorSceneManager.ClosePreviewScene(persistenceScene);
                else EditorSceneManager.CloseScene(persistenceScene, true);
            }
            if (temporaryFolder != null) AssetDatabase.DeleteAsset(temporaryFolder);
        }

        [Test]
        public void RecordRoundTripPreservesIdentityOwnerPoseAndParameters()
        {
            var record = Record();
            record.OwnerId = Guid.NewGuid().ToString();
            record.Rotation = new Vector3(20, 30, 40);
            record.Scale = new Vector3(2, 3, 4);
            record.Parameters.Add(new EntityParameter { Key = "health", Value = "42" });
            var restored = EntityRecord.FromData(record.CreateData());
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(JsonUtility.ToJson(record)));
        }

        [Test]
        public void InvalidLaterRecordDoesNotPartiallySeedWorld()
        {
            data.Entities.Add(Record());
            var invalid = Record();
            invalid.Id = "second";
            invalid.OwnerId = "invalid-owner";
            data.Entities.Add(invalid);
            var world = new WorldState();
            Assert.Throws<InvalidOperationException>(() => data.SeedInto(world));
            Assert.That(world.Objects, Is.Empty);
        }

        [Test]
        public void DataAndSceneEditsSynchronizeAndBindRealData()
        {
            data.Entities.Add(Record());
            Sync();
            var entity = root.GetComponentInChildren<PlayerEntity>();
            Assert.That(entity.Data.Id.Value, Is.EqualTo("first"));
            data.Entities[0].Cell = new Vector3Int(1, 0, 0);
            data.Entities[0].DisplayName = "Changed in data";
            data.Entities[0].Rotation = new Vector3(10, 25, 5);
            data.Entities[0].Scale = new Vector3(2, 3, 4);
            Sync();
            Assert.That(entity.AnchorWorld.x, Is.EqualTo(1).Within(.001f));
            Assert.That(entity.name, Is.EqualTo("Changed in data"));
            Assert.That(Quaternion.Angle(entity.transform.localRotation, Quaternion.Euler(10, 25, 5)), Is.LessThan(.001f));
            Assert.That(entity.transform.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
            entity.name = "Changed in scene";
            entity.transform.position = Vector3.zero;
            entity.transform.localScale = Vector3.one * 2;
            Sync();
            Assert.That(data.Entities[0].DisplayName, Is.EqualTo(entity.name));
            Assert.That(data.Entities[0].Cell, Is.EqualTo(Vector3Int.zero));
            Assert.That(data.Entities[0].Scale, Is.EqualTo(Vector3.one * 2));
        }

        [Test]
        public void DuplicationGetsNewIdAndDeletionsWorkInBothDirections()
        {
            data.Entities.Add(Record());
            data.Entities[0].OwnerId = Guid.NewGuid().ToString();
            Sync();
            var entity = root.GetComponentInChildren<PlayerEntity>();
            var duplicate = Object.Instantiate(entity, root.transform);
            Sync();
            Assert.That(data.Entities.Count, Is.EqualTo(2));
            Assert.That(duplicate.SceneId, Is.Not.EqualTo(entity.SceneId));
            Assert.That(data.Entities.Single(r => r.Id == duplicate.SceneId).OwnerId, Is.Empty);
            Object.DestroyImmediate(duplicate.gameObject);
            Sync();
            Assert.That(data.Entities.Count, Is.EqualTo(1));
            data.Entities.Clear();
            Sync();
            Assert.That(root.GetComponentsInChildren<PlayerEntity>(), Is.Empty);
        }

        [Test]
        public void PersistedRecordsReuseMatchingInstanceOnFirstSynchronization()
        {
            data.Entities.Add(Record());
            var entity = Object.Instantiate(prefab, root.transform);
            Set(entity, "sceneId", "first");
            Sync();
            Assert.That(root.GetComponentsInChildren<PlayerEntity>().Length, Is.EqualTo(1));
            Assert.That(entity.Data, Is.Not.Null);
            var copy = JsonUtility.FromJson<EntityRecord>(JsonUtility.ToJson(data.Entities[0]));
            Assert.That(copy.CreateData().Id.Value, Is.EqualTo(entity.SceneId));
        }

        [Test]
        public void SceneSaveAndReopenPreservesRecordAndInstanceIdentity()
        {
            string folderName = "__EntityAuthoring_" + Guid.NewGuid().ToString("N");
            temporaryFolder = "Assets/Tests/Editor/World/" + folderName;
            AssetDatabase.CreateFolder("Assets/Tests/Editor/World", folderName);
            string path = temporaryFolder + "/Persistence.unity";
            var originalActiveScene = SceneManager.GetActiveScene();
            // A second untitled scene is forbidden. The Editor's asset factory creates a normal
            // empty scene file without saving, unloading or replacing the user's existing scene.
            var createAsset = typeof(EditorSceneManager).GetMethod("CreateSceneAsset",
                BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null);
            Assert.That(createAsset, Is.Not.Null, "Unity's empty scene asset factory is required for isolated persistence testing.");
            Assert.That(createAsset.Invoke(null, new object[] { path, false }), Is.EqualTo(true));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            try
            {
                persistenceScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Assert.That(EditorSceneManager.IsPreviewScene(persistenceScene), Is.False);
                SceneManager.SetActiveScene(originalActiveScene);
                SceneManager.MoveGameObjectToScene(root, persistenceScene);
                SceneManager.MoveGameObjectToScene(template, persistenceScene);
                data.Entities.Add(Record());
                data.Entities[0].Parameters.Add(new EntityParameter { Key = "persistent", Value = "yes" });
                Sync();
                Assert.That(root.GetComponentInChildren<PlayerEntity>().Data, Is.Not.Null);
                Assert.That(EditorSceneManager.SaveScene(persistenceScene, path), Is.True);
                EditorSceneManager.CloseScene(persistenceScene, true);
                root = null;
                template = null;
                persistenceScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Assert.That(EditorSceneManager.IsPreviewScene(persistenceScene), Is.False);
                SceneManager.SetActiveScene(originalActiveScene);
                root = persistenceScene.GetRootGameObjects().Single(o => o.GetComponent<WorldDataObject>() != null);
                template = persistenceScene.GetRootGameObjects().Single(o => o.name == "Player template");
                data = root.GetComponent<WorldDataObject>();
                Sync();
                Assert.That(data.Entities.Single().Id, Is.EqualTo("first"));
                Assert.That(data.Entities.Single().Parameters.Single().Value, Is.EqualTo("yes"));
                var entity = root.GetComponentsInChildren<PlayerEntity>().Single();
                Assert.That(entity.SceneId, Is.EqualTo("first"));
                Assert.That(entity.Data, Is.Not.Null, "Reopened normal scenes must execute authoring synchronization.");
                data.Entities[0].DisplayName = "Updated after reopen";
                data.Entities[0].Cell = Vector3Int.right;
                Sync();
                Assert.That(entity.name, Is.EqualTo("Updated after reopen"));
                Assert.That(entity.AnchorWorld.x, Is.EqualTo(1).Within(.001f));
                Assert.That(entity.Data.Cell.Value.X, Is.EqualTo(1));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(originalActiveScene));
            }
            finally
            {
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                    SceneManager.SetActiveScene(originalActiveScene);
            }
        }

        [Test]
        public void CreationUndoAndRedoRestoreDataAndViewTogether()
        {
            Undo.IncrementCurrentGroup();
            var created = (BaseEntity)SyncType.GetMethod("CreateEntity", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { data, prefab, Vector3.zero });
            string id = created.SceneId;
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Sync();
            Assert.That(data.Entities, Is.Empty);
            Assert.That(root.GetComponentsInChildren<PlayerEntity>(), Is.Empty);
            Undo.PerformRedo();
            Sync();
            Assert.That(data.Entities.Single().Id, Is.EqualTo(id));
            Assert.That(root.GetComponentsInChildren<PlayerEntity>().Single().SceneId, Is.EqualTo(id));
        }

        [Test]
        public void DuplicatePlayerOwnerIsRejectedBeforeWorldMutation()
        {
            var first = Record();
            first.OwnerId = Guid.NewGuid().ToString();
            var second = Record();
            second.Id = "second";
            second.OwnerId = first.OwnerId.ToUpperInvariant();
            data.Entities.Add(first);
            data.Entities.Add(second);
            var world = new WorldState();
            Assert.Throws<InvalidOperationException>(() => data.SeedInto(world));
            Assert.That(world.Objects, Is.Empty);
        }

        [Test]
        public void SceneTransformUndoAndRedoRestoreRecordPoseTogether()
        {
            data.Entities.Add(Record());
            Sync();
            var entity = root.GetComponentInChildren<PlayerEntity>();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(entity.transform, "Edit entity pose");
            entity.transform.position = Vector3.right;
            entity.transform.localRotation = Quaternion.Euler(0, 50, 0);
            entity.transform.localScale = new Vector3(2, 3, 4);
            Undo.FlushUndoRecordObjects();
            Sync();
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Sync();
            Assert.That(data.Entities[0].Cell, Is.EqualTo(Vector3Int.zero));
            Assert.That(entity.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(Quaternion.Angle(entity.transform.localRotation, Quaternion.identity), Is.LessThan(.001f));
            Undo.PerformRedo();
            Sync();
            Assert.That(data.Entities[0].Cell, Is.EqualTo(Vector3Int.right));
            Assert.That(data.Entities[0].Scale, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(entity.transform.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(Quaternion.Angle(entity.transform.localRotation, Quaternion.Euler(0, 50, 0)), Is.LessThan(.001f));
        }

        [Test]
        public void LiveEditingMutatesExistingSyncFieldsWithoutReplacingWorldOrPlanning()
        {
            data.Entities.Add(Record());
            var world = new WorldState();
            data.SeedInto(world);
            data.Bind(world);
            var original = world.Find("first");
            var planning = world.Planning.Value;
            var draft = EntityRecord.FromData(original);
            draft.DisplayName = "Live name";
            draft.OwnerId = Guid.NewGuid().ToString();
            draft.Cell = Vector3Int.right;
            draft.Rotation = new Vector3(0, 40, 0);
            draft.Scale = Vector3.one * 2;
            draft.Parameters.Add(new EntityParameter { Key = "live", Value = "true" });
            var method = Type.GetType("ServerCore.World.Entities.Editor.WorldRuntimeDataEditing, Assembly-CSharp-Editor", true)
                .GetMethod("Apply", new[] { typeof(WorldDataObject), typeof(EntityRecord) });
            method.Invoke(null, new object[] { data, draft });
            Assert.That(data.RuntimeState, Is.SameAs(world));
            Assert.That(world.Planning.Value, Is.SameAs(planning));
            Assert.That(world.Find("first"), Is.SameAs(original));
            Assert.That(original.DisplayName.Value, Is.EqualTo("Live name"));
            Assert.That(original.Transform.Value.RotationY, Is.EqualTo(40));
            Assert.That(original.Transform.Value.ScaleX, Is.EqualTo(2));
            Assert.That(original.Parameters.Value["live"], Is.EqualTo("true"));
            draft.Cell = new Vector3Int(999, 0, 0);
            draft.DisplayName = "Must not apply";
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { data, draft }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(original.DisplayName.Value, Is.EqualTo("Live name"));
        }

        private static EntityRecord Record() => new EntityRecord
        {
            Id = "first", DefinitionId = "test.player", Kind = EntityKind.Player,
            DisplayName = "Test player", Cell = Vector3Int.zero
        };

        private void Sync() => SyncType.GetMethod("Synchronize", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { data });

        private static void Set(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (value is Object obj) property.objectReferenceValue = obj;
            else property.stringValue = (string)value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}








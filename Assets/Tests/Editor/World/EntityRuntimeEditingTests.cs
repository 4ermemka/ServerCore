using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ServerCore.World.Data;
using ServerCore.World.Entities;
using Shared.WorldObjects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ServerCore.World.Tests
{
    public sealed class EntityRuntimeEditingTests
    {
        private GameObject root;
        private GameObject template;
        private WorldDataObject data;
        private PlayerEntity view;
        private PlayerData model;
        private static Type EditorType(string name) => Type.GetType("ServerCore.World.Entities.Editor." + name + ", Assembly-CSharp-Editor", true);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            root = new GameObject("Live authoring test");
            data = root.AddComponent<WorldDataObject>();
            var grid = new GameObject("Grid").AddComponent<GridProvider>();
            grid.transform.SetParent(root.transform);
            Set(grid, "cellsContainer", grid.transform);
            for (int x = 0; x < 2; x++)
            {
                var cell = new GameObject("Cell").AddComponent<GridCell>();
                cell.transform.SetParent(grid.transform);
                cell.transform.position = Vector3.right * x;
                cell.Initialize(new Vector3Int(x, 0, 0), 0, Quaternion.identity, true);
                grid.RegisterCell(cell);
            }
            template = new GameObject("Player template");
            var prefab = template.AddComponent<PlayerEntity>();
            Set(prefab, "definitionId", "test.player");
            Set(data, "_gridProvider", grid);
            Set(data, "_entitiesRoot", root.transform);
            var serialized = new SerializedObject(data);
            var prefabs = serialized.FindProperty("_prefabs");
            prefabs.arraySize = 1;
            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var state = new WorldState();
            model = new PlayerData();
            model.Id.Value = "live";
            model.DefinitionId.Value = "test.player";
            model.DisplayName.Value = "Live player";
            state.Players.Add(model);
            data.Bind(state);
            view = Object.Instantiate(prefab, root.transform);
            Set(view, "sceneId", model.Id.Value);
            view.name = model.DisplayName.Value;
            view.Bind(model, new UnityGridSpace(grid));
            Undo.ClearAll();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            if (template != null) Object.Destroy(template);
            Undo.ClearAll();
            yield return new ExitPlayMode();
        }

        [Test]
        public void InspectorTransformAndNameEditsUpdateLiveModel()
        {
            Undo.RecordObject(view.transform, "Inspector transform edit");
            view.transform.position = Vector3.right;
            view.transform.localRotation = Quaternion.Euler(0, 45, 0);
            view.transform.localScale = new Vector3(2, 3, 4);
            Undo.FlushUndoRecordObjects();
            Assert.That(model.Cell.Value, Is.EqualTo(new GridCoordinate(1, 0, 0)));
            Assert.That(model.Transform.Value.RotationY, Is.EqualTo(45).Within(.001f));
            Assert.That(model.Transform.Value.ScaleY, Is.EqualTo(3));
            Undo.RecordObject(view.gameObject, "Inspector name edit");
            view.name = "Renamed live";
            Undo.FlushUndoRecordObjects();
            Assert.That(model.DisplayName.Value, Is.EqualTo("Renamed live"));
        }

        [UnityTest]
        public IEnumerator ProgrammaticPreviewsAndNetworkChangesDoNotWriteBack()
        {
            view.transform.position = Vector3.right;
            view.transform.localRotation = Quaternion.Euler(0, 60, 0);
            yield return null;
            Assert.That(model.Cell.Value, Is.EqualTo(new GridCoordinate(0, 0, 0)));
            Assert.That(model.Transform.Value.RotationY, Is.Zero);
            model.Cell.SetValueSilent(new GridCoordinate(1, 0, 0));
            model.Transform.SetValueSilent(new ObjectTransformData { RotationY = 25, ScaleX = 2 });
            yield return null;
            Assert.That(model.Cell.Value, Is.EqualTo(new GridCoordinate(1, 0, 0)));
            Assert.That(model.Transform.Value.RotationY, Is.EqualTo(25));
            Assert.That(model.Transform.Value.ScaleX, Is.EqualTo(2));
        }

        [Test]
        public void InvalidInspectorScaleRestoresAuthoritativePose()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Entity runtime edit rejected"));
            Undo.RecordObject(view.transform, "Invalid inspector scale");
            view.transform.localScale = Vector3.zero;
            Undo.FlushUndoRecordObjects();
            Assert.That(view.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(model.Transform.Value.ScaleX, Is.EqualTo(1));
        }

        [Test]
        public void StaleDraftCannotOverwriteNewerLiveMovement()
        {
            var draft = EntityRecord.FromData(model);
            string original = JsonUtility.ToJson(draft);
            draft.DisplayName = "Draft name";
            model.Cell.Value = new GridCoordinate(1, 0, 0);
            var apply = EditorType("WorldRuntimeDataEditing").GetMethod("Apply", new[] { typeof(WorldDataObject), typeof(EntityRecord), typeof(string) });
            Assert.That(apply, Is.Not.Null, "Live draft edits require optimistic source validation.");
            var failure = Assert.Throws<TargetInvocationException>(() => apply.Invoke(null, new object[] { data, draft, original }));
            Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(model.Cell.Value, Is.EqualTo(new GridCoordinate(1, 0, 0)));
            Assert.That(model.DisplayName.Value, Is.EqualTo("Live player"));
        }

        [Test]
        public void DirtyDraftSurvivesExternalChangesAndRequiresExplicitReset()
        {
            var type = EditorType("WorldRuntimeDataDraft");
            var draft = Activator.CreateInstance(type, model);
            var record = (EntityRecord)type.GetProperty("Record").GetValue(draft);
            record.DisplayName = "Unsaved draft";
            model.Cell.Value = new GridCoordinate(1, 0, 0);
            type.GetMethod("Refresh").Invoke(draft, new object[] { model });
            Assert.That(type.GetProperty("Record").GetValue(draft), Is.SameAs(record));
            Assert.That(record.DisplayName, Is.EqualTo("Unsaved draft"));
            Assert.That(type.GetProperty("HasConflict").GetValue(draft), Is.EqualTo(true));
            type.GetMethod("Reset").Invoke(draft, new object[] { model });
            var reset = (EntityRecord)type.GetProperty("Record").GetValue(draft);
            Assert.That(reset.Cell, Is.EqualTo(Vector3Int.right));
            Assert.That(reset.DisplayName, Is.EqualTo("Live player"));
            Assert.That(type.GetProperty("HasConflict").GetValue(draft), Is.EqualTo(false));
        }

        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

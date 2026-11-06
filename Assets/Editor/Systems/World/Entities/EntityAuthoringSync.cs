using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ServerCore.World.Entities.Editor
{
    /// <summary>Reconciles only changed records/views; persistent records take precedence on scene load.</summary>
    [InitializeOnLoad]
    public static class EntityAuthoringSync
    {
        private sealed class Baseline
        {
            public readonly Dictionary<string, string> Records = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Views = new(StringComparer.Ordinal);
            public readonly Dictionary<string, EntityId> Instances = new(StringComparer.Ordinal);
            public string Context;
        }

        private static readonly Dictionary<EntityId, Baseline> Baselines = new();
        private static readonly Dictionary<EntityId, string> Errors = new();
        private static bool synchronizing;
        private static double nextPoll;
        private static bool undoPending;

        static EntityAuthoringSync()
        {
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += _ => { Baselines.Clear(); Errors.Clear(); };
            EditorSceneManager.sceneClosed += _ => { Baselines.Clear(); Errors.Clear(); };
            EditorSceneManager.sceneSaving += (scene, _) =>
            {
                foreach (var data in FindData())
                    if (data.gameObject.scene == scene) TrySynchronize(data);
            };
        }

        public static string GetValidationError(WorldDataObject data)
            => data != null && Errors.TryGetValue(data.GetEntityId(), out var error) ? error : null;

        private static IEnumerable<WorldDataObject> FindData()
            => Resources.FindObjectsOfTypeAll<WorldDataObject>().Where(CanSynchronize);

        private static bool CanSynchronize(WorldDataObject data)
            => data != null && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating && !EditorUtility.IsPersistent(data) &&
                data.gameObject.scene.IsValid() && data.gameObject.scene.isLoaded &&
                PrefabStageUtility.GetPrefabStage(data.gameObject) == null;

        private static void OnUndoRedo()
        {
            // All induced changes share the originating Undo group. Rebase restored objects together
            // instead of interpreting restored/deleted instances as a new user edit.
            Baselines.Clear();
            undoPending = true;
        }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + .2;
            if (undoPending) { undoPending = false; return; }
            foreach (var data in FindData()) TrySynchronize(data);
        }

        private static void TrySynchronize(WorldDataObject data)
        {
            try { Synchronize(data); Errors.Remove(data.GetEntityId()); }
            catch (Exception exception)
            {
                EntityId id = data.GetEntityId();
                if (!Errors.TryGetValue(id, out var old) || old != exception.Message)
                    Debug.LogError("Entity authoring: " + exception.Message, data);
                Errors[id] = exception.Message;
            }
        }

        public static void Synchronize(WorldDataObject data)
        {
            if (synchronizing || !CanSynchronize(data)) return;
            synchronizing = true;
            try { Reconcile(data); }
            finally { synchronizing = false; }
        }

        private static void Reconcile(WorldDataObject data)
        {
            data.ValidateRecords(data.Entities);
            EntityId key = data.GetEntityId();
            Baselines.TryGetValue(key, out var before);
            var views = data.EntitiesRoot.GetComponentsInChildren<BaseEntity>(true)
                .Where(v => v.gameObject.scene == data.gameObject.scene &&
                    v.GetComponentsInParent<BaseEntity>(true).Length == 1 &&
                    (v.GetComponentInParent<WorldDataObject>(true) == null ||
                     v.GetComponentInParent<WorldDataObject>(true) == data))
                .ToList();
            string context = ContextSignature(data);
            if (before != null && before.Context == context && data.Entities.Count == before.Records.Count &&
                views.Count == before.Views.Count &&
                data.Entities.All(r => before.Records.TryGetValue(r.Id, out var old) && old == Signature(r)) &&
                views.All(v => before.Views.TryGetValue(v.SceneId ?? "", out var old) && old == ViewSignature(v))) return;

            var proposed = data.Entities.Select(Clone).ToList();
            var original = data.Entities.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var byId = new Dictionary<string, BaseEntity>(StringComparer.Ordinal);
            var remove = new List<BaseEntity>();
            // Keep the original instance when a duplicate inherits its SceneId.
            if (before != null)
                views.Sort((a, b) => IsOriginal(b, before).CompareTo(IsOriginal(a, before)));
            foreach (var view in views)
            {
                string id = view.SceneId;
                bool duplicate = !string.IsNullOrWhiteSpace(id) && byId.ContainsKey(id);
                if (string.IsNullOrWhiteSpace(id) || duplicate)
                {
                    string sourceId = id;
                    do { id = Guid.NewGuid().ToString("N"); }
                    while (original.ContainsKey(id) || byId.ContainsKey(id));
                    original.TryGetValue(sourceId ?? "", out var source);
                    var copy = Capture(data, view, id, source);
                    if (duplicate && copy.Kind == EntityKind.Player) copy.OwnerId = "";
                    proposed.Add(copy);
                }
                else
                {
                    var record = proposed.Find(r => r.Id == id);
                    if (record == null)
                    {
                        if (before != null && before.Records.ContainsKey(id)) { remove.Add(view); continue; }
                        proposed.Add(Capture(data, view, id, null));
                    }
                    else if (before != null && before.Records.TryGetValue(id, out var prior) &&
                             prior == Signature(record) && before.Views.TryGetValue(id, out var oldView) &&
                             oldView != ViewSignature(view))
                    {
                        proposed[proposed.IndexOf(record)] = Capture(data, view, id, record);
                    }
                }
                byId.Add(id, view);
            }
            if (before != null)
                foreach (var pair in before.Views)
                    if (!byId.ContainsKey(pair.Key))
                    {
                        var record = proposed.Find(r => r.Id == pair.Key);
                        if (record != null && before.Records.TryGetValue(pair.Key, out var old) && old == Signature(record))
                            proposed.Remove(record);
                    }

            // No Undo, ID writes, deletion or instantiation occurs until the entire proposal is valid.
            data.ValidateRecords(proposed);
            int group = Undo.GetCurrentGroup();
            bool recordsChanged = !SameRecords(data.Entities, proposed);
            if (recordsChanged)
            {
                Undo.RecordObject(data, "Synchronize entity data");
                data.Entities.Clear();
                data.Entities.AddRange(proposed);
                EditorUtility.SetDirty(data);
            }
            foreach (var view in remove) Undo.DestroyObjectImmediate(view.gameObject);
            var grid = new UnityGridSpace(data.GridProvider);
            foreach (var record in data.Entities)
            {
                var model = record.CreateData();
                if (byId.TryGetValue(record.Id, out var view) &&
                    (view.DefinitionId != record.DefinitionId || !view.Accepts(model)))
                {
                    Undo.DestroyObjectImmediate(view.gameObject);
                    view = null;
                }
                if (view == null) view = Instantiate(data, data.GetPrefab(record));
                SetSceneId(view, record.Id);
                if (view.name != record.DisplayName)
                {
                    Undo.RecordObject(view.gameObject, "Rename entity");
                    view.name = record.DisplayName;
                }
                Undo.RecordObject(view.transform, "Synchronize entity transform");
                view.Bind(model, grid);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.gameObject);
            }
            Undo.CollapseUndoOperations(group);
            var after = new Baseline { Context = ContextSignature(data) };
            foreach (var record in data.Entities) after.Records.Add(record.Id, Signature(record));
            foreach (var view in data.EntitiesRoot.GetComponentsInChildren<BaseEntity>(true))
                if (after.Records.ContainsKey(view.SceneId ?? "") && !after.Views.ContainsKey(view.SceneId))
                {
                    after.Views.Add(view.SceneId, ViewSignature(view));
                    after.Instances.Add(view.SceneId, view.GetEntityId());
                }
            Baselines[key] = after;
        }

        public static BaseEntity CreateEntity(WorldDataObject data, BaseEntity prefab, Vector3 position)
        {
            if (!CanSynchronize(data)) throw new InvalidOperationException("Select a WorldDataObject in a loaded scene outside Play/prefab mode.");
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (data.Prefabs == null || !data.Prefabs.Contains(prefab))
                throw new InvalidOperationException("Add this entity prefab to WorldDataObject.Prefabs before creating it.");
            Synchronize(data);
            var cell = data.GridProvider.GetClosestCell(position);
            if (cell == null) throw new InvalidOperationException("Grid Provider contains no valid cells; generate the scene grid first.");
            var record = new EntityRecord
            {
                Id = Guid.NewGuid().ToString("N"), DefinitionId = prefab.DefinitionId,
                Kind = KindOf(prefab), DisplayName = prefab.name, Cell = cell.GridPosition,
                Rotation = prefab.transform.localEulerAngles, Scale = prefab.transform.localScale
            };
            var proposed = new List<EntityRecord>(data.Entities) { record };
            data.ValidateRecords(proposed);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create entity");
            Undo.RecordObject(data, "Create entity data");
            data.Entities.Add(record);
            EditorUtility.SetDirty(data);
            Synchronize(data);
            Undo.CollapseUndoOperations(group);
            var result = data.EntitiesRoot.GetComponentsInChildren<BaseEntity>(true).First(e => e.SceneId == record.Id);
            Selection.activeGameObject = result.gameObject;
            return result;
        }

        private static EntityRecord Capture(WorldDataObject data, BaseEntity view, string id, EntityRecord previous)
        {
            var cell = data.GridProvider.GetClosestCell(view.AnchorWorld);
            if (cell == null) throw new InvalidOperationException($"{view.name}: no valid grid cell for entity anchor.");
            var record = previous == null ? new EntityRecord() : Clone(previous);
            record.Id = id;
            record.DefinitionId = view.DefinitionId;
            record.Kind = KindOf(view);
            record.DisplayName = view.name;
            record.Cell = cell.GridPosition;
            record.Rotation = view.transform.localEulerAngles;
            record.Scale = view.transform.localScale;
            return record;
        }

        private static EntityKind KindOf(BaseEntity view) => view is PlayerEntity ? EntityKind.Player :
            view is FurnitureEntity ? EntityKind.Furniture : throw new InvalidOperationException($"Unsupported entity type: {view.GetType().Name}.");
        private static bool IsOriginal(BaseEntity view, Baseline baseline) => view.SceneId != null &&
            baseline.Instances.TryGetValue(view.SceneId, out var id) && id.Equals(view.GetEntityId());
        private static EntityRecord Clone(EntityRecord record) => JsonUtility.FromJson<EntityRecord>(Signature(record));
        private static string Signature(EntityRecord record) => JsonUtility.ToJson(record);
        private static string ViewSignature(BaseEntity view) => string.Join("|", view.GetEntityId(), view.SceneId,
            view.DefinitionId, view.name, view.transform.localPosition.ToString("R"),
            view.transform.localRotation.ToString("R"), view.transform.localScale.ToString("R"), view.AnchorWorld.ToString("R"));
        private static string ContextSignature(WorldDataObject data) => EditorJsonUtility.ToJson(data);
        private static bool SameRecords(IReadOnlyList<EntityRecord> a, IReadOnlyList<EntityRecord> b)
            => a.Count == b.Count && !a.Where((r, i) => Signature(r) != Signature(b[i])).Any();

        private static BaseEntity Instantiate(WorldDataObject data, BaseEntity prefab)
        {
            var instance = PrefabUtility.IsPartOfPrefabAsset(prefab)
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, data.EntitiesRoot)
                : Object.Instantiate(prefab.gameObject, data.EntitiesRoot);
            Undo.RegisterCreatedObjectUndo(instance, "Create entity view");
            return instance.GetComponent<BaseEntity>();
        }

        private static void SetSceneId(BaseEntity view, string id)
        {
            if (view.SceneId == id) return;
            var serialized = new SerializedObject(view);
            serialized.FindProperty("sceneId").stringValue = id;
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
        }
    }
}



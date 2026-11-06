using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ServerCore.World.Entities.Editor
{
    [CustomEditor(typeof(WorldDataObject))]
    public sealed class WorldDataObjectEditor : UnityEditor.Editor
    {
        private sealed class Draft
        {
            public WorldRuntimeDataDraft State;
            public string Error;
        }

        private readonly Dictionary<string, Draft> drafts = new(StringComparer.Ordinal);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_gridProvider"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_entitiesRoot"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_prefabs"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_entities"), new GUIContent("Authored entities"), true);
                if (serializedObject.ApplyModifiedProperties()) Synchronize();
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create Player")) EntityAuthoringMenus.Create((WorldDataObject)target, EntityKind.Player);
                    if (GUILayout.Button("Create Furniture")) EntityAuthoringMenus.Create((WorldDataObject)target, EntityKind.Furniture);
                }
                if (GUILayout.Button("Synchronize scene and data")) Synchronize();
            }
            string error = EntityAuthoringSync.GetValidationError((WorldDataObject)target);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (Application.isPlaying) DrawRuntime((WorldDataObject)target);
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        private void DrawRuntime(WorldDataObject data)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live world entities", EditorStyles.boldLabel);
            if (data.RuntimeState == null)
            {
                EditorGUILayout.HelpBox("The scene has no bound runtime WorldState yet.", MessageType.Info);
                return;
            }
            bool editable = WorldRuntimeDataEditing.CanEdit;
            if (!editable) EditorGUILayout.HelpBox("Live values are read only. Editing requires the ready host.", MessageType.Info);
            var models = data.RuntimeState.Objects.ToArray();
            var ids = new HashSet<string>(models.Select(m => m.Id.Value), StringComparer.Ordinal);
            foreach (var stale in drafts.Keys.Where(id => !ids.Contains(id)).ToArray()) drafts.Remove(stale);
            if (models.Length == 0) EditorGUILayout.LabelField("No live entities.");
            foreach (var model in models)
            {
                var snapshot = EntityRecord.FromData(model);
                if (!drafts.TryGetValue(snapshot.Id, out var draft))
                    drafts[snapshot.Id] = draft = new Draft { State = new WorldRuntimeDataDraft(model) };
                draft.State.Refresh(model);
                var record = draft.State.Record;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(snapshot.DisplayName + " / " + snapshot.Kind, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Id", snapshot.Id);
                    EditorGUILayout.LabelField("Definition", snapshot.DefinitionId);
                    using (new EditorGUI.DisabledScope(!editable))
                    {
                        record.DisplayName = EditorGUILayout.TextField("Display name", record.DisplayName);
                        record.OwnerId = EditorGUILayout.TextField("Owner GUID", record.OwnerId);
                        record.Cell = EditorGUILayout.Vector3IntField("Cell", record.Cell);
                        record.Rotation = EditorGUILayout.Vector3Field("Local rotation", record.Rotation);
                        record.Scale = EditorGUILayout.Vector3Field("Local scale", record.Scale);
                        EditorGUILayout.LabelField("Parameters");
                        for (int i = 0; i < record.Parameters.Count; i++)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                record.Parameters[i].Key = EditorGUILayout.TextField(record.Parameters[i].Key);
                                record.Parameters[i].Value = EditorGUILayout.TextField(record.Parameters[i].Value);
                                if (GUILayout.Button("−", GUILayout.Width(24))) { record.Parameters.RemoveAt(i); break; }
                            }
                        }
                        if (GUILayout.Button("Add parameter")) record.Parameters.Add(new EntityParameter { Key = "", Value = "" });
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Apply live changes"))
                            {
                                try
                                {
                                    WorldRuntimeDataEditing.Apply(data, record, draft.State.SourceSignature);
                                    draft.State.Reset(model);
                                    draft.Error = null;
                                }
                                catch (Exception exception) { draft.Error = exception.Message; }
                            }
                            if (GUILayout.Button("Reset draft")) { draft.State.Reset(model); draft.Error = null; }
                        }
                    }
                    if (draft.State.HasConflict) EditorGUILayout.HelpBox("Live values changed while this draft was edited. Your draft is preserved. Reset the draft to load current values before applying.", MessageType.Warning);
                    if (!string.IsNullOrEmpty(draft.Error)) EditorGUILayout.HelpBox(draft.Error, MessageType.Error);
                }
            }
        }

        private void Synchronize()
        {
            try { EntityAuthoringSync.Synchronize((WorldDataObject)target); }
            catch (Exception exception) { Debug.LogError("Entity authoring: " + exception.Message, target); }
        }
    }
}


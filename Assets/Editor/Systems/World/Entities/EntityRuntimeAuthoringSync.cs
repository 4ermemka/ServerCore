using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ServerCore.World.Entities.Editor
{
    /// <summary>Only editor Undo property edits can write scene poses back into a live model.</summary>
    [InitializeOnLoad]
    public static class EntityRuntimeAuthoringSync
    {
        [Flags]
        private enum EditedFields { None = 0, Position = 1, Rotation = 2, Scale = 4, Name = 8 }
        private static bool processing;
        private static bool acceptingEdits;

        static EntityRuntimeAuthoringSync()
        {
            acceptingEdits = EditorApplication.isPlaying;
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.postprocessModifications += OnPostprocessModifications;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
            EditorApplication.quitting += Unregister;
        }

        private static void Unregister()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
            EditorApplication.quitting -= Unregister;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
            => acceptingEdits = state == PlayModeStateChange.EnteredPlayMode;

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (!Application.isPlaying || !acceptingEdits || processing || modifications == null) return modifications;
            var edited = new Dictionary<BaseEntity, EditedFields>();
            foreach (var modification in modifications)
            {
                var property = modification.currentValue;
                if (property == null || property.target == null) continue;
                BaseEntity entity = null;
                var fields = EditedFields.None;
                if (property.target is Transform transform)
                {
                    entity = transform.GetComponent<BaseEntity>();
                    if (property.propertyPath.StartsWith("m_LocalPosition", StringComparison.Ordinal)) fields = EditedFields.Position;
                    else if (property.propertyPath.StartsWith("m_LocalRotation", StringComparison.Ordinal)) fields = EditedFields.Rotation;
                    else if (property.propertyPath.StartsWith("m_LocalScale", StringComparison.Ordinal)) fields = EditedFields.Scale;
                }
                else if (property.target is GameObject gameObject && property.propertyPath == "m_Name")
                {
                    entity = gameObject.GetComponent<BaseEntity>();
                    fields = EditedFields.Name;
                }
                if (entity == null || fields == EditedFields.None || entity.Data == null ||
                    EditorUtility.IsPersistent(entity) || PrefabStageUtility.GetPrefabStage(entity.gameObject) != null ||
                    entity.GetComponentsInParent<BaseEntity>(true).Length != 1) continue;
                edited.TryGetValue(entity, out var previous);
                edited[entity] = previous | fields;
            }
            if (edited.Count == 0) return modifications;
            processing = true;
            try
            {
                foreach (var pair in edited)
                {
                    var entity = pair.Key;
                    var owner = FindOwner(entity);
                    if (owner == null) continue;
                    try
                    {
                        if (!WorldRuntimeDataEditing.CanEdit)
                            throw new InvalidOperationException("Only the ready host can edit live world data.");
                        var record = EntityRecord.FromData(entity.Data);
                        if ((pair.Value & EditedFields.Position) != 0)
                        {
                            var cell = owner.GridProvider.GetClosestCell(entity.AnchorWorld);
                            if (cell == null) throw new InvalidOperationException("The entity anchor has no valid grid cell.");
                            record.Cell = cell.GridPosition;
                        }
                        if ((pair.Value & EditedFields.Rotation) != 0) record.Rotation = entity.transform.localEulerAngles;
                        if ((pair.Value & EditedFields.Scale) != 0) record.Scale = entity.transform.localScale;
                        if ((pair.Value & EditedFields.Name) != 0) record.DisplayName = entity.name;
                        WorldRuntimeDataEditing.Apply(owner, record);
                        // Applying an unchanged cell still needs to snap an edited position to its center.
                        if ((pair.Value & EditedFields.Position) != 0) entity.RestorePosition();
                    }
                    catch (Exception exception)
                    {
                        entity.RestorePosition();
                        entity.name = entity.Data.DisplayName.Value;
                        Debug.LogWarning("Entity runtime edit rejected: " + exception.Message, entity);
                    }
                }
            }
            finally { processing = false; }
            return modifications;
        }

        private static WorldDataObject FindOwner(BaseEntity entity)
            => Resources.FindObjectsOfTypeAll<WorldDataObject>().FirstOrDefault(data =>
                !EditorUtility.IsPersistent(data) && data.gameObject.scene.IsValid() && data.gameObject.scene.isLoaded &&
                data.gameObject.scene == entity.gameObject.scene && data.RuntimeState != null &&
                ReferenceEquals(data.RuntimeState.Find(entity.Data.Id.Value), entity.Data) &&
                (data.EntitiesRoot == entity.transform || entity.transform.IsChildOf(data.EntitiesRoot)));
    }
}

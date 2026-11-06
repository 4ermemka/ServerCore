using System;
using System.Linq;
using ServerCore.World.Data;
using UnityEngine;

namespace ServerCore.World.Entities.Editor
{
    /// <summary>Editor-only live data editing through existing Sync fields and authoritative permissions.</summary>
    public static class WorldRuntimeDataEditing
    {
        public static bool CanEdit
        {
            get
            {
                if (!Application.isPlaying) return true;
                var session = UnityEngine.Object.FindAnyObjectByType<GameSessionController>();
                return session == null || session.IsHost && session.IsReady;
            }
        }

        public static void Apply(WorldDataObject data, EntityRecord record) => Apply(data, record, null);

        public static void Apply(WorldDataObject data, EntityRecord record, string expectedSourceSignature)
        {
            if (!CanEdit) throw new InvalidOperationException("Only the ready host can edit live world data.");
            if (data == null || data.RuntimeState == null) throw new InvalidOperationException("WorldDataObject has no bound runtime state.");
            if (record == null) throw new ArgumentNullException(nameof(record));
            var target = data.RuntimeState.Find(record.Id) ?? throw new InvalidOperationException("The runtime entity no longer exists.");
            var original = EntityRecord.FromData(target);
            if (expectedSourceSignature != null && expectedSourceSignature != JsonUtility.ToJson(original))
                throw new InvalidOperationException("Live entity changed while this draft was being edited. Reset the draft before applying.");
            if (record.DefinitionId != original.DefinitionId || record.Kind != original.Kind)
                throw new InvalidOperationException("Live editing cannot change entity identity, definition or kind.");
            var proposed = data.RuntimeState.Objects.Select(EntityRecord.FromData).ToList();
            proposed[proposed.FindIndex(r => r.Id == record.Id)] = record;
            data.ValidateRecords(proposed);
            var source = record.CreateData();
            target.OwnerId.Value = source.OwnerId.Value;
            target.DisplayName.Value = source.DisplayName.Value;
            target.Cell.Value = source.Cell.Value;
            if (original.Rotation != record.Rotation || original.Scale != record.Scale)
                target.Transform.Value = source.Transform.Value;
            var oldParameters = target.Parameters.Value;
            var newParameters = source.Parameters.Value;
            if (oldParameters == null || oldParameters.Count != newParameters.Count ||
                oldParameters.Any(p => !newParameters.TryGetValue(p.Key, out var value) || value != p.Value))
                target.Parameters.Value = newParameters;
        }
    }
}


using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World.Entities.Editor
{
    /// <summary>Preserves an edited live draft until the user explicitly resolves external changes.</summary>
    public sealed class WorldRuntimeDataDraft
    {
        public EntityRecord Record { get; private set; }
        public string SourceSignature { get; private set; }
        public bool HasConflict { get; private set; }
        public bool IsDirty => JsonUtility.ToJson(Record) != SourceSignature;

        public WorldRuntimeDataDraft(ObjectData source) => Reset(source);

        public void Refresh(ObjectData source)
        {
            string current = JsonUtility.ToJson(EntityRecord.FromData(source));
            if (current == SourceSignature) { HasConflict = false; return; }
            if (IsDirty) { HasConflict = true; return; }
            Reset(source);
        }

        public void Reset(ObjectData source)
        {
            Record = EntityRecord.FromData(source);
            SourceSignature = JsonUtility.ToJson(Record);
            HasConflict = false;
        }
    }
}

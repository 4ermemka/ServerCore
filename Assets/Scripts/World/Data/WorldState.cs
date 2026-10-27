using System;
using System.Collections.Generic;
using Assets.Shared.SyncSystem.Collections;
using Assets.Shared.SyncSystem.Core;
using Shared.TurnPlanning;
using Shared.WorldObjects;

namespace ServerCore.World.Data
{
    public sealed class SceneState
    {
        public string Path { get; set; } = "Assets/Scenes/Test.unity";
        public long Revision { get; set; }
    }

    /// <summary>Единый синхронизируемый корень; View/ассеты/транспорт не входят в данные.</summary>
    public sealed class WorldState : TrackableNode
    {
        public const int CurrentSchemaVersion = 3;
        public readonly Sync<SceneState> Scene = new(new SceneState());
        public readonly Sync<PlanningState> Planning = new(new PlanningState());
        public readonly SyncList<PlayerData> Players = new();
        public readonly SyncList<FurnitureData> Furniture = new();
        public bool IsInitialized { get; private set; }

        public IEnumerable<ObjectData> Objects
        {
            get
            {
                foreach (var player in Players) yield return player;
                foreach (var furniture in Furniture) yield return furniture;
            }
        }

        public ObjectData Find(string id)
        {
            foreach (var item in Objects) if (item.Id.Value == id) return item;
            return null;
        }

        public void MarkInitialized() => IsInitialized = true;

        public override Dictionary<string, object> CreateSnapshot()
        {
            var result = base.CreateSnapshot();
            result["$schema"] = CurrentSchemaVersion;
            return result;
        }

        /// <summary>Одна транзакция/патч: зрители не увидят половину исполненного хода.</summary>
        public void PublishSnapshot(Dictionary<string, object> snapshot)
        {
            ApplySnapshot(snapshot);
            OnChanged("$world", null, CreateSnapshot());
        }

        public override void ApplyPatch(string path, object value)
        {
            if (path == "$world") ApplySnapshot(SyncValueConverter.ConvertValue<Dictionary<string, object>>(value));
            else base.ApplyPatch(path, value);
        }

        public override void ApplySnapshot(Dictionary<string, object> snapshot)
        {
            if (snapshot == null || !snapshot.TryGetValue("$schema", out var version))
                throw new InvalidOperationException("WorldState: отсутствует версия snapshot.");
            int schema = SyncValueConverter.ConvertValue<int>(version);
            if (schema != 2 && schema != CurrentSchemaVersion)
                throw new InvalidOperationException("WorldState: поддерживаются схемы 2 и 3. Boxes v1 не поддерживается.");
            var scene = schema == 2 ? new SceneState() : Read<SceneState>(snapshot, nameof(Scene));
            var planning = schema == 2 ? new PlanningState() : Read<PlanningState>(snapshot, nameof(Planning));
            if (string.IsNullOrWhiteSpace(scene.Path) || planning.Plans == null || planning.Participants == null)
                throw new InvalidOperationException("WorldState: некорректная сцена или планы.");
            using (SyncMutationScope.EnterSilent())
            {
                Players.ApplySnapshot(SelectCollection(snapshot, nameof(Players)));
                Furniture.ApplySnapshot(SelectCollection(snapshot, nameof(Furniture)));
                Planning.SetValueSilent(planning);
                Scene.SetValueSilent(scene);
            }
            IsInitialized = true;
            OnPatched(nameof(WorldState), snapshot);
        }

        private static T Read<T>(Dictionary<string, object> snapshot, string key) where T : class
        {
            if (!snapshot.TryGetValue(key, out var value) || SyncValueConverter.ConvertValue<T>(value) is not T result)
                throw new InvalidOperationException("WorldState: отсутствует " + key);
            return result;
        }

        private static Dictionary<string, object> SelectCollection(Dictionary<string, object> source, string name)
        {
            var result = new Dictionary<string, object>();
            string prefix = name + ".";
            foreach (var pair in source)
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) result.Add(pair.Key.Substring(prefix.Length), pair.Value);
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using ServerCore.World.Data;
using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World.Entities
{
    [Serializable]
    public sealed class EntityRecord
    {
        public string Id;
        public string DefinitionId;
        public EntityKind Kind;
        public string OwnerId = "";
        public string DisplayName = "";
        public Vector3Int Cell;
        public Vector3 Rotation;
        public Vector3 Scale = Vector3.one;
        public List<EntityParameter> Parameters = new();

        public ObjectData CreateData()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DefinitionId))
                throw new InvalidOperationException("Entity requires a nonempty Id and DefinitionId.");
            Guid owner = Guid.Empty;
            if (!string.IsNullOrEmpty(OwnerId) && !Guid.TryParse(OwnerId, out owner))
                throw new InvalidOperationException($"Entity {Id}: OwnerId must be a GUID or empty.");
            if (!Finite(Rotation) || !Finite(Scale) || Scale.x == 0 || Scale.y == 0 || Scale.z == 0)
                throw new InvalidOperationException($"Entity {Id}: transform must be finite with nonzero scale.");
            ObjectData result = Kind switch
            {
                EntityKind.Player => new PlayerData(),
                EntityKind.Furniture => new FurnitureData(),
                _ => throw new InvalidOperationException($"Entity {Id}: unsupported kind {Kind}.")
            };
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Parameters != null)
                foreach (var parameter in Parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key) || parameters.ContainsKey(parameter.Key))
                        throw new InvalidOperationException($"Entity {Id}: parameter keys must be nonempty and unique.");
                    parameters.Add(parameter.Key, parameter.Value ?? "");
                }
            result.Id.Value = Id;
            result.DefinitionId.Value = DefinitionId;
            result.OwnerId.Value = owner;
            result.DisplayName.Value = DisplayName ?? "";
            result.Cell.Value = new GridCoordinate(Cell.x, Cell.y, Cell.z);
            result.Transform.Value = new ObjectTransformData
            {
                RotationX = Rotation.x, RotationY = Rotation.y, RotationZ = Rotation.z,
                ScaleX = Scale.x, ScaleY = Scale.y, ScaleZ = Scale.z
            };
            result.Parameters.Value = parameters;
            return result;
        }

        public static EntityRecord FromData(ObjectData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var pose = data.Transform.Value ?? throw new InvalidOperationException("Entity Transform is null.");
            var result = new EntityRecord
            {
                Id = data.Id.Value, DefinitionId = data.DefinitionId.Value,
                Kind = data is PlayerData ? EntityKind.Player : data is FurnitureData ? EntityKind.Furniture :
                    throw new ArgumentException("Unsupported entity data type.", nameof(data)),
                OwnerId = data.OwnerId.Value == Guid.Empty ? "" : data.OwnerId.Value.ToString(),
                DisplayName = data.DisplayName.Value,
                Cell = new Vector3Int(data.Cell.Value.X, data.Cell.Value.Y, data.Cell.Value.Z),
                Rotation = new Vector3(pose.RotationX, pose.RotationY, pose.RotationZ),
                Scale = new Vector3(pose.ScaleX, pose.ScaleY, pose.ScaleZ)
            };
            if (data.Parameters.Value != null)
                foreach (var pair in data.Parameters.Value)
                    result.Parameters.Add(new EntityParameter { Key = pair.Key, Value = pair.Value });
            return result;
        }

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

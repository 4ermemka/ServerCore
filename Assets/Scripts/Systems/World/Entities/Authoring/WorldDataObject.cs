using System;
using System.Collections.Generic;
using ServerCore.World.Data;
using UnityEngine;

namespace ServerCore.World.Entities
{
    /// <summary>Persistent scene authoring data; runtime state contains only pure entity models.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldDataObject : MonoBehaviour
    {
        [SerializeField] private GridProvider _gridProvider;
        [SerializeField] private Transform _entitiesRoot;
        [SerializeField] private BaseEntity[] _prefabs = Array.Empty<BaseEntity>();
        [SerializeField] private List<EntityRecord> _entities = new();

        public GridProvider GridProvider => _gridProvider;
        public Transform EntitiesRoot => _entitiesRoot != null ? _entitiesRoot : transform;
        public BaseEntity[] Prefabs => _prefabs;
        public List<EntityRecord> Entities => _entities;
        public WorldState RuntimeState { get; private set; }

        public void Bind(WorldState state) => RuntimeState = state;

        public void SeedInto(WorldState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ValidateRecords(_entities);
            foreach (var record in _entities)
                if (state.Find(record.Id) != null)
                    throw new InvalidOperationException($"Entity {record.Id} already exists in WorldState.");
            foreach (var record in _entities)
                GetPrefab(record).AddToWorld(state, record.CreateData());
        }

        /// <summary>Validates the complete proposed set before any scene or world mutation.</summary>
        public void ValidateRecords(IEnumerable<EntityRecord> records)
        {
            if (_gridProvider == null) throw new InvalidOperationException("WorldDataObject: assign Grid Provider.");
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            if (_prefabs == null) throw new InvalidOperationException("WorldDataObject: assign entity prefabs.");
            foreach (var prefab in _prefabs)
                if (prefab == null || string.IsNullOrWhiteSpace(prefab.DefinitionId) || !definitions.Add(prefab.DefinitionId))
                    throw new InvalidOperationException("WorldDataObject: prefabs require unique nonempty DefinitionIds.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var playerOwners = new HashSet<Guid>();
            var grid = new UnityGridSpace(_gridProvider);
            if (records == null) throw new InvalidOperationException("WorldDataObject: entity records are null.");
            foreach (var record in records)
            {
                if (record == null) throw new InvalidOperationException("WorldDataObject: null entity record.");
                var model = record.CreateData();
                if (record.Kind == EntityKind.Player && model.OwnerId.Value != Guid.Empty && !playerOwners.Add(model.OwnerId.Value))
                    throw new InvalidOperationException($"Duplicate player OwnerId: {model.OwnerId.Value}." );
                if (!ids.Add(record.Id)) throw new InvalidOperationException($"Duplicate entity Id: {record.Id}.");
                var prefab = GetPrefab(record);
                if (!prefab.Accepts(model) || (record.Kind == EntityKind.Player) != (prefab is PlayerEntity) ||
                    (record.Kind == EntityKind.Furniture) != (prefab is FurnitureEntity))
                    throw new InvalidOperationException($"Entity {record.Id}: prefab type does not match Kind.");
                if (!grid.Contains(model.Cell.Value))
                    throw new InvalidOperationException($"Entity {record.Id}: cell {record.Cell} does not exist in the grid.");
            }
        }

        public BaseEntity GetPrefab(EntityRecord record)
        {
            if (_prefabs != null)
                foreach (var prefab in _prefabs)
                    if (prefab != null && prefab.DefinitionId == record.DefinitionId) return prefab;
            throw new InvalidOperationException($"Entity {record.Id}: definition '{record.DefinitionId}' has no prefab mapping.");
        }
    }
}


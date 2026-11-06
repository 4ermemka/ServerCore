using System;
using System.Collections.Generic;
using System.Linq;
using ServerCore.World.Data;
using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World.Entities
{
    /// <summary>Scene policy for first-time player placement. GameSession invokes this only on the host.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldSceneController))]
    public sealed class PlayerEntitySpawner : MonoBehaviour
    {
        [SerializeField] private PlayerEntity _playerPrefab;
        [SerializeField] private Vector3 _spawnOrigin;

        public PlayerData EnsurePlayer(Guid clientId)
        {
            var world = GetComponent<WorldSceneController>();
            if (_playerPrefab == null || world.Grid == null)
                throw new InvalidOperationException("Player spawning requires a prefab and an initialized world grid.");
            return OwnedObjectRegistry.GetOrCreate(world.State.Players, clientId, () =>
            {
                string id = "player:" + clientId.ToString("N");
                if (world.State.Find(id) != null) throw new InvalidOperationException("The player ID is already used by another entity.");
                var occupied = new HashSet<GridCoordinate>(world.State.Objects.Select(item => item.Cell.Value));
                var provider = world.Grid.Provider;
                var cell = provider.AllCells
                    .Where(candidate => candidate.IsValid && !occupied.Contains(UnityGridSpace.Address(candidate)))
                    .OrderBy(candidate => (candidate.GetWorldPosition(provider.CellSize) - transform.TransformPoint(_spawnOrigin)).sqrMagnitude)
                    .ThenBy(candidate => candidate.GridPosition.x)
                    .ThenBy(candidate => candidate.GridPosition.z)
                    .ThenBy(candidate => candidate.GridPosition.y)
                    .FirstOrDefault();
                if (cell == null) throw new InvalidOperationException("No free grid cell is available for a new player.");
                var data = (PlayerData)_playerPrefab.CreateData(id, UnityGridSpace.Address(cell));
                data.DisplayName.Value = "Player " + clientId.ToString("N").Substring(0, 8);
                return data;
            });
        }
    }
}

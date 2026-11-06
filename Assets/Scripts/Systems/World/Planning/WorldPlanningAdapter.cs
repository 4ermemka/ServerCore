using System;
using System.Collections.Generic;
using ServerCore.World.Data;
using Shared.TurnPlanning;
using Shared.WorldObjects;

namespace ServerCore.World.Planning
{
    public sealed class WorldPlanningAdapter : IGridPlanningWorld
    {
        private readonly WorldState state;
        private readonly IGridSpace grid;
        public WorldPlanningAdapter(WorldState state, IGridSpace grid) { this.state = state; this.grid = grid; }
        public bool Contains(string id) => state.Find(id) != null;
        public Guid OwnerOf(string id) => state.Find(id).OwnerId.Value;
        public GridCoordinate PositionOf(string id) => state.Find(id).Cell.Value;
        public bool ContainsCell(GridCoordinate cell) => grid != null && grid.Contains(cell);
        public void Move(string id, GridCoordinate cell) => state.Find(id).Cell.Value = cell;
        public string GetParameter(string id, string key) => state.Find(id).Parameters.Value.TryGetValue(key, out var value) ? value : null;
        public void SetParameter(string id, string key, string value)
        {
            var data = state.Find(id);
            var copy = new Dictionary<string, string>(data.Parameters.Value) { [key] = value };
            data.Parameters.Value = copy;
        }
    }
}

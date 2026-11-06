using ServerCore.World.Data;
using Shared.WorldObjects;

namespace ServerCore.World.Entities
{
    public sealed class PlayerEntity : PersonEntity
    {
        public PlayerData PlayerData => Data as PlayerData;
        public override bool Accepts(ObjectData data) => data is PlayerData;
        public override ObjectData CreateData(string id, GridCoordinate cell)
        {
            var data = new PlayerData();
            data.Id.Value = id;
            data.DefinitionId.Value = DefinitionId;
            data.Cell.Value = cell;
            return data;
        }
        public override void AddToWorld(WorldState state, ObjectData data) => state.Players.Add((PlayerData)data);
    }
}

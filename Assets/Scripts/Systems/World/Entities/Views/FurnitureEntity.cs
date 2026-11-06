using ServerCore.World.Data;
using Shared.WorldObjects;

namespace ServerCore.World.Entities
{
    public sealed class FurnitureEntity : BaseEntity
    {
        public FurnitureData FurnitureData => Data as FurnitureData;
        public override bool Accepts(ObjectData data) => data is FurnitureData;
        public override ObjectData CreateData(string id, GridCoordinate cell)
        {
            var data = new FurnitureData();
            data.Id.Value = id;
            data.DefinitionId.Value = DefinitionId;
            data.Cell.Value = cell;
            return data;
        }
        public override void AddToWorld(WorldState state, ObjectData data) => state.Furniture.Add((FurnitureData)data);
    }
}

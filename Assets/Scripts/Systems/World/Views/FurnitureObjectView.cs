using ServerCore.World.Data;
using Shared.WorldObjects;

namespace ServerCore.World
{
    public class FurnitureObjectView : WorldObjectView<FurnitureData>
    {
        public override void AddToWorld(WorldState state, ObjectData data) => state.Furniture.Add((FurnitureData)data);
    }
}

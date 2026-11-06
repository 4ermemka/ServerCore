using ServerCore.World.Data;
using Shared.WorldObjects;

namespace ServerCore.World
{
    public sealed class PlayerObjectView : WorldObjectView<PlayerData>
    {
        public override void AddToWorld(WorldState state, ObjectData data) => state.Players.Add((PlayerData)data);
    }
}

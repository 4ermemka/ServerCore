using Shared.WorldObjects;

namespace ServerCore.World
{
    /// <summary>Ввод/View не знают, локальная это игра, сеть или иной источник команд.</summary>
    public interface IWorldInteraction
    {
        bool CanEdit(ObjectData data);
        void Select(string objectId);
        void PlanMove(string objectId, GridCoordinate target);
        bool TryPlannedPosition(string objectId, out GridCoordinate cell);
    }
}

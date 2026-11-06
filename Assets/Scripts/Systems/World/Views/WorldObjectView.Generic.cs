using Shared.WorldObjects;

namespace ServerCore.World
{
    /// <summary>При добавлении вида объекта повторяется только создание и выбор коллекции.</summary>
    public abstract class WorldObjectView<T> : WorldObjectView where T : ObjectData, new()
    {
        public override bool Accepts(ObjectData data) => data is T;
        public override ObjectData CreateData(string id, GridCoordinate cell)
        {
            var data = new T();
            data.Id.Value = id;
            data.DefinitionId.Value = DefinitionId;
            data.Cell.Value = cell;
            return data;
        }
    }
}

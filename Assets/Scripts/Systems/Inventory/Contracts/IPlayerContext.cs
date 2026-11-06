namespace ServerCore.Inventory
{
    /// <summary>Общий контекст игрока; идентификаторы навыков и чертежей задаются данными проекта.</summary>
    public interface IPlayerContext : IObservableInventoryContext
    {
        int Level { get; }

        int GetSkillLevel(string id);
        bool HasBlueprint(string id);
        void AddSkillExperience(string id, int amount);
    }
}

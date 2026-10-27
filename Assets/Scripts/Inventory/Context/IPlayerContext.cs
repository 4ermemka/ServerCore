using System;

namespace ServerCore.Inventory
{
    /// <summary>Уведомляет станции об изменении внешних условий на игровом потоке.</summary>
    public interface IObservableInventoryContext
    {
        event Action Changed;
    }

    /// <summary>Общий контекст игрока; идентификаторы навыков и чертежей задаются данными проекта.</summary>
    public interface IPlayerContext : IObservableInventoryContext
    {
        int Level { get; }
        int GetSkillLevel(string id);
        bool HasBlueprint(string id);
        void AddSkillExperience(string id, int amount);
    }
}

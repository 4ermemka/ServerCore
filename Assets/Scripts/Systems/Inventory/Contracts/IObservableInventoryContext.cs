using System;

namespace ServerCore.Inventory
{
    /// <summary>Уведомляет станции об изменении внешних условий на игровом потоке.</summary>
    public interface IObservableInventoryContext
    {
        event Action Changed;
    }
}

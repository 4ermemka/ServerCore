using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;

namespace ServerCore.Inventory
{
    /// <summary>Отложенная атомарная операция. quantity — выбранное при Drop количество.</summary>
    public delegate InventoryOperationResult<ItemInstance> InventoryTransfer(SlottedInventory<ItemInstance> target, int targetIndex, int quantity);
}

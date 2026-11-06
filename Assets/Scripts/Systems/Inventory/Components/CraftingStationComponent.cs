using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using Assets.Shared.Systems.InventorySystem.Slots;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Общий контракт для UI любой станции. Унаследованная Model содержит входные ресурсы;
    /// Output хранит подтверждённый результат. UI не зависит от типа внешнего контекста.
    /// </summary>
    public abstract class CraftingStationComponent : InventoryComponent
    {
        /// <summary>Доступные сейчас варианты. Preview — выбранный вариант, ещё не принадлежащий инвентарю.</summary>
        public abstract IReadOnlyList<CraftPreview<ItemInstance>> Previews { get; }
        public abstract CraftPreview<ItemInstance> Preview { get; }
        /// <summary>Одна ячейка готового предмета после TryConfirm; пока она занята, новый крафт блокируется.</summary>
        public abstract SlottedInventory<ItemInstance> Output { get; }
        public abstract event Action PreviewChanged;
        /// <summary>Подтверждает конкретный показанный preview и выдаёт предмет в свободный допустимый слот назначения.</summary>
        public abstract InventoryOperationResult<ItemInstance> TryClaim(CraftPreview<ItemInstance> preview,
            SlottedInventory<ItemInstance> target, int targetSlot = -1);
        /// <summary>Вариант подтверждения кнопкой: расходует ингредиенты и помещает результат в Output.</summary>
        public abstract InventoryOperationResult<ItemInstance> TryConfirm();
        public abstract InventoryValidationResult SelectRecipe(string id);
    }
}

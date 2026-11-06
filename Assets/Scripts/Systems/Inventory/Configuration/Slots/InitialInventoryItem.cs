using System;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>Описание стартовой стопки. При создании модели превращается в ItemInstance с новым ItemId.</summary>
    [Serializable]
    public class InitialInventoryItem
    {
        public ItemDefinition Item;
        [Min(1)]
        public int Quantity = 1;
        [Tooltip("-1 chooses the first compatible empty slot.")]
        public int Slot = -1;
    }
}

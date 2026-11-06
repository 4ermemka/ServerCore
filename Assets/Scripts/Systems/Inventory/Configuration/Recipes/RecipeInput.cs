using System;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Crafting;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Требование к ингредиенту: конкретный вид ИЛИ любой указанный тег. Пустой фильтр ничего не принимает.
    /// SlotIndex = -1 собирает количество из любых подходящих стопок; иной индекс закрепляет одну ячейку.
    /// </summary>
    [Serializable]
    public sealed class RecipeInput
    {
        public ItemDefinition[] Items = Array.Empty<ItemDefinition>();
        public ItemTag[] Tags = Array.Empty<ItemTag>();
        [Min(1)]
        public int Quantity = 1;
        [Min(-1)]
        public int SlotIndex = -1;
        internal RecipeIngredient<ItemInstance> Compile()
        {
            if (SlotIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(SlotIndex));
            var items = (Items ?? Array.Empty<ItemDefinition>()).Where(item => item != null).ToArray();
            var tags = (Tags ?? Array.Empty<ItemTag>()).Where(tag => tag != null).ToArray();
            return new RecipeIngredient<ItemInstance>(item => item != null && (items.Any(item.Is) || tags.Any(item.HasTag)), Quantity, SlotIndex < 0 ? (int? )null : SlotIndex);
        }
    }
}

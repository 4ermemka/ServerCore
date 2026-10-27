using System;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Настройки одной ячейки в Inspector. Подсказка независима от фильтра:
    /// HintItem/HintTag показывают ожидание, но сами по себе не запрещают другие предметы.
    /// </summary>
    [Serializable]
    public class SlotSettings
    {
        [Min(0)] public int Index;
        public string Label;
        public Sprite PlaceholderIcon;
        public ItemDefinition HintItem;
        public ItemTag HintTag;
        public ItemDefinition[] AllowedItems = Array.Empty<ItemDefinition>();
        public ItemTag[] AllowedTags = Array.Empty<ItemTag>();

        // Явная подпись/иконка имеют приоритет, затем конкретный предмет, затем группа.
        public string DisplayLabel => !string.IsNullOrWhiteSpace(Label) ? Label
            : HintItem != null ? HintItem.DisplayName : HintTag != null ? HintTag.DisplayName : "";
        public Sprite DisplayIcon => PlaceholderIcon != null ? PlaceholderIcon
            : HintItem != null ? HintItem.Icon : HintTag != null ? HintTag.Icon : null;

        /// <summary>
        /// Создаёт фильтр по снимку списков: конкретный предмет ИЛИ любой из тегов.
        /// Если оба списка пусты, ограничений нет. null-предмет допускает сам SlotRule.
        /// </summary>
        public SlotRule<ItemInstance> CreateRule()
        {
            var items = (AllowedItems ?? Array.Empty<ItemDefinition>()).Where(item => item != null).ToArray();
            var tags = (AllowedTags ?? Array.Empty<ItemTag>()).Where(tag => tag != null).ToArray();
            return new SlotRule<ItemInstance>(DisplayLabel, item =>
                items.Length == 0 && tags.Length == 0 || items.Any(item.Is) || tags.Any(item.HasTag));
        }
    }

    /// <summary>Описание стартовой стопки. При создании модели превращается в ItemInstance с новым ItemId.</summary>
    [Serializable]
    public class InitialInventoryItem
    {
        public ItemDefinition Item;
        [Min(1)] public int Quantity = 1;
        [Tooltip("-1 chooses the first compatible empty slot.")]
        public int Slot = -1;
    }
}

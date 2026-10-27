using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Items;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Конкретная стопка в игре: уникальный ItemId, ссылка на общие данные Definition и Quantity.
    /// Один вид может иметь много экземпляров. Инвентарь хранит эти объекты как неизменяемые значения.
    /// </summary>
    [Serializable]
    public class ItemInstance : IStackableItem<ItemInstance>
    {
        public string ItemId { get; private set; }
        public ItemDefinition Definition { get; }
        public int Quantity { get; private set; }
        public int MaxStack => Definition.MaxStack;
        public string Name => DisplayName;
        public virtual string DisplayName => Definition.DisplayName;
        public virtual string Description => Definition.Description;

        /// <summary>Создаёт новую стопку; явный itemId пригодится при восстановлении сохранённого экземпляра.</summary>
        public ItemInstance(ItemDefinition definition, int quantity = 1, string itemId = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id)) throw new ArgumentException("The item definition needs a stable ID.", nameof(definition));
            if (quantity < 1 || quantity > definition.MaxStack) throw new ArgumentOutOfRangeException(nameof(quantity));
            Definition = definition;
            Quantity = quantity;
            ItemId = string.IsNullOrWhiteSpace(itemId) ? Guid.NewGuid().ToString("N") : itemId;
        }

        /// <summary>Проверяет вид предмета, а не уникальный ItemId этой стопки.</summary>
        public bool Is(ItemDefinition definition) => definition != null &&
            (Definition == definition || (!string.IsNullOrEmpty(definition.Id) && Definition.Id == definition.Id));

        public bool HasTag(ItemTag tag) => Definition.HasTag(tag);

        /// <summary>Неизвестный подкласс должен явно разрешить слияние и сравнить свои дополнительные параметры.</summary>
        protected virtual bool SupportsStackMerging => GetType() == typeof(ItemInstance);
        public virtual bool CanStackWith(ItemInstance other) =>
            other != null && SupportsStackMerging && other.SupportsStackMerging &&
            GetType() == other.GetType() && Is(other.Definition);

        public virtual ItemInstance WithStack(int quantity, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("A stack needs an identity.", nameof(itemId));
            var copy = WithQuantity(quantity);
            if (ReferenceEquals(copy, this)) throw new InvalidOperationException("WithQuantity must return an immutable copy.");
            copy.ItemId = itemId;
            return copy;
        }

        /// <summary>
        /// Возвращает остаток после частичного расхода, сохраняя ItemId и фактический тип подкласса.
        /// Копия поверхностная: TData и прочие ссылочные параметры также должны быть неизменяемыми.
        /// Нулевой остаток выражается пустым слотом, поэтому quantity должен быть положительным.
        /// </summary>
        public virtual ItemInstance WithQuantity(int quantity)
        {
            if (quantity < 1 || quantity > Definition.MaxStack) throw new ArgumentOutOfRangeException(nameof(quantity));
            var copy = (ItemInstance)MemberwiseClone();
            copy.Quantity = quantity;
            return copy;
        }
    }

    /// <summary>
    /// Точка расширения для собственных параметров экземпляра: уровень, качество и т.п.
    /// Тип TData не влияет на UI или рецептурное ядро; при необходимости наследуйте класс и измените описание.
    /// Это модель времени выполнения: атрибут Serializable сам по себе не сохраняет её read-only свойства в Unity.
    /// </summary>
    [Serializable]
    public class ItemInstance<TData> : ItemInstance
    {
        public TData Data { get; }
        protected override bool SupportsStackMerging => GetType() == typeof(ItemInstance<TData>);
        public override bool CanStackWith(ItemInstance other) => base.CanStackWith(other) &&
            other is ItemInstance<TData> value && EqualityComparer<TData>.Default.Equals(Data, value.Data);

        public ItemInstance(ItemDefinition definition, TData data, int quantity = 1, string itemId = null)
            : base(definition, quantity, itemId) => Data = data;
    }
}

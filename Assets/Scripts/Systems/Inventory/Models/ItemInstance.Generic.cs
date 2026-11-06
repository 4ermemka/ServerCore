using System;
using System.Collections.Generic;

namespace ServerCore.Inventory
{
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

        public override bool CanStackWith(ItemInstance other) => base.CanStackWith(other) && other is ItemInstance<TData> value && EqualityComparer<TData>.Default.Equals(Data, value.Data);
        public ItemInstance(ItemDefinition definition, TData data, int quantity = 1, string itemId = null) : base(definition, quantity, itemId) => Data = data;
    }
}

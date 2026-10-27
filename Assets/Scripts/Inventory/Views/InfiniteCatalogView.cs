using System;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;
using Assets.Shared.Systems.InventorySystem.Storage;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Отладочный бесконечный каталог. Ячейки показывают шаблоны; drop создаёт новую стопку,
    /// а исходный шаблон не расходуется. Общие фильтры целевого инвентаря продолжают действовать.
    /// </summary>
    public sealed class InfiniteCatalogView : InventoryView
    {
        public ItemCatalog Catalog;
        [Min(1)] public int QuantityPerDrag = 10;
        public string CatalogTitle = "КАТАЛОГ ПРЕДМЕТОВ";
        private ItemCatalog _loaded;
        private int _revision = -1;
        private SlottedInventory<ItemInstance> _display;
        private InfiniteItemStorage<ItemInstance, object> _storage;

        protected override bool AcceptsDrops => false;
        protected override SlottedInventory<ItemInstance> Model
        {
            get
            {
                if (Catalog == null) Catalog = Resources.Load<ItemCatalog>("Inventory/ItemCatalog");
                if (_display == null || _loaded != Catalog || _revision != (Catalog != null ? Catalog.Revision : 0))
                    RebuildCatalog();
                return _display;
            }
        }

        private void RebuildCatalog()
        {
            _loaded = Catalog;
            _revision = Catalog != null ? Catalog.Revision : 0;
            var definitions = (Catalog != null ? Catalog.Items : null) ?? Array.Empty<ItemDefinition>();
            var templates = definitions.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id) && item.MaxStack > 0)
                .Select(item => new ItemInstance(item)).ToArray();
            _display = new SlottedInventory<ItemInstance>(templates.Length, name: CatalogTitle);
            foreach (var template in templates) _display.TryAdd(template);
            _storage = new InfiniteItemStorage<ItemInstance, object>(Guid.NewGuid().ToString("N"),
                new DelegateInventoryItemFactory<ItemInstance>((template, id) =>
                    new ItemInstance(template.Definition, Mathf.Clamp(QuantityPerDrag, 1, template.Definition.MaxStack), id)),
                name: CatalogTitle, initialTemplates: templates);
        }

        public override string GetQuantityText(int index, ItemInstance item) => item == null ? "" : "∞";
        public override int GetDragQuantity(int index) => GetItem(index) is ItemInstance item
            ? Mathf.Clamp(QuantityPerDrag, 1, item.MaxStack) : 0;

        public override void Refresh()
        {
            base.Refresh();
            if (TitleLabel != null) TitleLabel.text = CatalogTitle;
        }

        protected override InventoryTransfer CaptureTransfer(int index)
        {
            var model = Model;
            if (index < 0 || index >= model.SlotCount) return null;
            var template = model[index];
            var storage = _storage;
            var maximum = GetDragQuantity(index);
            return (target, targetIndex, quantity) =>
            {
                if (!ReferenceEquals(model, Model) || !ReferenceEquals(storage, _storage))
                    return Reject("catalog_changed", "Каталог изменился; начните перенос заново.");
                if (quantity < 1 || quantity > maximum || maximum != GetDragQuantity(index))
                    return Reject("invalid_quantity", "Выбранное количество больше недоступно.");
                // Materialize выдаёт новую идентичность. Отказ целевого TryAdd оставит обе видимые модели прежними.
                var issued = storage.TryMaterialize(template.ItemId);
                return issued.Succeeded ? target.TryAdd(issued.Item.WithQuantity(quantity), targetIndex) : issued.Operation;
            };
        }
    }
}

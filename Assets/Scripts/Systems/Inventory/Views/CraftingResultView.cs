using System;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Специализация View для выхода: показывает уже созданный Output[0], иначе предварительный результат.
    /// Принимать предметы из других ячеек нельзя. PreviewChanged нужен отдельно от Output.SlotsChanged,
    /// поскольку предварительный результат ещё не содержится в модели выходного инвентаря.
    /// </summary>
    public sealed class CraftingResultView : InventoryView
    {
        public CraftingStationComponent Station;
        public SlotSettings ResultSettings = new SlotSettings { Index = 0 };
        private CraftingStationComponent _subscribedStation;

        protected override SlottedInventory<ItemInstance> Model => Station != null ? Station.Output : null;
        protected override bool AcceptsDrops => false;

        public override void Rebind()
        {
            UnsubscribeStation();
            if (isActiveAndEnabled && Station != null)
            {
                _subscribedStation = Station;
                _subscribedStation.PreviewChanged += Refresh;
            }
            base.Rebind();
        }

        protected override void LateUpdate()
        {
            if (_subscribedStation != Station) Rebind();
            else base.LateUpdate();
        }

        protected override void OnDisable()
        {
            UnsubscribeStation();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            UnsubscribeStation();
            base.OnDestroy();
        }

        private void UnsubscribeStation()
        {
            if (_subscribedStation != null) _subscribedStation.PreviewChanged -= Refresh;
            _subscribedStation = null;
        }

        public override ItemInstance GetItem(int index)
        {
            var stored = base.GetItem(index);
            return stored ?? (index == 0 && Station != null ? Station.Preview?.Result : null);
        }

        public override SlotSettings GetSlotSettings(int index) => index == 0 ? ResultSettings : null;
        public override bool CanSplitDrag(int index) => base.GetItem(index) != null;

        protected override InventoryTransfer CaptureTransfer(int index)
        {
            // Готовый предмет переносится обычным путём и не расходует ингредиенты повторно.
            if (base.GetItem(index) != null) return base.CaptureTransfer(index);
            var station = Station;
            // Захватываем именно увиденный результат: смена ресурсов/контекста во время drag приведёт к отказу.
            var preview = station != null && index == 0 ? station.Preview : null;
            if (preview == null) return null;
            return (target, targetIndex, quantity) => quantity != preview.Result.Quantity
                ? Reject("partial_preview", "Предварительный результат можно получить только целиком.")
                : station != null && Station == station
                ? station.TryClaim(preview, target, targetIndex)
                : Reject("stale_drag", "Источник результата изменился.");
        }
    }
}

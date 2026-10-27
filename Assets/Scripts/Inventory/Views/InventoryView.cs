using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    /// <summary>Отложенная атомарная операция. quantity — выбранное при Drop количество.</summary>
    public delegate InventoryOperationResult<ItemInstance> InventoryTransfer(
        SlottedInventory<ItemInstance> target, int targetIndex, int quantity);
    /// <summary>
    /// Связывает Source.Model с UI-ячейками. Геометрия задаётся prefab-ом и layout-компонентами Unity.
    /// OnEnable подписывается на модель, OnDisable/OnDestroy отписываются; предметы остаются в Source.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryView : MonoBehaviour
    {
        public InventoryComponent Source;
        /// <summary>Родитель только для ячеек этого представления; вложенные InventoryView обслуживают себя сами.</summary>
        public RectTransform Content;
        public InventorySlotView SlotPrefab;
        /// <summary>Генерировать ячейки по вместимости модели или использовать вручную заданные Index.</summary>
        public bool AutoGenerate = true;
        public Text TitleLabel;
        public Text CountLabel;
        public Text HintLabel;

        private readonly List<InventorySlotView> _slots = new List<InventorySlotView>();
        private readonly List<InventorySlotView> _generated = new List<InventorySlotView>();
        private SlottedInventory<ItemInstance> _boundModel;
        private RectTransform _boundContent;
        private bool _boundAutoGenerate;
        private bool _refreshing;
        private Text _authoredTitleSource, _authoredHintSource;
        private string _authoredTitle, _authoredHint;

        public IReadOnlyList<InventorySlotView> Slots => _slots;
        public SlottedInventory<ItemInstance> BoundModel => _boundModel;
        /// <summary>Результат UI-переноса для сообщений пользователю; не является событием всех изменений модели.</summary>
        public event Action<InventoryOperationResult<ItemInstance>> OperationCompleted;
        protected virtual SlottedInventory<ItemInstance> Model => Source != null ? Source.Model : null;
        protected virtual bool AcceptsDrops => true;

        protected virtual void OnEnable() => Rebind();
        protected virtual void OnDisable()
        {
            InventoryDragController.Cancel(this);
            Unsubscribe();
        }

        protected virtual void OnDestroy()
        {
            InventoryDragController.Cancel(this);
            Unsubscribe();
        }

        protected virtual void LateUpdate()
        {
            if (!ReferenceEquals(Model, _boundModel) || Content != _boundContent || AutoGenerate != _boundAutoGenerate) Rebind();
        }

        protected virtual void OnApplicationFocus(bool focused)
        {
            if (!focused) InventoryDragController.Cancel(this);
        }

        public void Configure(InventoryComponent source, RectTransform content,
            InventorySlotView slotPrefab, bool autoGenerate = true)
        {
            Source = source;
            Content = content;
            SlotPrefab = slotPrefab;
            AutoGenerate = autoGenerate;
            Rebind();
        }

        /// <summary>Без накопления подписок переподключает Source/Content/режим и сразу обновляет отображение.</summary>
        public virtual void Rebind()
        {
            InventoryDragController.Cancel(this);
            Unsubscribe();
            if (_boundContent != Content) ClearGenerated();
            _boundContent = Content;
            _boundAutoGenerate = AutoGenerate;
            if (isActiveAndEnabled)
            {
                _boundModel = Model;
                if (_boundModel != null) _boundModel.SlotsChanged += Refresh;
            }
            Refresh();
        }

        private void Unsubscribe()
        {
            if (_boundModel != null) _boundModel.SlotsChanged -= Refresh;
            _boundModel = null;
        }

        public virtual ItemInstance GetItem(int index) =>
            _boundModel != null && index >= 0 && index < _boundModel.SlotCount ? _boundModel[index] : null;

        public virtual SlotSettings GetSlotSettings(int index) => Source != null ? Source.GetSlotSettings(index) : null;

        /// <summary>Подпись количества; бесконечные каталоги могут показывать свой символ вместо размера стопки.</summary>
        public virtual string GetQuantityText(int index, ItemInstance item) => item != null ? $"×{item.Quantity}" : string.Empty;
        public virtual int GetDragQuantity(int index) => GetItem(index)?.Quantity ?? 0;
        public virtual bool CanSplitDrag(int index) => true;

        /// <summary>Перечитывает модель по SlotsChanged. Можно вызвать вручную после изменения только подписей/иконок.</summary>
        public virtual void Refresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                CaptureText(TitleLabel, ref _authoredTitleSource, ref _authoredTitle);
                CaptureText(HintLabel, ref _authoredHintSource, ref _authoredHint);
                SyncSlots();
                foreach (var slot in _slots) if (slot != null) slot.Refresh();
                if (TitleLabel != null) TitleLabel.text = Source != null ? Source.Title : _authoredTitle;
                if (CountLabel != null)
                {
                    var occupied = 0;
                    var count = _boundModel?.SlotCount ?? 0;
                    for (var i = 0; i < count; i++) if (GetItem(i) != null) occupied++;
                    CountLabel.text = $"{occupied} / {count}";
                }
            }
            finally { _refreshing = false; }
        }

        private void SyncSlots()
        {
            var count = _boundModel?.SlotCount ?? 0;
            _slots.Clear();
            if (Content == null) return;
            if (!AutoGenerate)
            {
                // Порядок обхода и координаты не определяют содержимое: у каждой авторской ячейки свой Index.
                ClearGenerated();
                foreach (var slot in Content.GetComponentsInChildren<InventorySlotView>(true))
                {
                    if (slot.GetComponentInParent<InventoryView>(true) != this) continue;
                    slot.Bind(this, slot.Index);
                    slot.gameObject.SetActive(slot.Index >= 0 && slot.Index < count);
                    _slots.Add(slot);
                }
                return;
            }

            _generated.RemoveAll(slot => slot == null);
            // Используем и сохранённые в prefab-е ячейки. В автоматическом режиме лишние удаляются,
            // недостающие создаются, индексы назначаются последовательно в порядке иерархии.
            foreach (var slot in Content.GetComponentsInChildren<InventorySlotView>(true))
            {
                if (slot.GetComponentInParent<InventoryView>(true) != this) continue;
                if (_slots.Count < count)
                {
                    slot.Bind(this, _slots.Count);
                    slot.gameObject.SetActive(true);
                    _slots.Add(slot);
                }
                else
                {
                    _generated.Remove(slot);
                    RemoveGenerated(slot);
                }
            }
            while (SlotPrefab != null && _slots.Count < count)
            {
                var slot = Instantiate(SlotPrefab, Content, false);
                slot.name = $"Slot {_slots.Count}";
                _generated.Add(slot);
                slot.Bind(this, _slots.Count);
                slot.gameObject.SetActive(true);
                _slots.Add(slot);
            }
        }

        private void ClearGenerated()
        {
            foreach (var slot in _generated) RemoveGenerated(slot);
            _generated.Clear();
        }

        private static void RemoveGenerated(InventorySlotView slot)
        {
            if (slot == null) return;
            slot.Unbind();
            slot.gameObject.SetActive(false);
            // Destroy отложен до конца кадра; отсоединение не даёт повторному Refresh найти удаляемую ячейку.
            slot.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(slot.gameObject);
            else DestroyImmediate(slot.gameObject);
        }

        public void ShowHint(ItemInstance item)
        {
            CaptureText(HintLabel, ref _authoredHintSource, ref _authoredHint);
            if (HintLabel != null) HintLabel.text = item != null ? item.Description : _authoredHint;
        }

        private static void CaptureText(Text label, ref Text source, ref string authored)
        {
            if (label == null || source == label) return;
            source = label;
            authored = label.text;
        }

        /// <summary>
        /// Запоминает источник и ItemId в начале drag; сам перенос выполняется только при drop.
        /// Подкласс результата вместо обычного переноса захватывает токен preview.
        /// </summary>
        protected virtual InventoryTransfer CaptureTransfer(int index)
        {
            var source = _boundModel;
            var item = GetItem(index);
            if (source == null || item == null) return null;
            return (target, targetIndex, quantity) =>
            {
                if (!ReferenceEquals(source, _boundModel) || !ReferenceEquals(source, Model) || index >= source.SlotCount ||
                    !ReferenceEquals(source[index], item))
                    return Reject("stale_drag", "Предмет в исходной ячейке изменился.");
                return source.TryMoveTo(target, index, targetIndex, quantity);
            };
        }

        internal InventoryTransfer CaptureDrag(int index) =>
            isActiveAndEnabled ? CaptureTransfer(index) : null;

        internal InventoryOperationResult<ItemInstance> ReceiveDrop(
            InventoryTransfer transfer, int index, int quantity)
        {
            if (!isActiveAndEnabled || !AcceptsDrops || _boundModel == null || !ReferenceEquals(_boundModel, Model) ||
                index < 0 || index >= _boundModel.SlotCount)
                return Reject("invalid_drop", "В эту ячейку нельзя переместить предмет.");
            return transfer != null ? transfer(_boundModel, index, quantity) : Reject("empty_source", "Нет предмета для перемещения.");
        }

        /// <summary>Программный перенос через те же UI-проверки, что и мышью; для чистой модели используйте TryMoveTo.</summary>
        public InventoryOperationResult<ItemInstance> TryTransferTo(int sourceIndex, InventoryView target, int targetIndex, int quantity = -1)
        {
            var transfer = CaptureDrag(sourceIndex);
            var amount = quantity == -1 ? GetDragQuantity(sourceIndex) : quantity;
            var result = target != null ? target.ReceiveDrop(transfer, targetIndex, amount) : Reject("missing_target", "Нет целевого инвентаря.");
            ReportOperation(result);
            if (target != null && target != this) target.ReportOperation(result);
            return result;
        }

        internal void ReportOperation(InventoryOperationResult<ItemInstance> result) => OperationCompleted?.Invoke(result);

        protected static InventoryOperationResult<ItemInstance> Reject(string code, string message) =>
            InventoryOperationResult<ItemInstance>.Rejected(null, InventoryValidationResult.Deny(code, message), null);
    }
}

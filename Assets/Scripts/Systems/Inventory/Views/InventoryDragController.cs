using System;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Одна активная операция перетаскивания для всех панелей: хранит источник, pointerId и отложенный перенос.
    /// Иконка под курсором — только визуальная копия. До Drop реальные инвентари не меняются.
    /// Отмена, потеря фокуса или скрытие источника удаляют копию без возвратных операций над предметами.
    /// </summary>
    public static class InventoryDragController
    {
        private static InventorySlotView _source;
        private static InventoryTransfer _transfer;
        private static Canvas _canvas;
        private static RectTransform _ghost;
        private static int _pointerId;
        private static int _maximum;
        private static bool _canSplit;
        private static float _scrollRemainder;
        private static Text _quantityLabel;
        public static int SelectedQuantity { get; private set; }
        public static InventoryView SourceView => _source != null ? _source.Owner : null;
        public static event Action DragChanged;
        public static RectTransform ActiveGhost => _ghost;
        public static bool IsDragging => _source != null && _transfer != null;

        // Сбрасываем статическое состояние также при запуске Play Mode без перезагрузки домена.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Cancel(); DragChanged = null; }

        internal static void Begin(InventorySlotView source, PointerEventData data)
        {
            Cancel();
            if (source == null || !source.isActiveAndEnabled || source.Owner == null) return;
            source.Refresh();
            var transfer = source.Owner.CaptureDrag(source.Index);
            var canvas = source.GetComponentInParent<Canvas>();
            if (transfer == null || canvas == null) return;
            _source = source;
            _transfer = transfer;
            _canvas = canvas.rootCanvas;
            _pointerId = data.pointerId;
            _maximum = source.Owner.GetDragQuantity(source.Index);
            SelectedQuantity = _maximum;
            _canSplit = source.Owner.CanSplitDrag(source.Index);
            _scrollRemainder = 0;
            _ghost = new GameObject("Inventory Drag Ghost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image)).GetComponent<RectTransform>();
            _ghost.SetParent(_canvas.transform, false);
            _ghost.anchorMin = _ghost.anchorMax = _ghost.pivot = new Vector2(.5f, .5f);
            var sourceRect = (RectTransform)source.transform;
            var size = _canvas.transform.InverseTransformVector(sourceRect.TransformVector(sourceRect.rect.size));
            _ghost.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
            var group = _ghost.GetComponent<CanvasGroup>();
            // Drop должен попасть в целевую ячейку, а не в изображение под курсором.
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = .85f;
            var icon = _ghost.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.sprite = source.Item?.Definition?.Icon;
            icon.enabled = icon.sprite != null;
            if (source.QuantityLabel != null)
            {
                var label = new GameObject("Quantity", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(_ghost, false);
                var rect = label.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                label.font = source.QuantityLabel.font;
                label.fontSize = source.QuantityLabel.fontSize;
                label.fontStyle = source.QuantityLabel.fontStyle;
                label.color = source.QuantityLabel.color;
                label.alignment = TextAnchor.UpperRight;
                label.text = source.QuantityLabel.text;
                label.raycastTarget = false;
                _quantityLabel = label;
            }
            UpdateQuantityLabel();
            Move(source, data);
            DragChanged?.Invoke();
        }

        /// <summary>Меняет только намерение пользователя. Модель изменится один раз при успешном Drop.</summary>
        public static void ScrollQuantity(Vector2 delta)
        {
            if (!IsDragging || !_canSplit) return;
            _scrollRemainder += delta.y;
            int steps = (int)_scrollRemainder;
            _scrollRemainder -= steps;
            SelectedQuantity = Mathf.Clamp(SelectedQuantity + steps, 1, _maximum);
            if (SelectedQuantity == 1 && _scrollRemainder < 0 || SelectedQuantity == _maximum && _scrollRemainder > 0)
                _scrollRemainder = 0;
            UpdateQuantityLabel();
        }
        private static void UpdateQuantityLabel()
        {
            if (_quantityLabel != null) _quantityLabel.text = $"×{SelectedQuantity}";
        }

        internal static void Move(InventorySlotView source, PointerEventData data)
        {
            if (_source != source || data.pointerId != _pointerId || _ghost == null || _canvas == null) return;
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera ?? data.pressEventCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, data.position, camera, out var point))
                _ghost.localPosition = new Vector3(point.x, point.y, 0);
        }

        internal static void Drop(InventorySlotView target, PointerEventData data)
        {
            if (!IsDragging || data.pointerId != _pointerId || target == null || !target.isActiveAndEnabled || target.Owner == null) return;
            var sourceOwner = _source.Owner;
            var targetOwner = target.Owner;
            var transfer = _transfer;
            var quantity = SelectedQuantity;
            // Убираем drag до вызова модели: её уведомления могут скрыть или перепривязать представление.
            Cancel();
            if (sourceOwner == null || !sourceOwner.isActiveAndEnabled) return;
            var result = targetOwner.ReceiveDrop(transfer, target.Index, quantity);
            sourceOwner.ReportOperation(result);
            if (targetOwner != sourceOwner) targetOwner.ReportOperation(result);
        }

        internal static void End(InventorySlotView source, PointerEventData data)
        {
            if (_source == source && data.pointerId == _pointerId) Cancel();
        }

        public static void Cancel(InventoryView owner)
        {
            if (_source != null && _source.Owner == owner) Cancel();
        }

        public static void Cancel(InventorySlotView source)
        {
            if (_source == source) Cancel();
        }

        public static void Cancel()
        {
            bool wasDragging = _transfer != null;
            _source = null;
            _transfer = null;
            _canvas = null;
            if (_ghost != null)
            {
                _ghost.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(_ghost.gameObject);
                else UnityEngine.Object.DestroyImmediate(_ghost.gameObject);
            }
            _ghost = null;
            _quantityLabel = null; SelectedQuantity = 0; _maximum = 0; _scrollRemainder = 0;
            if (wasDragging) DragChanged?.Invoke();
        }
    }
}

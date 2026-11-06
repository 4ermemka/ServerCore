using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Отображает один индекс модели владельца и передаёт события мыши общему контроллеру drag.
    /// Item — кэш для отображения (в выходной ячейке это может быть preview), а не самостоятельное хранилище.
    /// Фон должен принимать raycast, иконка и подписи не должны закрывать ячейку от событий.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySlotView : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Min(0)] public int Index;
        public Image Background;
        public Image Icon;
        public Text NameLabel;
        public Text QuantityLabel;
        public Text DescriptionLabel;
        public Color ItemColor = Color.white;
        public Color PlaceholderColor = new Color(1, 1, 1, .18f);
        public Color HoverColor = new Color32(47, 72, 82, 255);

        private Color _backgroundColor;
        private bool _hovered;
        private Image _authoredIconSource;
        private Text _authoredNameSource, _authoredQuantitySource, _authoredDescriptionSource;
        private Sprite _authoredIcon;
        private Color _authoredIconColor;
        private bool _authoredIconEnabled;
        private string _authoredName, _authoredQuantity, _authoredDescription;
        public InventoryView Owner { get; private set; }
        public ItemInstance Item { get; private set; }

        /// <summary>Вызывается владельцем при привязке/перестройке раскладки; смена адреса отменяет прежний drag.</summary>
        public void Bind(InventoryView owner, int index)
        {
            if (Owner != owner || Index != index) InventoryDragController.Cancel(this);
            Owner = owner;
            Index = index;
            Refresh();
        }

        public void Unbind()
        {
            InventoryDragController.Cancel(this);
            Owner = null;
            Refresh();
        }

        public void Refresh()
        {
            CaptureAuthoredAppearance();
            Item = Owner != null ? Owner.GetItem(Index) : null;
            var settings = Owner != null ? Owner.GetSlotSettings(Index) : null;
            if (Icon != null)
            {
                var configuredIcon = settings?.DisplayIcon;
                Icon.sprite = Item != null ? Item.Definition?.Icon : configuredIcon != null ? configuredIcon : _authoredIcon;
                Icon.enabled = Icon.sprite != null && (Item != null || configuredIcon != null || _authoredIconEnabled);
                Icon.color = Item != null ? ItemColor : configuredIcon != null ? PlaceholderColor : _authoredIconColor;
                Icon.raycastTarget = false;
            }
            var configuredLabel = settings?.DisplayLabel;
            if (NameLabel != null) NameLabel.text = Item != null ? Item.DisplayName
                : !string.IsNullOrEmpty(configuredLabel) ? configuredLabel : _authoredName;
            if (QuantityLabel != null) QuantityLabel.text = Item != null ? Owner.GetQuantityText(Index, Item) : _authoredQuantity;
            if (DescriptionLabel != null) DescriptionLabel.text = Item != null ? Item.Description : _authoredDescription;
            if (_hovered && Owner != null) Owner.ShowHint(Item);
        }

        // Сохраняем авторские значения до первого показа предмета. При замене ссылки UI запоминаем новый компонент.
        private void CaptureAuthoredAppearance()
        {
            if (Icon != null && _authoredIconSource != Icon)
            {
                _authoredIconSource = Icon;
                _authoredIcon = Icon.sprite;
                _authoredIconColor = Icon.color;
                _authoredIconEnabled = Icon.enabled;
            }
            CaptureText(NameLabel, ref _authoredNameSource, ref _authoredName);
            CaptureText(QuantityLabel, ref _authoredQuantitySource, ref _authoredQuantity);
            CaptureText(DescriptionLabel, ref _authoredDescriptionSource, ref _authoredDescription);
        }

        private static void CaptureText(Text label, ref Text source, ref string authored)
        {
            if (label == null || source == label) return;
            source = label;
            authored = label.text;
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) InventoryDragController.Begin(this, data);
        }
        public void OnDrag(PointerEventData data) => InventoryDragController.Move(this, data);
        public void OnEndDrag(PointerEventData data) => InventoryDragController.End(this, data);
        public void OnDrop(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) InventoryDragController.Drop(this, data);
        }

        public void OnPointerEnter(PointerEventData data)
        {
            if (!_hovered && Background != null)
            {
                _backgroundColor = Background.color;
                Background.color = HoverColor;
            }
            _hovered = true;
            if (Owner != null) Owner.ShowHint(Item);
        }

        public void OnPointerExit(PointerEventData data) => ClearHover();
        private void ClearHover()
        {
            if (!_hovered) return;
            _hovered = false;
            if (Background != null) Background.color = _backgroundColor;
            if (Owner != null) Owner.ShowHint(null);
        }

        private void OnDisable()
        {
            InventoryDragController.Cancel(this);
            ClearHover();
        }
        private void OnDestroy() => InventoryDragController.Cancel(this);
    }
}

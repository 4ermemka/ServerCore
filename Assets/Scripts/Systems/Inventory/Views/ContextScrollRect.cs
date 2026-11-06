using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    /// <summary>
    /// EventSystem не прокручивает панель повторно после InputManager.Update.
    /// Перемещение полосы и прокрутка тачем сохраняют стандартное поведение ScrollRect.
    /// Без активного координатора компонент работает как обычный ScrollRect.
    /// </summary>
    public sealed class ContextScrollRect : ScrollRect
    {
        private InventoryInputCoordinator coordinator;
        public override void OnScroll(PointerEventData data)
        {
            if (coordinator == null) coordinator = GetComponentInParent<InventoryInputCoordinator>();
            if (coordinator != null && coordinator.OwnsScroll) return;
            // Самостоятельная панель также не прокручивается во время переноса стопки.
            if (InventoryDragController.IsDragging) return;
            base.OnScroll(data);
        }
        public void ApplyContextScroll(PointerEventData data)
        {
            if (isActiveAndEnabled) base.OnScroll(data);
        }
        protected override void OnTransformParentChanged()
        {
            coordinator = null;
            base.OnTransformParentChanged();
        }
    }
}

using System.Linq;
using Shared.UI;
using UnityEngine;

namespace ServerCore.World
{
    /// <summary>Групповая команда сцены. Каждое корневое окно также имеет собственные Open/Close/Toggle для UnityEvent.</summary>
    public sealed class WorldDemoControls : MonoBehaviour
    {
        [SerializeField] private GameObject inventoryDemo;
        public void ToggleInventory()
        {
            if (inventoryDemo == null) return;
            var roots = inventoryDemo.GetComponentsInChildren<TabPanel>(true)
                .Where(panel => panel.transform.parent == null || panel.transform.parent.GetComponentInParent<TabPanel>(true) == null).ToArray();
            // Сохраняем совместимость сцены с прежним prefab без вкладок.
            if (roots.Length == 0) { inventoryDemo.SetActive(!inventoryDemo.activeSelf); return; }
            bool open = !inventoryDemo.activeSelf || !roots.Any(panel => panel.IsVisible);
            inventoryDemo.SetActive(true);
            foreach (var root in roots) root.SetOpen(open);
        }
        private void OnGUI()
        {
            var input = InputManager.Instance;
            if (input == null) return;
            GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 24),
                $"ЛКМ: перенос   |   {input.GetBinding("Cancel")}: отменить   |   {input.GetBinding("Inventory.Toggle")}: инвентари   |   {input.GetBinding("Settings.Input")}: управление");
        }
    }
}

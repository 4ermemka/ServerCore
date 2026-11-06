using UnityEngine;
using UnityEngine.EventSystems;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Обеспечивает события мыши в отдельной тестовой сцене, создавая EventSystem только при его отсутствии.
    /// Использует StandaloneInputModule (старый Input Manager); в игре с другим вводом настройте свой EventSystem.
    /// Созданный служебный объект является ребёнком Canvas и отключается вместе с ним.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class InventoryCanvas : MonoBehaviour
    {
        private void Awake()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var events = new GameObject("Inventory EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(transform, false);
        }
    }
}

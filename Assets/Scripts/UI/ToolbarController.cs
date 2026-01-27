using UnityEngine;

/// <summary>
/// Пример использования - панель инструментов
/// </summary>
public class ToolbarController : InputHandler
{
    [SerializeField] private GameObject toolbarPanel;
    private bool isVisible = false;

    protected override void SubscribeEvents()
    {
        SubscribeToAction(
            actionName: "ToggleToolbar",
            onPressed: ToggleToolbar,
            onHeld: null,  // Можно подписаться только на нужные
            onReleased: null
        );
    }

    protected override void UnsubscribeEvents()
    {

    }

    void Start()
    {
        toolbarPanel.SetActive(isVisible);
    }

    void ToggleToolbar()
    {
        isVisible = !isVisible;
        toolbarPanel.SetActive(isVisible);
        Debug.Log($"Toolbar {(isVisible ? "shown" : "hidden")}");
    }
}
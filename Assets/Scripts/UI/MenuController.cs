using UnityEngine;

/// <summary>
/// ѕример использовани€ - панель инструментов
/// </summary>
public class MenuController : InputHandler
{
    [SerializeField] private GameObject toolbarPanel;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject compassPanel;
    private bool isVisible = false;

    protected override void SubscribeEvents()
    {
        SubscribeToAction(
            actionName: "ToggleToolbar",
            onPressed: ToggleToolbar,
            onHeld: null,
            onReleased: null
        );

        SubscribeToAction(
            actionName: "ToggleInventory",
            onPressed: ToggleInventory,
            onHeld: null,
            onReleased: null
        );

        SubscribeToAction(
            actionName: "ToggleCompass",
            onPressed: ActiveCompass,
            onHeld: null,
            onReleased: DeactiveCompass
        );
    }

    protected override void UnsubscribeEvents()
    {

    }

    protected void Start()
    {
        toolbarPanel.SetActive(isVisible);
        inventoryPanel.SetActive(isVisible);
        compassPanel.SetActive(false);
    }

    protected void ToggleToolbar()
    {
        toolbarPanel.SetActive(!toolbarPanel.activeSelf);
    }

    protected void ToggleInventory()
    {
        inventoryPanel.SetActive(!inventoryPanel.activeSelf);
    }

    protected void ActiveCompass()
    {
        compassPanel.SetActive(true);
    }

    protected void DeactiveCompass()
    {
        compassPanel.SetActive(false);
    }
}
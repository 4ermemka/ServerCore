namespace ServerCore.Inventory
{
    public partial class InventoryComponent
    {
        // OnValidate только ставит флаг; изменения работающей модели выполняются позже из Update.
        protected virtual void OnValidate() => _configurationDirty = true;
    }
}

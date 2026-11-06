namespace ServerCore.Inventory
{
    public sealed partial class ItemCatalog
    {
        private void OnValidate() => Revision++;
    }
}

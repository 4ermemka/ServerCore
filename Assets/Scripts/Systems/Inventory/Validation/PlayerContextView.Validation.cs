namespace ServerCore.Inventory
{
    public sealed partial class PlayerContextView
    {
        private void OnValidate() => _configurationDirty = true;
    }
}

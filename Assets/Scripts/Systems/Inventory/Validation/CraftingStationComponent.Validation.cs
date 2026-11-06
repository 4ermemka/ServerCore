namespace ServerCore.Inventory
{
    public abstract partial class CraftingStationComponent<TContext>
    {
        protected override void OnValidate()
        {
            base.OnValidate();
            _inspectorDirty = true;
        }
    }
}

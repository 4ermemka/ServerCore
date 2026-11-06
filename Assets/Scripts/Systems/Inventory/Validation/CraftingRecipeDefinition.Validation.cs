using System;

namespace ServerCore.Inventory
{
    public sealed partial class CraftingRecipeDefinition
    {
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                Id = Guid.NewGuid().ToString("N");
        }
    }
}

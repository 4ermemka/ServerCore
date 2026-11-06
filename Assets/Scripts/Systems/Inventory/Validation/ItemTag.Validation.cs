using System;

namespace ServerCore.Inventory
{
    public partial class ItemTag
    {
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                Id = Guid.NewGuid().ToString("N");
        }
    }
}

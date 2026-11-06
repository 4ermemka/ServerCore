using System;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class PlayerBlueprint
    {
        public string Id;
        public string DisplayName;
        public bool Known;
    }
}

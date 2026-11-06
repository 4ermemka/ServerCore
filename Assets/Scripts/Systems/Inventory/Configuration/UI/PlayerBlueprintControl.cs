using System;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class PlayerBlueprintControl
    {
        public string Id;
        public Toggle KnownToggle;
        public Text Label;
    }
}

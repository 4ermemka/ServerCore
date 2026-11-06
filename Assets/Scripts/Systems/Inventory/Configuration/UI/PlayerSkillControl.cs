using System;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class PlayerSkillControl
    {
        public string Id;
        public InputField ValueInput;
        public Text Label;
    }
}

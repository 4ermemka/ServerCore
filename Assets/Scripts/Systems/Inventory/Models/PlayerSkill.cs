using System;
using UnityEngine;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class PlayerSkill
    {
        public string Id;
        public string DisplayName;
        [Min(0)]
        public int Level;
        [Min(0)]
        public int Experience;
        [Min(1)]
        public int ExperiencePerLevel = 100;
    }
}

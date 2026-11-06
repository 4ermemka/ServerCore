using System;
using UnityEngine;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class SkillRequirement
    {
        public string Id;
        [Min(0)]
        public int Level;
    }
}

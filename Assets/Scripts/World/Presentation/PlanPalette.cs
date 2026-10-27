using System.Collections.Generic;
using UnityEngine;

namespace ServerCore.World.Presentation
{
    [CreateAssetMenu(menuName = "World/Plan palette")]
    public sealed class PlanPalette : ScriptableObject
    {
        [SerializeField] private Color[] colors =
        {
            new(0.18f, .78f, 1), new(1, .58f, .16f), new(.65f, .42f, 1), new(.22f, .88f, .52f),
            new(1, .32f, .47f), new(1, .85f, .22f), new(.30f, .48f, 1), new(.98f, .40f, .86f),
            new(.10f, .88f, .82f), new(.72f, .88f, .25f), new(.85f, .62f, .40f), new(.67f, .79f, .96f)
        };
        private readonly List<Color32> expanded = new();
        public int Count => colors.Length;
        public Color Get(int index)
        {
            if (expanded.Count == 0)
                foreach (var color in colors)
                {
                    Color32 value = color;
                    value.a = 255;
                    if (expanded.Contains(value)) throw new System.InvalidOperationException("Plan palette contains repeated colors.");
                    expanded.Add(value);
                }
            // Планы не ограничены длиной палитры. Продолжаем её неповторяющимися RGB-цветами.
            while (expanded.Count <= index)
            {
                int attempt = expanded.Count;
                Color32 value;
                do { value = Color.HSVToRGB((++attempt * .61803398875f) % 1f, .68f, .96f); }
                while (expanded.Contains(value));
                expanded.Add(value);
            }
            return expanded[index];
        }
    }
}

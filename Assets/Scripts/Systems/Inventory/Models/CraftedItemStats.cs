using System;

namespace ServerCore.Inventory
{
    /// <summary>Неизменяемые параметры результата; смена уровня игрока не меняет уже созданный предмет.</summary>
    [Serializable]
    public sealed class CraftedItemStats : IEquatable<CraftedItemStats>
    {
        public int Level { get; }
        public int Quality { get; }

        public CraftedItemStats(int level, int quality)
        {
            Level = level;
            Quality = quality;
        }

        public bool Equals(CraftedItemStats other) => other != null && Level == other.Level && Quality == other.Quality;
        public override bool Equals(object other) => other is CraftedItemStats stats && Equals(stats);
        public override int GetHashCode() => (Level * 397) ^ Quality;
    }
}

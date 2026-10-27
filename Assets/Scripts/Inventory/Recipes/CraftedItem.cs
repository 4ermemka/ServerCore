using System;

namespace ServerCore.Inventory
{
    /// <summary>Неизменяемые параметры результата; смена уровня игрока не меняет уже созданный предмет.</summary>
    [Serializable]
    public sealed class CraftedItemStats : IEquatable<CraftedItemStats>
    {
        public int Level { get; }
        public int Quality { get; }
        public CraftedItemStats(int level, int quality) { Level = level; Quality = quality; }
        public bool Equals(CraftedItemStats other) => other != null && Level == other.Level && Quality == other.Quality;
        public override bool Equals(object other) => other is CraftedItemStats stats && Equals(stats);
        public override int GetHashCode() => (Level * 397) ^ Quality;
    }

    /// <summary>Общий результат с параметрами экземпляра: подходит оружию, еде и другим видам ItemDefinition.</summary>
    public sealed class CraftedItem : ItemInstance<CraftedItemStats>
    {
        protected override bool SupportsStackMerging => true;
        public CraftedItem(ItemDefinition definition, int level, int quality, int quantity = 1)
            : base(definition, new CraftedItemStats(level, quality), quantity) { }
        public override string DisplayName => $"{base.DisplayName} · ур. {Data.Level}";
        public override string Description => $"{base.Description}\nУровень: {Data.Level} · Качество: {Data.Quality}";
    }
}

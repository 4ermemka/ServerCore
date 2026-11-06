namespace ServerCore.Inventory
{

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

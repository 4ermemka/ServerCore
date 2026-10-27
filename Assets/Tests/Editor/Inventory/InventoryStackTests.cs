using Assets.Shared.Systems.InventorySystem.Slots;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using NUnit.Framework;
using UnityEngine;

namespace ServerCore.Inventory.Tests
{
    public class InventoryStackTests
    {
        private ItemDefinition definition;
        [SetUp] public void SetUp()
        {
            definition = ScriptableObject.CreateInstance<ItemDefinition>();
            definition.Id = "stack-test"; definition.MaxStack = 10;
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(definition);

        [Test]
        public void PartialMoveCreatesNewIdentityAndCanMergeBackInsideSameInventory()
        {
            var inventory = new SlottedInventory<ItemInstance>(2);
            inventory.TryAdd(new ItemInstance(definition, 9, "original"), 0);
            Assert.That(inventory.TryMoveTo(inventory, 0, 1, 3).Succeeded, Is.True);
            Assert.That(inventory[0].Quantity, Is.EqualTo(6));
            Assert.That(inventory[0].ItemId, Is.EqualTo("original"));
            Assert.That(inventory[1].Quantity, Is.EqualTo(3));
            Assert.That(inventory[1].ItemId, Is.Not.EqualTo("original"));
            Assert.That(inventory.TryMoveTo(inventory, 1, 0).Succeeded, Is.True);
            Assert.That(inventory[1], Is.Null); Assert.That(inventory[0].Quantity, Is.EqualTo(9));
        }
        [Test]
        public void RulesRejectMergedSnapshotAndNeitherInventoryChanges()
        {
            var a = new SlottedInventory<ItemInstance>(1); var b = new SlottedInventory<ItemInstance>(1);
            a.TryAdd(new ItemInstance(definition, 4)); b.TryAdd(new ItemInstance(definition, 5));
            b.SetRule(0, new SlotRule<ItemInstance>(accepts: item => item.Quantity <= 6));
            var oldA = a[0]; var oldB = b[0];
            Assert.That(a.TryMoveTo(b, 0, 0).Succeeded, Is.False);
            Assert.That(a[0], Is.SameAs(oldA)); Assert.That(b[0], Is.SameAs(oldB));
            Assert.That(a.TryMoveTo(b, 0, 0, 1).Succeeded, Is.True);
            Assert.That(a[0].Quantity + b[0].Quantity, Is.EqualTo(9));
        }
        [Test]
        public void AddIsAllOrNothingAndDuplicateIdentityCannotBeAbsorbed()
        {
            var inventory = new SlottedInventory<ItemInstance>(1);
            inventory.TryAdd(new ItemInstance(definition, 7, "original"));
            Assert.That(inventory.TryAdd(new ItemInstance(definition, 2, "original")).Succeeded, Is.False);
            Assert.That(inventory.TryAdd(new ItemInstance(definition, 4)).Succeeded, Is.False);
            Assert.That(inventory[0].Quantity, Is.EqualTo(7));
            Assert.That(inventory.TryAdd(new ItemInstance(definition, 3)).Succeeded, Is.True);
            Assert.That(inventory[0].Quantity, Is.EqualTo(10)); Assert.That(inventory[0].ItemId, Is.EqualTo("original"));
        }
        [Test]
        public void CraftedParametersDetermineCompatibilityAndPartialSwapIsRejected()
        {
            var inventory = new SlottedInventory<ItemInstance>(2);
            inventory.TryAdd(new CraftedItem(definition, 2, 1, 4), 0);
            inventory.TryAdd(new CraftedItem(definition, 3, 1, 2), 1);
            Assert.That(inventory.TryMoveTo(inventory, 0, 1, 2).Succeeded, Is.False);
            inventory.TryRemove(1); inventory.TryAdd(new CraftedItem(definition, 2, 1, 2), 1);
            Assert.That(inventory.TryMoveTo(inventory, 0, 1, 2).Succeeded, Is.True);
            Assert.That(inventory[1].Quantity, Is.EqualTo(4));
            Assert.That(((CraftedItem)inventory[1]).Data.Level, Is.EqualTo(2));
        }
        [Test]
        public void TransferObserversSeeCommittedTotalsAndActionsReportActualAmount()
        {
            var a = new SlottedInventory<ItemInstance>(1); var b = new SlottedInventory<ItemInstance>(1);
            a.TryAdd(new ItemInstance(definition, 7)); b.TryAdd(new ItemInstance(definition, 9));
            int moved = 0; bool coherent = false;
            a.ActionExecuting += (_, args) => moved = args.Action.Items[0].Quantity;
            a.SlotsChanged += () => coherent = a[0].Quantity == 6 && b[0].Quantity == 10;
            Assert.That(a.TryMoveTo(b, 0, 0).Succeeded, Is.True);
            Assert.That(moved, Is.EqualTo(1)); Assert.That(coherent, Is.True);
        }

        [Test]
        public void UnknownSubclassDoesNotMergeAwayItsAdditionalParameters()
        {
            var a = new CustomItem(definition, 4, 10);
            var b = new CustomItem(definition, 2, 20);
            Assert.That(a.CanStackWith(b), Is.False);
        }
        private sealed class CustomItem : ItemInstance
        {
            public int Durability { get; }
            public CustomItem(ItemDefinition definition, int quantity, int durability) : base(definition, quantity)
                => Durability = durability;
        }
        [Test]
        public void CraftedResultMergesAtomicallyAndFailedDeliveryKeepsIngredients()
        {
            var input = new SlottedInventory<ItemInstance>(1); var target = new SlottedInventory<ItemInstance>(1);
            input.TryAdd(new ItemInstance(definition, 4));
            target.TryAdd(new ItemInstance(definition, 9));
            var recipe = new CraftingRecipe<ItemInstance, object>("recipe", "Recipe",
                new[] { new RecipeIngredient<ItemInstance>(item => item.Is(definition), 1) },
                (_, __) => new ItemInstance(definition, 2));
            using var station = new RecipeCraftingStation<ItemInstance, object>(input, new[] { recipe }, new object());
            var preview = station.Preview;
            Assert.That(station.TryClaim(preview, target, 0).Succeeded, Is.False);
            Assert.That(input[0].Quantity, Is.EqualTo(4)); Assert.That(target[0].Quantity, Is.EqualTo(9));
            target.TryRemove(0); target.TryAdd(new ItemInstance(definition, 8, "target"));
            Assert.That(station.TryClaim(station.Preview, target, 0).Succeeded, Is.True);
            Assert.That(input[0].Quantity, Is.EqualTo(3)); Assert.That(target[0].Quantity, Is.EqualTo(10));
            Assert.That(target[0].ItemId, Is.EqualTo("target"));
        }
        [Test]
        public void CompatibleStacksMergeAndKeepOverflowInSource()
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            definition.Id = "metal"; definition.MaxStack = 10;
            try
            {
                var source = new SlottedInventory<ItemInstance>(1);
                var target = new SlottedInventory<ItemInstance>(1);
                source.TryAdd(new ItemInstance(definition, 7, "source"));
                target.TryAdd(new ItemInstance(definition, 8, "target"));
                Assert.That(source.TryMoveTo(target, 0, 0).Succeeded, Is.True);
                Assert.That(target[0].Quantity, Is.EqualTo(10));
                Assert.That(source[0].Quantity, Is.EqualTo(5));
                Assert.That(target[0].ItemId, Is.EqualTo("target"));
                Assert.That(source[0].ItemId, Is.EqualTo("source"));
            }
            finally { Object.DestroyImmediate(definition); }
        }
    }
}

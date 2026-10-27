using System;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using Assets.Shared.Systems.InventorySystem.Items;
using Assets.Shared.Systems.InventorySystem.Slots;
using NUnit.Framework;

namespace ServerCore.Inventory.Tests
{
    public class InventoryModelTests
    {
        private sealed class Item : ICraftingItem<Item>
        {
            public Item(string name, int quantity = 1, int power = 0, string id = null)
            { Name = name; Quantity = quantity; Power = power; ItemId = id ?? Guid.NewGuid().ToString("N"); }
            public string ItemId { get; }
            public string Name { get; }
            public int Quantity { get; }
            public int Power { get; }
            public Item WithQuantity(int amount) => new Item(Name, amount, Power, ItemId);
        }

        private sealed class Context { public int Level; }

        private static CraftingRecipe<Item, Context> Recipe(string id = "tool", int amount = 2,
            Func<Context, InventoryValidationResult> condition = null)
            => new CraftingRecipe<Item, Context>(id, id,
                new[] { new RecipeIngredient<Item>(x => x.Name == "metal", amount) },
                (items, context) => new Item("tool", power: context.Level + items.Where(x => x != null).Sum(x => x.Power)), condition);

        [Test]
        public void MoveAndSwapKeepIdentityAndPublishOnlyCommittedStates()
        {
            var source = new SlottedInventory<Item>(2);
            var target = new SlottedInventory<Item>(1);
            var metal = new Item("metal");
            var wood = new Item("wood");
            source.TryAdd(metal, 1); target.TryAdd(wood);
            bool sawCommitted = false;
            source.SlotsChanged += () => sawCommitted = ReferenceEquals(source[1], wood) && ReferenceEquals(target[0], metal);
            Assert.That(source.TryMoveTo(target, 1, 0).Succeeded, Is.True);
            Assert.That(sawCommitted, Is.True);
            Assert.That(source[0], Is.Null);
            Assert.That(source[1], Is.SameAs(wood));
            Assert.That(target[0], Is.SameAs(metal));
        }

        [Test]
        public void RejectedSwapRuleAndShrinkingCannotDiscardItems()
        {
            var source = new SlottedInventory<Item>(2);
            var target = new SlottedInventory<Item>(1);
            source.SetRule(1, new SlotRule<Item>("metal", x => x.Name == "metal"));
            var metal = new Item("metal"); var wood = new Item("wood");
            source.TryAdd(metal, 1); target.TryAdd(wood);
            long revision = source.Revision;
            Assert.That(source.TryMoveTo(target, 1, 0).Succeeded, Is.False);
            Assert.That(source.TryResize(1).Succeeded, Is.False);
            Assert.That(source.SetRule(1, new SlotRule<Item>(accepts: x => false)).IsAllowed, Is.False);
            Assert.That(source.Revision, Is.EqualTo(revision));
            Assert.That(source[1], Is.SameAs(metal)); Assert.That(target[0], Is.SameAs(wood));
            Assert.That(source.TryResize(3).Succeeded, Is.True);
            Assert.That(source.Slots.Count, Is.EqualTo(3)); Assert.That(source[2], Is.Null);
        }

        [Test]
        public void AutomaticPlacementSkipsRejectingSlotsAndPreventsDuplicateIdentity()
        {
            var inventory = new SlottedInventory<Item>(2);
            inventory.SetRule(0, new SlotRule<Item>(accepts: x => x.Name == "wood"));
            var item = new Item("metal");
            Assert.That(inventory.TryAdd(item).Succeeded, Is.True);
            Assert.That(inventory[1], Is.SameAs(item));
            Assert.That(inventory.TryAdd(item.WithQuantity(7), 0).Succeeded, Is.False);
            Assert.That(inventory.TryAdd(new Item("metal")).Succeeded, Is.False);
            Assert.That(inventory.TryRemove(-1).Succeeded, Is.False);
        }

        [Test]
        public void MovingWithinInventoryPreservesHolesAndSupportsSwap()
        {
            var inventory = new SlottedInventory<Item>(3);
            var a = new Item("a"); var b = new Item("b");
            inventory.TryAdd(a, 0); inventory.TryAdd(b, 2);
            Assert.That(inventory.TryMoveTo(inventory, 0, 1).Succeeded, Is.True);
            Assert.That(inventory[0], Is.Null);
            Assert.That(inventory.TryMoveTo(inventory, 1, 2).Succeeded, Is.True);
            Assert.That(inventory[1], Is.SameAs(b)); Assert.That(inventory[2], Is.SameAs(a));
        }

        [Test]
        public void ActionRulesVetoEverySwapDirection()
        {
            var source = new SlottedInventory<Item>(1); var target = new SlottedInventory<Item>(1);
            var a = new Item("a"); var b = new Item("b"); source.TryAdd(a); target.TryAdd(b);
            target.AddRule(new DelegateInventoryActionRule<Item>(action => action.Kind == InventoryActionKind.TransferOut
                ? InventoryValidationResult.Deny("bound", "Cannot remove") : InventoryValidationResult.Allow()));
            Assert.That(source.TryMoveTo(target, 0, 0).Succeeded, Is.False);
            Assert.That(source[0], Is.SameAs(a)); Assert.That(target[0], Is.SameAs(b));
        }

        [Test]
        public void ReentrantMutationsAreRejectedAndObserverFaultsDoNotUndoTransfer()
        {
            var source = new SlottedInventory<Item>(1); var target = new SlottedInventory<Item>(1);
            var item = new Item("metal"); source.TryAdd(item);
            int faults = 0; bool rejected = false; bool laterObserver = false;
            source.ObserverFaulted += (_, __) => faults++;
            source.ActionExecuting += (_, __) => { rejected = !source.TryRemove(0).Succeeded; throw new InvalidOperationException("observer"); };
            source.Changed += (_, __, ___) => throw new InvalidOperationException("sync observer");
            source.SlotsChanged += () => throw new InvalidOperationException("slot observer");
            source.SlotsChanged += () => laterObserver = true;
            Assert.That(source.TryMoveTo(target, 0, 0).Succeeded, Is.True);
            Assert.That(rejected && laterObserver, Is.True); Assert.That(faults, Is.EqualTo(1));
            Assert.That(source[0], Is.Null); Assert.That(target[0], Is.SameAs(item));
        }

        [Test]
        public void SnapshotRoundtripPreservesSlotPositionsAndInvalidatesPreview()
        {
            var input = new SlottedInventory<Item>(3); var item = new Item("metal", 4);
            input.TryAdd(item, 2);
            var copy = new SlottedInventory<Item>(0);
            copy.ApplySnapshot(input.CreateSnapshot());
            Assert.That(copy.SlotCount, Is.EqualTo(3)); Assert.That(copy[0], Is.Null);
            Assert.That(copy[2].ItemId, Is.EqualTo(item.ItemId));
            using (var station = new RecipeCraftingStation<Item, Context>(copy, new[] { Recipe() }, new Context()))
            {
                var preview = station.Preview;
                copy.ApplySnapshot(new SlottedInventory<Item>(3).CreateSnapshot());
                Assert.That(station.Preview, Is.Null);
                Assert.That(station.TryClaim(preview, new SlottedInventory<Item>(1)).Succeeded, Is.False);
            }
        }

        [Test]
        public void ClaimConsumesAcrossStacksAndKeepsImmutableRemainderIdentity()
        {
            var input = new SlottedInventory<Item>(3); var target = new SlottedInventory<Item>(1);
            var a = new Item("metal", 2, 3); var b = new Item("metal", 5, 4);
            input.TryAdd(a, 0); input.TryAdd(b, 2);
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe(amount: 4) }, new Context { Level = 10 }))
            {
                var preview = station.Preview;
                Assert.That(preview.Result.Power, Is.EqualTo(17));
                Assert.That(input[0], Is.SameAs(a)); Assert.That(input[2], Is.SameAs(b));
                Assert.That(station.TryClaim(preview, target).Succeeded, Is.True);
                Assert.That(target[0], Is.SameAs(preview.Result));
                Assert.That(input[0], Is.Null); Assert.That(input[2].Quantity, Is.EqualTo(3));
                Assert.That(input[2].ItemId, Is.EqualTo(b.ItemId)); Assert.That(b.Quantity, Is.EqualTo(5));
                Assert.That(station.TryClaim(preview, new SlottedInventory<Item>(1)).Succeeded, Is.False);
            }
        }

        [Test]
        public void OverlappingIngredientsFindAllocationThatGreedyMatchingWouldMiss()
        {
            var input = new SlottedInventory<Item>(2); input.TryAdd(new Item("rare", 2), 0); input.TryAdd(new Item("common", 2), 1);
            var recipe = new CraftingRecipe<Item, Context>("overlap", "overlap", new[] {
                new RecipeIngredient<Item>(x => true, 2), new RecipeIngredient<Item>(x => x.Name == "rare", 2) },
                (_, __) => new Item("result"));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { recipe }, new Context()))
            {
                Assert.That(station.Preview, Is.Not.Null);
                Assert.That(station.TryConfirm().Succeeded, Is.True);
                Assert.That(input.Slots.All(x => x == null), Is.True);
            }
        }

        [Test]
        public void OrderedRecipeRequiresSpecifiedSlotAndListsSeveralMatches()
        {
            var input = new SlottedInventory<Item>(2); input.TryAdd(new Item("metal", 5), 0);
            var ordered = new CraftingRecipe<Item, Context>("ordered", "ordered",
                new[] { new RecipeIngredient<Item>(x => x.Name == "metal", 1, 1) }, (_, __) => new Item("ordered tool"));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe(), ordered }, new Context()))
            {
                Assert.That(station.Previews.Count, Is.EqualTo(1));
                input.TryMoveTo(input, 0, 1);
                Assert.That(station.Previews.Count, Is.EqualTo(2));
                station.SelectRecipe("ordered"); Assert.That(station.Preview.RecipeId, Is.EqualTo("ordered"));
                Assert.That(station.TryConfirm().Succeeded, Is.True);
                Assert.That(station.Output[0].Name, Is.EqualTo("ordered tool"));
                Assert.That(station.Preview, Is.Null);
            }
        }

        [Test]
        public void PreviewIsStableUntilRulesInputOrContextChange()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 10));
            var context = new Context { Level = 2 }; int calls = 0;
            var recipe = new CraftingRecipe<Item, Context>("random", "random",
                new[] { new RecipeIngredient<Item>(x => true) }, (_, c) => new Item("tool", power: ++calls + c.Level));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { recipe }, context))
            {
                var first = station.Preview;
                Assert.That(station.Preview, Is.SameAs(first)); Assert.That(station.Previews[0], Is.SameAs(first));
                Assert.That(calls, Is.EqualTo(1));
                context.Level = 9; station.SetContext(context);
                Assert.That(station.Preview.Result.Power, Is.EqualTo(11));
                Assert.That(station.TryClaim(first, new SlottedInventory<Item>(1)).Succeeded, Is.False);
                var second = station.Preview;
                input.SetRule(0, new SlotRule<Item>("metal", x => true));
                Assert.That(station.Preview, Is.Not.SameAs(second));
            }
        }

        [Test]
        public void FailedDestinationsDoNotConsumeInputsOrRerollPreview()
        {
            var input = new SlottedInventory<Item>(1); var original = new Item("metal", 5); input.TryAdd(original);
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                var preview = station.Preview; var full = new SlottedInventory<Item>(1); var blocker = new Item("stone"); full.TryAdd(blocker);
                var rejecting = new SlottedInventory<Item>(1); rejecting.SetRule(0, new SlotRule<Item>(accepts: x => false));
                Assert.That(station.TryClaim(preview, full, 0).Succeeded, Is.False);
                Assert.That(station.TryClaim(preview, rejecting).Succeeded, Is.False);
                Assert.That(station.TryClaim(preview, input, 0).Succeeded, Is.False);
                Assert.That(input[0], Is.SameAs(original)); Assert.That(full[0], Is.SameAs(blocker));
                Assert.That(station.Preview, Is.SameAs(preview));
            }
        }

        [Test]
        public void CommitRechecksLiveContextConditionAndAllInventoryActionRules()
        {
            var input = new SlottedInventory<Item>(1); var original = new Item("metal", 5); input.TryAdd(original);
            var context = new Context { Level = 3 };
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe(condition: c => c.Level >= 3
                ? InventoryValidationResult.Allow() : InventoryValidationResult.Deny("level", "Level too low")) }, context))
            {
                var preview = station.Preview; context.Level = 0;
                Assert.That(station.TryConfirm().Succeeded, Is.False); Assert.That(input[0], Is.SameAs(original));
                context.Level = 3;
                input.AddRule(new DelegateInventoryActionRule<Item>(_ => InventoryValidationResult.Deny("locked", "Locked")));
                Assert.That(station.TryClaim(preview, new SlottedInventory<Item>(1)).Succeeded, Is.False);
                Assert.That(input[0], Is.SameAs(original));
            }
        }

        [Test]
        public void ConfirmThenMoveRealOutputAllowsNextCraftWithoutDuplication()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 5));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                var first = station.Preview;
                Assert.That(station.TryConfirm().Succeeded, Is.True); Assert.That(input[0].Quantity, Is.EqualTo(3));
                Assert.That(station.TryConfirm().Succeeded, Is.False); Assert.That(station.Previews, Is.Empty);
                Assert.That(station.Output[0], Is.SameAs(first.Result));
                var target = new SlottedInventory<Item>(1);
                Assert.That(station.Output.TryMoveTo(target, 0, 0).Succeeded, Is.True);
                Assert.That(station.Preview, Is.Not.Null);
                Assert.That(station.TryConfirm().Succeeded, Is.True); Assert.That(input[0].Quantity, Is.EqualTo(1));
            }
        }

        [Test]
        public void CraftObserversSeeCommittedParticipantsAndCannotReenter()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 4)); var target = new SlottedInventory<Item>(1);
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                bool rejected = false; bool atomic = false;
                var preview = station.Preview;
                station.ActionExecuting += (_, __) => rejected = !station.TryClaim(preview, target).Succeeded && !input.TryRemove(0).Succeeded;
                input.SlotsChanged += () => { atomic = input[0].Quantity == 2 && ReferenceEquals(target[0], preview.Result); throw new Exception("observer"); };
                station.PreviewChanged += () => throw new Exception("preview observer");
                Assert.That(station.TryClaim(preview, target).Succeeded, Is.True);
                Assert.That(rejected && atomic, Is.True);
            }
        }

        [Test]
        public void InterfaceExecuteUsesSameAtomicCommitAndDisposedStationRejectsClaims()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 4));
            var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context());
            ICraftingStation<Item, CraftCommitRequest<Item>> process = station;
            var target = new SlottedInventory<Item>(1);
            Assert.That(process.TryExecute(new CraftCommitRequest<Item>(station.Preview, target)).Succeeded, Is.True);
            var preview = station.Preview; station.Dispose();
            Assert.That(station.TryClaim(preview, new SlottedInventory<Item>(1)).Succeeded, Is.False);
            Assert.That(input[0].Quantity, Is.EqualTo(2));
        }

        [Test]
        public void OutputOnlyAcceptsRecipeConfirmationAndCannotReceiveSwapBack()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 4));
            var bag = new SlottedInventory<Item>(2); var blocker = new Item("wood"); bag.TryAdd(blocker, 0);
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                Assert.That(station.Output.TryAdd(new Item("stone")).Succeeded, Is.False);
                Assert.That(bag.TryMoveTo(station.Output, 0, 0).Succeeded, Is.False);
                var preview = station.Preview;
                Assert.That(station.TryConfirm().Succeeded, Is.True);
                Assert.That(station.Output.TryMoveTo(bag, 0, 0).Succeeded, Is.False);
                Assert.That(station.Output[0], Is.SameAs(preview.Result)); Assert.That(bag[0], Is.SameAs(blocker));
                Assert.That(station.Output.TryMoveTo(bag, 0, 1).Succeeded, Is.True);
                Assert.That(bag[1], Is.SameAs(preview.Result));
            }
        }

        [Test]
        public void LegacyBaseCallsDuringCommitCannotReenterOrErasePreparedCommit()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 4));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                CraftingStationBase<Item, CraftCommitRequest<Item>> legacy = station;
                var target = new SlottedInventory<Item>(1);
                var request = new CraftCommitRequest<Item>(station.Preview, target);
                bool rejected = false;
                station.ActionExecuting += (_, __) =>
                {
                    legacy.CanExecute(new CraftCommitRequest<Item>(null, target));
                    // The active commit is protected even through a legacy base reference.
                    rejected = !legacy.CanExecute(request).IsAllowed;
                };
                Assert.That(station.TryExecute(request).Succeeded, Is.True);
                Assert.That(rejected, Is.True); Assert.That(input[0].Quantity, Is.EqualTo(2));
            }
        }

        [Test]
        public void LegacyBaseExecutePublishesOneGuardedCraftLifecycle()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 4));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                CraftingStationBase<Item, CraftCommitRequest<Item>> legacy = station;
                int executions = 0; bool rejected = false;
                station.ActionExecuting += (_, __) => { executions++; rejected = !input.TryRemove(0).Succeeded; };
                var request = new CraftCommitRequest<Item>(station.Preview, new SlottedInventory<Item>(1));
                Assert.That(legacy.TryExecute(request).Succeeded, Is.True);
                Assert.That(executions, Is.EqualTo(1)); Assert.That(rejected, Is.True);
                Assert.That(input[0].Quantity, Is.EqualTo(2));
            }
        }

        [Test]
        public void LiveConditionChangedByExecutingObserverRejectsBeforeAnyMutation()
        {
            var input = new SlottedInventory<Item>(1); var original = new Item("metal", 4); input.TryAdd(original);
            var context = new Context { Level = 5 };
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe(condition: c => c.Level >= 5
                ? InventoryValidationResult.Allow() : InventoryValidationResult.Deny("level", "Level too low")) }, context))
            {
                var target = new SlottedInventory<Item>(1);
                station.ActionExecuting += (_, __) => context.Level = 0;
                Assert.That(station.TryClaim(station.Preview, target).Succeeded, Is.False);
                Assert.That(input[0], Is.SameAs(original)); Assert.That(target[0], Is.Null);
            }
        }

        [Test]
        public void ThrowingPredicatesRejectWithoutResourceLoss()
        {
            var source = new SlottedInventory<Item>(1); var target = new SlottedInventory<Item>(1);
            var item = new Item("metal"); source.TryAdd(item);
            target.SetRule(0, new SlotRule<Item>(accepts: _ => throw new InvalidOperationException("rule")));
            Assert.That(source.TryMoveTo(target, 0, 0).Succeeded, Is.False);
            Assert.That(source[0], Is.SameAs(item)); Assert.That(target[0], Is.Null);
            Assert.That(target.TryAdd(item).Succeeded, Is.False);
        }

        [Test]
        public void RecipeSelectionInvalidatesDraggedPreviewAndInvalidSelectionPreservesIt()
        {
            var input = new SlottedInventory<Item>(1); input.TryAdd(new Item("metal", 4));
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe("a"), Recipe("b") }, new Context()))
            {
                var first = station.Preview;
                Assert.That(station.SelectRecipe("missing").IsAllowed, Is.False);
                Assert.That(station.Preview, Is.SameAs(first));
                Assert.That(station.SelectRecipe("b").IsAllowed, Is.True);
                Assert.That(station.Preview.RecipeId, Is.EqualTo("b"));
                Assert.That(station.TryClaim(first, new SlottedInventory<Item>(1)).Succeeded, Is.False);
            }
        }

        [Test]
        public void CanExecuteDoesNotConsumePublishOrRecreatePreview()
        {
            var input = new SlottedInventory<Item>(1); var item = new Item("metal", 4); input.TryAdd(item);
            using (var station = new RecipeCraftingStation<Item, Context>(input, new[] { Recipe() }, new Context()))
            {
                var preview = station.Preview; int events = 0;
                station.ActionExecuting += (_, __) => events++;
                input.SlotsChanged += () => events++;
                var request = new CraftCommitRequest<Item>(preview, new SlottedInventory<Item>(1));
                Assert.That(station.CanExecute(request).IsAllowed, Is.True);
                Assert.That(station.Preview, Is.SameAs(preview)); Assert.That(input[0], Is.SameAs(item));
                Assert.That(events, Is.EqualTo(0));
            }
        }

        [Test]
        public void RejectedConfigurationPreservesCapacityRulesContentsAndRevision()
        {
            var inventory = new SlottedInventory<Item>(2);
            var originalRule = new SlotRule<Item>("original", x => x.Name == "metal");
            inventory.SetRule(0, originalRule);
            var metal = new Item("metal"); var wood = new Item("wood");
            inventory.TryAdd(metal, 0); inventory.TryAdd(wood, 1);
            long revision = inventory.Revision; int notifications = 0;
            inventory.SlotsChanged += () => notifications++;
            var rejecting = new[] {
                new SlotRule<Item>("replacement", x => true),
                new SlotRule<Item>("reject wood", x => x.Name == "metal"), null };
            Assert.That(inventory.TryConfigure(rejecting).IsAllowed, Is.False);
            Assert.That(inventory.SlotCount, Is.EqualTo(2));
            Assert.That(inventory.GetRule(0), Is.SameAs(originalRule)); Assert.That(inventory.GetRule(1), Is.Null);
            Assert.That(inventory[0], Is.SameAs(metal)); Assert.That(inventory[1], Is.SameAs(wood));
            Assert.That(inventory.Revision, Is.EqualTo(revision)); Assert.That(notifications, Is.EqualTo(0));
            Assert.That(inventory.TryConfigure(new SlotRule<Item>[1]).IsAllowed, Is.False);
            Assert.That(inventory.TryConfigure(null).IsAllowed, Is.False);
            Assert.That(inventory.SlotCount, Is.EqualTo(2)); Assert.That(inventory.Revision, Is.EqualTo(revision));
            Assert.That(inventory.GetRule(0), Is.SameAs(originalRule)); Assert.That(notifications, Is.EqualTo(0));
        }

        [Test]
        public void SuccessfulConfigurationCommitsOnceAndRejectsReentrantConfiguration()
        {
            var inventory = new SlottedInventory<Item>(2); var item = new Item("metal"); inventory.TryAdd(item, 1);
            long revision = inventory.Revision; int notifications = 0, actions = 0;
            bool reentrantRejected = false, sawCommitted = false;
            var metalRule = new SlotRule<Item>("metal", x => x.Name == "metal");
            var rules = new[] { new SlotRule<Item>("empty", x => false), metalRule, null };
            inventory.ActionExecuted += (_, __) => actions++;
            inventory.SlotsChanged += () =>
            {
                notifications++;
                sawCommitted = inventory.SlotCount == 3 && ReferenceEquals(inventory.GetRule(1), metalRule) &&
                    ReferenceEquals(inventory[1], item) && inventory.Revision == revision + 1;
                reentrantRejected = !inventory.TryConfigure(new SlotRule<Item>[5]).IsAllowed;
            };
            Assert.That(inventory.TryConfigure(rules).IsAllowed, Is.True);
            Assert.That(sawCommitted && reentrantRejected, Is.True);
            Assert.That(notifications, Is.EqualTo(1)); Assert.That(actions, Is.EqualTo(1));
            Assert.That(inventory.Revision, Is.EqualTo(revision + 1));
            rules[1] = null;
            Assert.That(inventory.GetRule(1), Is.SameAs(metalRule));
            Assert.That(inventory[0], Is.Null); Assert.That(inventory[2], Is.Null);
        }
    }
}

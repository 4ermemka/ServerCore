using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using Assets.Shared.Systems.InventorySystem.Slots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ServerCore.Inventory.Tests
{
    public class RecipeDefinitionTests
    {
        private readonly List<Object> _created = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void AuthoredIngredientAcceptsItemOrTagAndConsumesOnlyItsConfiguredSlot()
        {
            var metal = Item("metal");
            var wood = Item("wood");
            var tag = Asset<ItemTag>(); tag.Id = "fuel"; wood.Tags = new[] { tag };
            var recipe = Recipe(metal, Item("result"));
            recipe.Ingredients[0].Tags = new[] { tag };
            recipe.Ingredients[0].Quantity = 2;
            recipe.Ingredients[0].SlotIndex = 1;
            var inventory = new SlottedInventory<ItemInstance>(2);
            inventory.TryAdd(new ItemInstance(metal, 3), 0);
            inventory.TryAdd(new ItemInstance(wood, 2), 1);
            using var station = new RecipeCraftingStation<ItemInstance, IPlayerContext>(inventory,
                new[] { recipe.Compile() }, Context());
            Assert.That(station.TryConfirm().Succeeded, Is.True);
            Assert.That(inventory[0].Quantity, Is.EqualTo(3));
            Assert.That(inventory[1], Is.Null);
            Assert.That(station.Output[0].Definition, Is.SameAs(recipe.Result));
        }

        [Test]
        public void AnySlotIngredientAggregatesMatchingStacksAndRejectsUnrelatedItems()
        {
            var metal = Item("metal");
            var recipe = Recipe(metal, Item("result")); recipe.Ingredients[0].Quantity = 3;
            var inventory = new SlottedInventory<ItemInstance>(3);
            inventory.TryAdd(new ItemInstance(metal), 0);
            inventory.TryAdd(new ItemInstance(Item("unrelated"), 9), 1);
            using var station = new RecipeCraftingStation<ItemInstance, IPlayerContext>(inventory,
                new[] { recipe.Compile() }, Context());
            Assert.That(station.Preview, Is.Null);
            inventory.TryAdd(new ItemInstance(metal, 2), 2);
            Assert.That(station.TryConfirm().Succeeded, Is.True);
            Assert.That(inventory[1].Quantity, Is.EqualTo(9));
            Assert.That(inventory[0], Is.Null); Assert.That(inventory[2], Is.Null);
        }

        [Test]
        public void BlueprintAndSkillGatesBothApplyAndCompiledSettingsAreSnapshots()
        {
            var recipe = Recipe(Item("input"), Item("output"));
            recipe.RequiredBlueprint = "blueprint";
            recipe.RequiredSkills = new[] { new SkillRequirement { Id = "craft", Level = 2 } };
            var compiled = recipe.Compile();
            var context = Context();
            Assert.That(compiled.Condition(context).IsAllowed, Is.False);
            context.SetBlueprint("blueprint", true);
            Assert.That(compiled.Condition(context).IsAllowed, Is.False);
            context.SetSkillLevel("craft", 2);
            Assert.That(compiled.Condition(context).IsAllowed, Is.True);
            recipe.RequiredSkills[0].Level = 9;
            Assert.That(compiled.Condition(context).IsAllowed, Is.True);
            Assert.That(compiled.Condition(null).IsAllowed, Is.False);
        }

        [Test]
        public void CraftedQuantityLevelAndQualityRemainStableAfterContextChanges()
        {
            var recipe = Recipe(Item("input"), Item("output"));
            recipe.UsePlayerLevel = true; recipe.Quality = 3; recipe.ResultQuantity = 4;
            var context = Context(); context.SetLevel(7);
            var compiled = recipe.Compile();
            var result = (CraftedItem)compiled.CreateResult(Array.Empty<ItemInstance>(), context);
            context.SetLevel(11);
            recipe.Quality = 9; recipe.ResultQuantity = 1;
            Assert.That(result.Quantity, Is.EqualTo(4));
            Assert.That(result.Data.Level, Is.EqualTo(7));
            Assert.That(result.Data.Quality, Is.EqualTo(3));
            var remainder = (CraftedItem)result.WithQuantity(2);
            Assert.That(remainder.Data, Is.SameAs(result.Data));
            Assert.That(remainder.ItemId, Is.EqualTo(result.ItemId));
            Assert.That(result.Quantity, Is.EqualTo(4));
        }

        [Test]
        public void RewardIsAwardedOnceAfterSuccessfulClaimAndUnlocksAnotherRecipe()
        {
            var context = Context();
            context.Skills.Add(new PlayerSkill { Id = "craft", Level = 1, ExperiencePerLevel = 5 });
            var material = Item("input");
            var first = Recipe(material, Item("output")); first.RewardSkill = "craft"; first.RewardExperience = 5;
            var second = Recipe(material, first.Result); second.Id = "advanced";
            second.RequiredSkills = new[] { new SkillRequirement { Id = "craft", Level = 2 } };
            var station = Station(context, first, second);
            station.Model.TryAdd(new ItemInstance(material, 3), 0);
            Assert.That(station.Previews.Count, Is.EqualTo(1));
            var captured = station.Preview;
            var full = new SlottedInventory<ItemInstance>(0);
            Assert.That(station.TryClaim(captured, full).Succeeded, Is.False);
            Assert.That(context.GetSkillLevel("craft"), Is.EqualTo(1));
            var target = new SlottedInventory<ItemInstance>(2);
            Assert.That(station.TryClaim(captured, target).Succeeded, Is.True);
            Assert.That(context.GetSkillLevel("craft"), Is.EqualTo(2));
            Assert.That(station.Previews.Count, Is.EqualTo(2));
            Assert.That(station.TryClaim(captured, target).Succeeded, Is.False);
            Assert.That(context.GetSkillLevel("craft"), Is.EqualTo(2));
            Assert.That(station.TryConfirm().Succeeded, Is.True);
            Assert.That(context.GetSkillLevel("craft"), Is.EqualTo(3));
            Assert.That(station.TryConfirm().Succeeded, Is.False);
            Assert.That(context.GetSkillLevel("craft"), Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RewardFailurePreservesSuccessfulCommitAndIsNotRetried(bool confirmToOutput)
        {
            var material = Item("input");
            var recipe = Recipe(material, Item("output"));
            recipe.RewardSkill = "craft"; recipe.RewardExperience = 5;
            var context = new ThrowingRewardContext();
            var station = Station(null, recipe);
            station.SetContext(context);
            station.Model.TryAdd(new ItemInstance(material), 0);
            var preview = station.Preview;
            var target = confirmToOutput ? station.Output : new SlottedInventory<ItemInstance>(1);
            InventoryOperationResult<ItemInstance> result = null;

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Reward provider failed"));
            Assert.DoesNotThrow(() => result = confirmToOutput
                ? station.TryConfirm() : station.TryClaim(preview, target));

            Assert.That(result.Succeeded, Is.True);
            Assert.That(station.Model[0], Is.Null);
            Assert.That(target[0], Is.SameAs(preview.Result));
            Assert.That(context.AwardAttempts, Is.EqualTo(1));
            Assert.That(station.TryClaim(preview, target).Succeeded, Is.False);
            Assert.That(context.AwardAttempts, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ParentContextWinsDiscoveryAndLaterSourceAssignmentRebindsPreview()
        {
            yield return new EnterPlayMode();
            var parent = Context(); parent.SetLevel(4);
            var explicitSource = Context(); explicitSource.SetLevel(8);
            var material = Item("input");
            var recipe = Recipe(material, Item("output")); recipe.UsePlayerLevel = true;
            var station = Station(null, recipe);
            station.transform.SetParent(parent.transform);
            station.gameObject.SetActive(true);
            station.Model.TryAdd(new ItemInstance(material), 0);
            Assert.That(station.ReadPlayerContext(), Is.SameAs(parent));
            Assert.That(((CraftedItem)station.Preview.Result).Data.Level, Is.EqualTo(4));
            var previous = station.Preview;
            station.ContextSource = explicitSource;
            yield return null; // Настоящий Update станции должен заметить замену источника.
            Assert.That(station.ReadPlayerContext(), Is.SameAs(explicitSource));
            Assert.That(((CraftedItem)station.Preview.Result).Data.Level, Is.EqualTo(8));
            Assert.That(station.TryClaim(previous, new SlottedInventory<ItemInstance>(1)).Succeeded, Is.False);
            parent.SetLevel(12);
            Assert.That(((CraftedItem)station.Preview.Result).Data.Level, Is.EqualTo(8));
        }

        [Test]
        public void AuthoredPlaceholdersReturnAfterAnItemAndHintsDoNotBecomeFilters()
        {
            var item = Item("hint"); var other = Item("other");
            var root = GameObject("view", typeof(RectTransform)); root.SetActive(false);
            var source = root.AddComponent<InventoryComponent>(); source.SlotCount = 1;
            source.Slots = new[] { new SlotSettings { Index = 0, HintItem = item } };
            Assert.That(source.Model.TryAdd(new ItemInstance(other), 0).Succeeded, Is.True);
            source.Model.TryRemove(0);
            source.Slots = Array.Empty<SlotSettings>();
            var view = root.AddComponent<InventoryView>(); view.Source = source; view.AutoGenerate = false;
            view.Content = (RectTransform)root.transform;
            var slotObject = GameObject("slot", typeof(RectTransform)); slotObject.transform.SetParent(root.transform);
            var slot = slotObject.AddComponent<InventorySlotView>();
            slot.NameLabel = GameObject("name", typeof(RectTransform)).AddComponent<Text>();
            slot.NameLabel.text = "Authored empty label";
            slot.DescriptionLabel = GameObject("description", typeof(RectTransform)).AddComponent<Text>();
            slot.DescriptionLabel.text = "Authored empty help";
            var texture = new Texture2D(1, 1); _created.Add(texture);
            var authoredIcon = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero); _created.Add(authoredIcon);
            var overrideIcon = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero); _created.Add(overrideIcon);
            slot.Icon = GameObject("icon", typeof(RectTransform)).AddComponent<Image>();
            slot.Icon.sprite = authoredIcon;
            root.SetActive(true); view.Rebind();
            Assert.That(slot.NameLabel.text, Is.EqualTo("Authored empty label"));
            Assert.That(slot.Icon.sprite, Is.SameAs(authoredIcon));
            source.Model.TryAdd(new ItemInstance(other), 0);
            Assert.That(slot.NameLabel.text, Is.EqualTo("other"));
            source.Model.TryRemove(0);
            Assert.That(slot.NameLabel.text, Is.EqualTo("Authored empty label"));
            Assert.That(slot.DescriptionLabel.text, Is.EqualTo("Authored empty help"));
            Assert.That(slot.Icon.sprite, Is.SameAs(authoredIcon));
            source.Slots = new[] { new SlotSettings { Index = 0, Label = "Explicit override", PlaceholderIcon = overrideIcon } };
            view.Refresh();
            Assert.That(slot.NameLabel.text, Is.EqualTo("Explicit override"));
            Assert.That(slot.Icon.sprite, Is.SameAs(overrideIcon));
        }

        private ConfiguredCraftingStation Station(PlayerContext context, params CraftingRecipeDefinition[] recipes)
        {
            var root = GameObject("station"); root.SetActive(false);
            var station = root.AddComponent<ConfiguredCraftingStation>();
            station.Recipes = recipes; station.ContextSource = context; station.SlotCount = 2;
            return station;
        }

        private sealed class ThrowingRewardContext : IPlayerContext
        {
            public int Level => 1;
            public int AwardAttempts { get; private set; }
            public event Action Changed { add { } remove { } }
            public int GetSkillLevel(string id) => 0;
            public bool HasBlueprint(string id) => false;
            public void AddSkillExperience(string id, int amount)
            {
                AwardAttempts++;
                throw new InvalidOperationException("Reward provider failed");
            }
        }

        private PlayerContext Context() => GameObject("player").AddComponent<PlayerContext>();
        private GameObject GameObject(string name, params Type[] components)
        {
            var value = new GameObject(name, components); _created.Add(value); return value;
        }
        private T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>(); _created.Add(asset); return asset;
        }
        private ItemDefinition Item(string id)
        {
            var item = Asset<ItemDefinition>(); item.Id = id; item.DisplayName = id; return item;
        }
        private CraftingRecipeDefinition Recipe(ItemDefinition input, ItemDefinition output)
        {
            var recipe = Asset<CraftingRecipeDefinition>(); recipe.Id = "recipe"; recipe.DisplayName = "Recipe";
            recipe.Ingredients = new[] { new RecipeInput { Items = new[] { input } } }; recipe.Result = output;
            return recipe;
        }
    }
}

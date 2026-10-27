using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class PlayerContextTestStation : CraftingStationComponent<IPlayerContext>
    {
        public IPlayerContext Source;
        public ItemDefinition Ingredient, Product;
        public RecipeCraftingStation<ItemInstance, IPlayerContext> Core => Station;
        protected override IPlayerContext BuildContext() => Source;
        protected override IEnumerable<CraftingRecipe<ItemInstance, IPlayerContext>> BuildRecipes()
        {
            yield return Recipe("basic", 1);
            yield return Recipe("advanced", 2);
        }
        private CraftingRecipe<ItemInstance, IPlayerContext> Recipe(string id, int skill)
            => new CraftingRecipe<ItemInstance, IPlayerContext>(id, id,
                new[] { new RecipeIngredient<ItemInstance>(item => item.Is(Ingredient)) },
                (_, context) => new ItemInstance<int>(Product, context.Level),
                context => context != null && context.HasBlueprint("tool") && context.GetSkillLevel("craft") >= skill
                    ? InventoryValidationResult.Allow() : InventoryValidationResult.Deny("locked", "Недоступно"));
    }

    public class PlayerContextTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private PlayerContext _player;
        private ItemDefinition _ingredient, _product;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            _player = CreateObject("Игрок").AddComponent<PlayerContext>();
            _player.Skills.Add(new PlayerSkill { Id = "craft", DisplayName = "Ремесло", Level = 1, ExperiencePerLevel = 10 });
            _player.SetBlueprint("tool", true);
            _ingredient = CreateDefinition("material");
            _product = CreateDefinition("tool");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            LogAssert.NoUnexpectedReceived();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void ExperienceCrossesSeveralThresholdsAndNoOpSettersDoNotNotify()
        {
            var changes = 0;
            _player.Changed += () => changes++;
            _player.AddSkillExperience("craft", 25);
            Assert.That(_player.GetSkillLevel("craft"), Is.EqualTo(3));
            Assert.That(_player.Skills[0].Experience, Is.EqualTo(5));
            Assert.That(changes, Is.EqualTo(1));
            _player.SetSkillLevel("craft", 3);
            _player.SetLevel(_player.Level);
            _player.SetBlueprint("tool", true);
            _player.AddSkillExperience("craft", 0);
            Assert.That(changes, Is.EqualTo(1));
            _player.SetSkillLevel("custom:new-skill", 4);
            Assert.That(_player.GetSkillLevel("custom:new-skill"), Is.EqualTo(4));
            Assert.That(_player.GetSkillLevel("unknown"), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => _player.AddSkillExperience("craft", -1));
        }

        [Test]
        public void SkillAndBlueprintChangesImmediatelyRefreshEveryStationEvenWhenDisabled()
        {
            var first = CreateStation();
            var second = CreateStation();
            second.gameObject.SetActive(false);
            Assert.That(first.Previews.Count, Is.EqualTo(1));
            Assert.That(second.Previews.Count, Is.EqualTo(1));
            _player.SetSkillLevel("craft", 2);
            Assert.That(first.Previews.Count, Is.EqualTo(2));
            Assert.That(second.Previews.Count, Is.EqualTo(2));
            _player.SetBlueprint("tool", false);
            Assert.That(first.Previews, Is.Empty);
            Assert.That(second.Previews, Is.Empty);
        }

        [Test]
        public void LiveChangeRejectsCapturedPreviewAndPreservesCommittedResultData()
        {
            var station = CreateStation();
            var stale = station.Preview;
            _player.SetLevel(7);
            var bag = new SlottedInventory<ItemInstance>(2);
            Assert.That(station.TryClaim(stale, bag).Validation.Code, Is.EqualTo("stale_preview"));
            Assert.That(station.Model[0].Quantity, Is.EqualTo(4));
            var current = station.Preview;
            Assert.That(station.TryClaim(current, bag).Succeeded, Is.True);
            _player.SetLevel(12);
            Assert.That(((ItemInstance<int>)bag[0]).Data, Is.EqualTo(7));
            Assert.That(bag[0], Is.SameAs(current.Result));
        }

        [Test]
        public void ExperienceAwardedInsideProtectedCommitUnlocksNextRecipeBeforeReturn()
        {
            var station = CreateStation();
            var target = new SlottedInventory<ItemInstance>(2);
            var faults = 0;
            station.Core.ObserverFaulted += (_, __) => faults++;
            station.Core.ActionExecuted += (_, __) => _player.AddSkillExperience("craft", 10);
            Assert.That(station.TryClaim(station.Preview, target).Succeeded, Is.True);
            Assert.That(faults, Is.Zero);
            Assert.That(_player.GetSkillLevel("craft"), Is.EqualTo(2));
            Assert.That(station.Previews.Select(preview => preview.RecipeId), Does.Contain("advanced"));
        }

        [Test]
        public void ChangesInsideExternalPreviewCallbackFlushAfterProtectedRefresh()
        {
            var station = CreateStation();
            var changed = false;
            var observedLevel = 0;
            station.PreviewChanged += () =>
            {
                if (!changed)
                {
                    changed = true;
                    _player.SetLevel(8);
                }
                observedLevel = ((ItemInstance<int>)station.Preview.Result).Data;
            };
            Assert.That(station.Model.SetRule(0, new SlotRule<ItemInstance>()).IsAllowed, Is.True);
            // Не читаем Preview после операции: проверяем, что именно подписчик уже получил свежие данные.
            Assert.That(observedLevel, Is.EqualTo(8));
        }

        [Test]
        public void NestedContextChangeRefreshesDisabledStationObserversBeforeSetterReturns()
        {
            var station = CreateStation();
            station.gameObject.SetActive(false);
            var changed = false;
            var observedLevel = 0;
            station.PreviewChanged += () =>
            {
                if (!changed)
                {
                    changed = true;
                    _player.SetLevel(8);
                }
                observedLevel = ((ItemInstance<int>)station.Preview.Result).Data;
            };

            _player.SetLevel(2);

            // Getter мог бы сам применить очередь и скрыть ошибку: проверяем только последнее уведомление.
            Assert.That(observedLevel, Is.EqualTo(8));
        }

        [Test]
        public void RepeatedChangesFromPreviewCallbackYieldInsteadOfLoopingAndKeepPendingRefresh()
        {
            var station = CreateStation();
            station.gameObject.SetActive(false);
            var notifications = 0;
            Action callback = () =>
            {
                // Предохранитель теста завершает даже ошибочную неограниченную реализацию.
                if (++notifications < 100) _player.SetLevel(_player.Level + 1);
            };
            station.PreviewChanged += callback;

            _player.SetLevel(2);

            Assert.That(notifications, Is.GreaterThan(1));
            Assert.That(notifications, Is.LessThan(100));
            station.PreviewChanged -= callback;
            Assert.That(((ItemInstance<int>)station.Preview.Result).Data, Is.EqualTo(_player.Level));
        }

        [Test]
        public void ReplacingContextUnsubscribesPreviousProviderAndDestroyUnsubscribesCurrent()
        {
            var station = CreateStation();
            var replacement = CreateObject("Другой игрок").AddComponent<PlayerContext>();
            replacement.SetBlueprint("tool", true);
            replacement.SetSkillLevel("craft", 2);
            station.SetContext(replacement);
            var current = station.Preview;
            var changes = 0;
            station.PreviewChanged += () => changes++;
            _player.SetLevel(20);
            Assert.That(station.Preview, Is.SameAs(current));
            Assert.That(changes, Is.Zero);
            replacement.SetLevel(5);
            Assert.That(((ItemInstance<int>)station.Preview.Result).Data, Is.EqualTo(5));
            Object.DestroyImmediate(station.gameObject);
            var afterDestroy = changes;
            replacement.SetLevel(6);
            Assert.That(changes, Is.EqualTo(afterDestroy));
        }

        [Test]
        public void InspectorValidationNotifiesOnUpdateAndExplicitContextWinsPendingValidation()
        {
            var station = CreateStation();
            _player.Skills[0].Level = 2;
            _player.SendMessage("OnValidate");
            _player.SendMessage("Update");
            Assert.That(station.Previews.Count, Is.EqualTo(2));
            var replacement = CreateObject("Явный контекст").AddComponent<PlayerContext>();
            replacement.SetBlueprint("tool", true);
            replacement.SetSkillLevel("craft", 1);
            station.SendMessage("OnValidate");
            station.SetContext(replacement);
            station.SendMessage("Update");
            Assert.That(station.Previews.Count, Is.EqualTo(1));
        }

        [Test]
        public void DebugViewRefreshUsesSilentControlsAndRebindRemovesPreviousListeners()
        {
            var root = CreateObject("Панель контекста");
            root.SetActive(false);
            var view = root.AddComponent<PlayerContextView>();
            var level = CreateObject("Уровень").AddComponent<InputField>();
            var skill = CreateObject("Навык").AddComponent<InputField>();
            var known = CreateObject("Чертёж").AddComponent<Toggle>();
            view.Source = _player;
            view.LevelInput = level;
            view.Skills = new[] { new PlayerSkillControl { Id = "craft", ValueInput = skill } };
            view.Blueprints = new[] { new PlayerBlueprintControl { Id = "tool", KnownToggle = known } };
            root.SetActive(true);
            view.Rebind();
            var callbacks = 0;
            level.onValueChanged.AddListener(_ => callbacks++);
            skill.onValueChanged.AddListener(_ => callbacks++);
            known.onValueChanged.AddListener(_ => callbacks++);
            _player.SetLevel(9);
            _player.SetSkillLevel("craft", 3);
            _player.SetBlueprint("tool", false);
            Assert.That(level.text, Is.EqualTo("9"));
            Assert.That(skill.text, Is.EqualTo("3"));
            Assert.That(known.isOn, Is.False);
            Assert.That(callbacks, Is.Zero);
            level.onEndEdit.Invoke("11");
            skill.onEndEdit.Invoke("4");
            known.onValueChanged.Invoke(true);
            Assert.That(_player.Level, Is.EqualTo(11));
            Assert.That(_player.GetSkillLevel("craft"), Is.EqualTo(4));
            Assert.That(_player.HasBlueprint("tool"), Is.True);
            var replacement = CreateObject("Новый источник панели").AddComponent<PlayerContext>();
            replacement.SetLevel(15);
            view.Source = replacement;
            view.Rebind();
            _player.SetLevel(20);
            Assert.That(level.text, Is.EqualTo("15"));
            level.onEndEdit.Invoke("16");
            Assert.That(replacement.Level, Is.EqualTo(16));
            Assert.That(_player.Level, Is.EqualTo(20));
            root.SetActive(false);
            level.onEndEdit.Invoke("99");
            Assert.That(replacement.Level, Is.EqualTo(16));
        }

        private PlayerContextTestStation CreateStation()
        {
            var owner = CreateObject("Тестовая станция");
            owner.SetActive(false);
            var station = owner.AddComponent<PlayerContextTestStation>();
            station.Source = _player;
            station.Ingredient = _ingredient;
            station.Product = _product;
            station.SlotCount = 1;
            Assert.That(station.Model.TryAdd(new ItemInstance(_ingredient, 4)).Succeeded, Is.True);
            _ = station.Preview;
            owner.SetActive(true);
            return station;
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name, typeof(RectTransform));
            _objects.Add(result);
            return result;
        }

        private ItemDefinition CreateDefinition(string id)
        {
            var result = ScriptableObject.CreateInstance<ItemDefinition>();
            result.Id = id;
            result.DisplayName = id;
            _objects.Add(result);
            return result;
        }
    }
}

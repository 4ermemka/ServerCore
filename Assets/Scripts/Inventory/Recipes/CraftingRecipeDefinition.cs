using System;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Требование к ингредиенту: конкретный вид ИЛИ любой указанный тег. Пустой фильтр ничего не принимает.
    /// SlotIndex = -1 собирает количество из любых подходящих стопок; иной индекс закрепляет одну ячейку.
    /// </summary>
    [Serializable]
    public sealed class RecipeInput
    {
        public ItemDefinition[] Items = Array.Empty<ItemDefinition>();
        public ItemTag[] Tags = Array.Empty<ItemTag>();
        [Min(1)] public int Quantity = 1;
        [Min(-1)] public int SlotIndex = -1;

        internal RecipeIngredient<ItemInstance> Compile()
        {
            if (SlotIndex < -1) throw new ArgumentOutOfRangeException(nameof(SlotIndex));
            var items = (Items ?? Array.Empty<ItemDefinition>()).Where(item => item != null).ToArray();
            var tags = (Tags ?? Array.Empty<ItemTag>()).Where(tag => tag != null).ToArray();
            return new RecipeIngredient<ItemInstance>(item => item != null &&
                (items.Any(item.Is) || tags.Any(item.HasTag)), Quantity, SlotIndex < 0 ? (int?)null : SlotIndex);
        }
    }

    [Serializable]
    public sealed class SkillRequirement
    {
        public string Id;
        [Min(0)] public int Level;
    }

    /// <summary>
    /// Редактируемый рецепт. Compile копирует настройки в неизменяемое описание для общего ядра;
    /// фабрика preview не расходует ресурсы и не выдаёт опыт. Награда принадлежит подтверждению станции.
    /// </summary>
    [CreateAssetMenu(menuName = "Inventory/Crafting Recipe")]
    public sealed class CraftingRecipeDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public RecipeInput[] Ingredients = Array.Empty<RecipeInput>();
        public ItemDefinition Result;
        [Min(1)] public int ResultQuantity = 1;
        public bool UsePlayerLevel;
        [Min(1)] public int Quality = 1;
        public string RequiredBlueprint;
        public SkillRequirement[] RequiredSkills = Array.Empty<SkillRequirement>();
        public string RewardSkill;
        [Min(0)] public int RewardExperience;

        /// <summary>
        /// Уровень/качество хранятся в отдельном экземпляре CraftedItem. Без особых параметров создаётся ItemInstance.
        /// Ошибочная конфигурация выдаёт исключение при сборке станции, а не молча меняет количество/состав.
        /// </summary>
        public CraftingRecipe<ItemInstance, IPlayerContext> Compile()
        {
            if (Result == null) throw new InvalidOperationException($"Recipe '{name}' needs a result definition.");
            if (string.IsNullOrWhiteSpace(Result.Id)) throw new InvalidOperationException($"Recipe '{name}' result needs a stable ID.");
            if (ResultQuantity < 1 || ResultQuantity > Result.MaxStack) throw new ArgumentOutOfRangeException(nameof(ResultQuantity));
            if (Quality < 1) throw new ArgumentOutOfRangeException(nameof(Quality));
            if (RewardExperience < 0) throw new ArgumentOutOfRangeException(nameof(RewardExperience));
            var ingredients = (Ingredients ?? Array.Empty<RecipeInput>()).Select(input =>
                (input ?? throw new InvalidOperationException($"Recipe '{name}' contains an empty ingredient.")).Compile()).ToArray();
            var skills = (RequiredSkills ?? Array.Empty<SkillRequirement>()).Select(skill =>
            {
                if (skill == null || string.IsNullOrWhiteSpace(skill.Id) || skill.Level < 0)
                    throw new InvalidOperationException($"Recipe '{name}' contains an invalid skill requirement.");
                return new SkillRequirement { Id = skill.Id, Level = skill.Level };
            }).ToArray();
            var result = Result;
            var quantity = ResultQuantity;
            var useLevel = UsePlayerLevel;
            var quality = Quality;
            var blueprint = RequiredBlueprint;
            return new CraftingRecipe<ItemInstance, IPlayerContext>(Id,
                string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName, ingredients,
                (_, context) => useLevel || quality != 1
                    ? new CraftedItem(result, useLevel ? context.Level : 1, quality, quantity)
                    : new ItemInstance(result, quantity),
                context =>
                {
                    if (context == null || context is UnityEngine.Object value && value == null)
                        return InventoryValidationResult.Deny("player_context_missing", "Не найден контекст игрока.");
                    if (!string.IsNullOrWhiteSpace(blueprint) && !context.HasBlueprint(blueprint))
                        return InventoryValidationResult.Deny("recipe_unknown", "Нужен изученный чертёж.");
                    foreach (var skill in skills)
                        if (context.GetSkillLevel(skill.Id) < skill.Level)
                            return InventoryValidationResult.Deny("skill_required", $"Нужен навык {skill.Id} уровня {skill.Level}.");
                    return InventoryValidationResult.Allow();
                });
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        }
    }
}

using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Станция из настроек Inspector: рецепты задаются asset-ами, правила и подсказки — унаследованным Slots.
    /// Все станции используют живой контекст игрока; локальные копии навыков не создаются.
    /// </summary>
    public class ConfiguredCraftingStation : CraftingStationComponent<IPlayerContext>
    {
        public CraftingRecipeDefinition[] Recipes = Array.Empty<CraftingRecipeDefinition>();
        [Tooltip("MonoBehaviour implementing IPlayerContext. Empty: search parents, then this scene.")]
        public MonoBehaviour ContextSource;

        private MonoBehaviour _observedSource;
        private IPlayerContext _discoveredContext;
        private readonly Dictionary<string, Reward> _rewards = new Dictionary<string, Reward>(StringComparer.Ordinal);

        private sealed class Reward
        {
            public string Skill;
            public int Experience;
        }

        /// <summary>Явный источник имеет приоритет; иначе сначала ищется игрок в родителях, затем в этой сцене.</summary>
        public IPlayerContext ReadPlayerContext()
        {
            if (ContextSource != null) return ContextSource as IPlayerContext;
            foreach (var component in GetComponentsInParent<MonoBehaviour>(true))
                if (component is IPlayerContext context) return context;
            if (IsAlive(_discoveredContext) && _discoveredContext is MonoBehaviour cached &&
                cached.gameObject.scene == gameObject.scene) return _discoveredContext;
            var scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(false))
                    if (component is IPlayerContext context && component.isActiveAndEnabled)
                        return _discoveredContext = context;
            _discoveredContext = null;
            return null;
        }

        protected override IPlayerContext BuildContext()
        {
            _observedSource = ContextSource;
            return ReadPlayerContext();
        }

        protected override IEnumerable<CraftingRecipe<ItemInstance, IPlayerContext>> BuildRecipes()
        {
            _rewards.Clear();
            foreach (var definition in Recipes ?? Array.Empty<CraftingRecipeDefinition>())
            {
                if (definition == null) continue;
                var compiled = definition.Compile();
                _rewards.Add(compiled.Id, new Reward { Skill = definition.RewardSkill, Experience = definition.RewardExperience });
                yield return compiled;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!ReferenceEquals(ContextSource, _observedSource)) RefreshContext();
            // Источник может появиться после Awake. Уже назначенный через SetContext интерфейс не заменяем поиском.
            else if (!IsAlive(CurrentContext))
            {
                var discovered = ReadPlayerContext();
                if (IsAlive(discovered)) SetContext(discovered);
            }
        }

        public override InventoryOperationResult<ItemInstance> TryConfirm()
        {
            var context = CurrentContext;
            var recipeId = Preview?.RecipeId;
            var result = base.TryConfirm();
            Award(result, context, recipeId);
            return result;
        }

        public override InventoryOperationResult<ItemInstance> TryClaim(CraftPreview<ItemInstance> preview,
            SlottedInventory<ItemInstance> target, int targetSlot = -1)
        {
            var context = CurrentContext;
            var result = base.TryClaim(preview, target, targetSlot);
            Award(result, context, preview?.RecipeId);
            return result;
        }

        private void Award(InventoryOperationResult<ItemInstance> result, IPlayerContext context, string recipeId)
        {
            // Базовая операция уже отпустила защиту транзакции. Changed безопасно обновляет все станции.
            if (result.Succeeded && IsAlive(context) && recipeId != null && _rewards.TryGetValue(recipeId, out var reward) &&
                !string.IsNullOrWhiteSpace(reward.Skill) && reward.Experience > 0)
            {
                // Сбой внешнего провайдера не отменяет уже выданный предмет и не превращает успех в отказ.
                // Награду не повторяем: провайдер мог изменить опыт до исключения.
                try { context.AddSkillExperience(reward.Skill, reward.Experience); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
        }

        private static bool IsAlive(IPlayerContext context) => context != null &&
            (!(context is UnityEngine.Object value) || value != null);
    }
}

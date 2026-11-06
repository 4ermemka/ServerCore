using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ServerCore.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Миграция примеров на настройки Inspector. Существующие определения, рецепты и внутренние
/// раскладки станций сохраняются. Повторный запуск добавляет только отсутствующую конфигурацию.
/// </summary>
public static class InventoryPrefabBuilder
{
    private const string Prefabs = "Assets/Prefabs/Inventory";
    private const string Recipes = "Assets/Data/Inventory/Recipes";
    private static readonly Color Background = new Color32(20, 30, 40, 246);
    private static readonly Color Cell = new Color32(34, 47, 59, 255);
    private static readonly Color White = new Color32(236, 243, 245, 255);
    private static readonly Color Muted = new Color32(152, 174, 187, 255);
    private static readonly Color Accent = new Color32(112, 220, 187, 255);
    private static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    [MenuItem("Tools/Inventory/Upgrade Sample Workbenches and Debug Panels")]
    public static void Build()
    {
        Directory.CreateDirectory(Recipes);
        AssetDatabase.Refresh();
        var definitions = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>).ToArray();
        var tags = AssetDatabase.FindAssets("t:ItemTag", new[] { "Assets/Data" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ItemTag>).ToArray();
        ItemDefinition Item(string id) => definitions.Single(item => item.Id == id);
        ItemTag Tag(string id) => tags.Single(tag => tag.Id == id);
        var metal = Item("metal"); var gun = Item("gun"); var wood = Tag("wood");
        var vegetables = Item("vegetables"); var soup = Item("soup"); var fuel = Tag("flame_fuel");
        var gunRecipes = new[] {
            Recipe("Gun", "gun", "Оружие · 1 металл + 1 древесина", metal, wood, gun, 1, "crafting", 0, "gun", true, 1),
            Recipe("ReinforcedGun", "reinforced-gun", "Усиленное · 2 металла + 1 древесина", metal, wood, gun, 2, "crafting", 2, "reinforced-gun", true, 2)
        };
        var soupRecipes = new[] {
            Recipe("SimpleSoup", "simple-soup", "Простой овощной суп", vegetables, fuel, soup, 1, "cooking", 1),
            Recipe("GoodSoup", "good-soup", "Хороший овощной суп", vegetables, fuel, soup, 2, "cooking", 2),
            Recipe("ExcellentSoup", "excelent-soup", "Прекрасный овощной суп", vegetables, fuel, soup, 3, "cooking", 3)
        };
        UpgradeStation("GunWorkbench", gunRecipes, metal, wood, gun);
        UpgradeStation("CampfireWorkbench", soupRecipes, vegetables, fuel, soup);
        var catalog = ItemCatalogBuilder.Refresh();
        UpgradeDemo(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("Inventory Editor configuration and debug panels upgraded.");
    }

    private static CraftingRecipeDefinition Recipe(string name, string id, string title, ItemDefinition input,
        ItemTag group, ItemDefinition result, int amount, string skill, int level, string blueprint = "", bool playerLevel = false, int quality = 1)
    {
        var path = Recipes + "/" + name + ".asset";
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipeDefinition>(path);
        if (recipe != null) return recipe; // Пользовательские изменения рецепта не перезаписываются.
        recipe = ScriptableObject.CreateInstance<CraftingRecipeDefinition>();
        recipe.Id = id; recipe.DisplayName = title;
        recipe.Ingredients = new[] {
            new RecipeInput { Items = new[] { input }, Quantity = amount, SlotIndex = 0 },
            new RecipeInput { Tags = new[] { group }, Quantity = 1, SlotIndex = 1 }
        };
        recipe.Result = result; recipe.ResultQuantity = 1;
        recipe.RequiredBlueprint = blueprint;
        recipe.RequiredSkills = level == 0 ? Array.Empty<SkillRequirement>() : new[] { new SkillRequirement { Id = skill, Level = level } };
        recipe.UsePlayerLevel = playerLevel; recipe.Quality = quality;
        recipe.RewardSkill = skill; recipe.RewardExperience = 1;
        AssetDatabase.CreateAsset(recipe, path);
        return recipe;
    }

    private static void UpgradeStation(string name, CraftingRecipeDefinition[] recipes, ItemDefinition input, ItemTag group, ItemDefinition output)
    {
        var path = Prefabs + "/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var station = root.GetComponent<ConfiguredCraftingStation>();
            if (station.Recipes == null || station.Recipes.Length == 0) station.Recipes = recipes;
            if (station.Slots == null || station.Slots.Length == 0)
                station.Slots = new[] {
                    new SlotSettings { Index = 0, HintItem = input, AllowedItems = new[] { input } },
                    new SlotSettings { Index = 1, HintTag = group, AllowedTags = new[] { group } }
                };
            var result = root.GetComponentInChildren<CraftingResultView>(true);
            // Старый скопированный Campfire показывал иконку оружия в выходе; мигрируем подсказку на его продукт.
            if (result != null && (result.ResultSettings.HintItem == null || result.ResultSettings.HintItem.Id == "gun"))
                result.ResultSettings.HintItem = output;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void UpgradeDemo(ItemCatalog catalog)
    {
        var path = Prefabs + "/InventoryDemo.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var player = root.GetComponentInChildren<PlayerContext>(true);
            bool createPanels = player == null;
            if (player == null)
            {
                player = new GameObject("PlayerContext").AddComponent<PlayerContext>();
                player.transform.SetParent(root.transform, false);
                player.InitialLevel = 1;
                player.Skills = new List<PlayerSkill> {
                    new PlayerSkill { Id = "crafting", DisplayName = "Создание оружия", Level = 2, ExperiencePerLevel = 3 },
                    new PlayerSkill { Id = "cooking", DisplayName = "Готовка", Level = 1, ExperiencePerLevel = 1 }
                };
                player.Blueprints = new List<PlayerBlueprint> {
                    new PlayerBlueprint { Id = "gun", DisplayName = "Чертёж оружия", Known = true },
                    new PlayerBlueprint { Id = "reinforced-gun", DisplayName = "Усиленное оружие", Known = true }
                };
            }
            foreach (var station in root.GetComponentsInChildren<ConfiguredCraftingStation>(true))
            {
                station.ContextSource = player;
                PrefabUtility.RecordPrefabInstancePropertyModifications(station);
            }
            if (createPanels)
            {
                root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1680, 1050);
                var bag = root.GetComponentsInChildren<InventoryView>(true).First(view => view.Source != null && !(view.Source is CraftingStationComponent));
                Place((RectTransform)bag.transform, 24, 256);
                Place(root.GetComponentInChildren<GunWorkbench>(true).GetComponent<RectTransform>(), 936, 256);
                Place(root.GetComponentInChildren<CampfireWorkbench>(true).GetComponent<RectTransform>(), 1308, 256);
                BuildCatalog(root.transform, catalog);
                BuildContextPanel(root.transform, player);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void BuildCatalog(Transform parent, ItemCatalog catalog)
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/InventoryPanel.prefab"), parent);
        root.name = "InfiniteCatalog";
        var old = root.GetComponent<InventoryView>();
        var source = old.Source;
        var content = old.Content; var slot = old.SlotPrefab; var title = old.TitleLabel; var count = old.CountLabel; var hint = old.HintLabel;
        UnityEngine.Object.DestroyImmediate(old);
        UnityEngine.Object.DestroyImmediate(source);
        var view = root.AddComponent<InfiniteCatalogView>();
        view.Catalog = catalog; view.Content = content; view.SlotPrefab = slot; view.TitleLabel = title;
        view.CountLabel = count; view.HintLabel = hint; view.AutoGenerate = true; view.QuantityPerDrag = 10;
        title.text = "КАТАЛОГ ПРЕДМЕТОВ";
        hint.text = "Бесконечные образцы · перенос по 10 шт. (до MaxStack)";
        Place((RectTransform)root.transform, 480, 256);
    }

    private static void BuildContextPanel(Transform parent, PlayerContext player)
    {
        var root = Rect(parent, "PlayerContextPanel", 24, 20, 1632, 208);
        Image(root, Background);
        var view = root.gameObject.AddComponent<PlayerContextView>();
        view.Source = player;
        Label(root, "Title", "КОНТЕКСТ ИГРОКА", 24, 18, 450, 30, 24);
        Label(root, "Hint", "Изменения сразу влияют на рецепты и preview. Готовые предметы сохраняют свои параметры.", 24, 158, 1560, 30, 15, Muted);
        Label(root, "LevelLabel", "Уровень игрока", 24, 65, 200, 24, 16);
        view.LevelInput = Input(root, "Level", 24, 96, 168, 38);
        var skills = new List<PlayerSkillControl>();
        for (int i = 0; i < player.Skills.Count; i++)
        {
            var entry = player.Skills[i]; float x = 260 + i * 290;
            var label = Label(root, "SkillLabel " + entry.Id, entry.DisplayName, x, 65, 265, 24, 16);
            skills.Add(new PlayerSkillControl { Id = entry.Id, Label = label, ValueInput = Input(root, "Skill " + entry.Id, x, 96, 168, 38) });
        }
        view.Skills = skills.ToArray();
        var blueprints = new List<PlayerBlueprintControl>();
        for (int i = 0; i < player.Blueprints.Count; i++)
        {
            var entry = player.Blueprints[i]; float x = 930 + i * 330;
            var toggleRoot = Rect(root, "Blueprint " + entry.Id, x, 81, 300, 50);
            var box = Rect(toggleRoot, "Box", 0, 10, 28, 28);
            var background = Image(box, Cell, true);
            var check = Image(Rect(box, "Check", 5, 5, 18, 18), Accent);
            var toggle = toggleRoot.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background; toggle.graphic = check;
            var label = Label(toggleRoot, "Label", entry.DisplayName, 40, 0, 250, 50, 16);
            blueprints.Add(new PlayerBlueprintControl { Id = entry.Id, KnownToggle = toggle, Label = label });
        }
        view.Blueprints = blueprints.ToArray();
    }

    private static InputField Input(Transform parent, string name, float x, float y, float w, float h)
    {
        var root = Rect(parent, name, x, y, w, h);
        var background = Image(root, Cell, true);
        var field = root.gameObject.AddComponent<InputField>();
        field.targetGraphic = background;
        field.textComponent = Label(root, "Value", "1", 10, 0, w - 20, h, 20);
        field.textComponent.alignment = TextAnchor.MiddleLeft;
        field.contentType = InputField.ContentType.IntegerNumber;
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = 5; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static void Place(RectTransform rect, float x, float y)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.localScale = Vector3.one;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
    }

    private static Image Image(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast;
        return image;
    }

    private static Text Label(Transform parent, string name, string text, float x, float y, float w, float h, int size, Color? color = null)
    {
        var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
        label.font = Font; label.text = text; label.fontSize = size; label.color = color ?? White; label.raycastTarget = false;
        return label;
    }
}

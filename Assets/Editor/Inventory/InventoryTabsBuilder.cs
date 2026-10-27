using System;
using System.IO;
using System.Linq;
using ServerCore.Inventory;
using ServerCore.World;
using Shared.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Однократная миграция готовых UI. Сохраняет компоненты, ссылки моделей, рецепты и GUID prefab-ов.</summary>
public static class InventoryTabsBuilder
{
    private const string Folder = "Assets/Prefabs/Inventory/";
    private const string UiFolder = "Assets/Prefabs/UI/";
    private static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    private static TabButton buttonPrefab;

    [MenuItem("Tools/Inventory/Upgrade to procedural tabs")]
    public static void Setup()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory(UiFolder); AssetDatabase.Refresh();
        buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "TabButton.prefab")?.GetComponent<TabButton>();
        if (buttonPrefab == null) buttonPrefab = BuildButton();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "TabPanel.prefab") == null)
        {
            var panel = Rect("TabPanel"); panel.sizeDelta = new Vector2(500, 600);
            Surface(panel.gameObject, "Вкладка", Icon("Def_Metal"));
            PrefabUtility.SaveAsPrefabAsset(panel.gameObject, UiFolder + "TabPanel.prefab"); Object.DestroyImmediate(panel.gameObject);
        }
        Upgrade("InventoryPanel", go => AdaptInventory(go, "Инвентарь", Icon("Def_Caps")));
        Upgrade("GunWorkbench", go => AdaptStation(go, "Оружейная", Icon("Def_Gun")));
        Upgrade("CampfireWorkbench", go => AdaptStation(go, "Костёр", Icon("Def_Soup")));
        Upgrade("InventoryDemo", AdaptDemo);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        var demo = Object.FindObjectsByType<InventoryCanvas>(FindObjectsInactive.Include).FirstOrDefault();
        if (demo == null) demo = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "InventoryDemo.prefab"))).GetComponent<InventoryCanvas>();
        demo.gameObject.SetActive(true);
        var workspace = demo.transform.Find("Workspace");
        if (workspace == null) throw new InvalidOperationException("InventoryDemo должен быть экземпляром обновлённого prefab.");
        foreach (Transform child in workspace) { child.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject); }
        var controls = Object.FindAnyObjectByType<WorldDemoControls>();
        if (controls != null) Set(controls, ("inventoryDemo", demo.gameObject));
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        InventoryInputSetup.Setup();
        Debug.Log("Tabs: prefab-ы мигрированы; I открывает/закрывает корневые окна.");
    }

    private static void Upgrade(string name, Action<GameObject> action)
    {
        var path = Folder + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { action(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Data/Inventory/" + name + ".asset")?.Icon;

    private static TabPanel Surface(GameObject root, string caption, Sprite icon)
    {
        var existing = root.GetComponent<TabPanel>();
        if (existing != null) return existing;
        var children = root.transform.Cast<Transform>().ToArray();
        var body = Rect("Body", root.transform); TabPanel.Stretch(body);
        body.gameObject.AddComponent<TabPageHost>();
        body.gameObject.AddComponent<RectMask2D>();
        foreach (var child in children) child.SetParent(body, false);
        var stripRoot = Rect("Tabs", root.transform); TabPanel.Stretch(stripRoot);
        var scroll = stripRoot.gameObject.AddComponent<ContextScrollRect>();
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
        var viewport = Rect("Viewport", stripRoot); TabPanel.Stretch(viewport);
        Paint(viewport, new Color32(19, 27, 35, 255)); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Buttons", viewport); scroll.viewport = viewport; scroll.content = content;
        var strip = stripRoot.gameObject.AddComponent<TabStrip>(); Set(strip, ("scroll", scroll), ("buttonPrefab", buttonPrefab));
        var facade = root.AddComponent<TabPanel>();
        Set(facade, ("body", body), ("strip", strip)); facade.Caption = caption; facade.Icon = icon;
        stripRoot.gameObject.SetActive(false); // Полоса нужна только когда есть дочерние TabPanel.
        if (root.TryGetComponent<Image>(out var background)) background.raycastTarget = true;
        return facade;
    }
    private static TabButton BuildButton()
    {
        var root = Rect("TabButton"); root.sizeDelta = new Vector2(190, 48);
        var bg = Paint(root, new Color32(38, 51, 63, 255));
        root.gameObject.AddComponent<Button>().targetGraphic = bg;
        var icon = Paint(Rect("Icon", root), Color.white); icon.preserveAspect = true; icon.raycastTarget = false;
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
        icon.rectTransform.pivot = new Vector2(0, .5f); icon.rectTransform.anchoredPosition = new Vector2(10, 0); icon.rectTransform.sizeDelta = new Vector2(28, 28);
        var label = Text(Rect("Text", root), "Вкладка", 17); TabPanel.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(46, 4); label.rectTransform.offsetMax = new Vector2(-8, -4);
        label.alignment = TextAnchor.MiddleLeft;
        var view = root.gameObject.AddComponent<TabButton>(); Set(view, ("icon", icon), ("label", label), ("background", bg));
        var asset = PrefabUtility.SaveAsPrefabAsset(root.gameObject, UiFolder + "TabButton.prefab");
        Object.DestroyImmediate(root.gameObject); return asset.GetComponent<TabButton>();
    }

    private static void AdaptInventory(GameObject root, string caption, Sprite icon)
    {
        if (root.GetComponent<TabPanel>() != null) return;
        var view = root.GetComponent<InventoryView>();
        var panel = Surface(root, caption, icon);
        var scroll = view.Content.GetComponentInParent<ScrollRect>();
        var oldViewport = scroll.viewport; var oldBar = scroll.verticalScrollbar;
        view.Content.SetParent(panel.Body, false);
        Object.DestroyImmediate(scroll);
        Object.DestroyImmediate(oldViewport.gameObject); Object.DestroyImmediate(oldBar.gameObject);
        var content = VerticalScroll(panel.Body);
        // Прокручивается вся панель: даже в низком родительском окне заголовок/подсказка
        // не отнимают фиксированную высоту у viewport и не делают его отрицательным.
        view.TitleLabel.transform.SetParent(content, false); Height(view.TitleLabel.gameObject, 34);
        view.CountLabel.transform.SetParent(content, false); Height(view.CountLabel.gameObject, 24);
        view.Content.SetParent(content, false);
        var fitter = view.Content.GetComponent<ContentSizeFitter>(); if (fitter != null) Object.DestroyImmediate(fitter);
        var oldGrid = view.Content.GetComponent<GridLayoutGroup>(); if (oldGrid != null) Object.DestroyImmediate(oldGrid);
        view.Content.gameObject.AddComponent<AdaptiveGridLayout>();
        view.HintLabel.transform.SetParent(content, false); Height(view.HintLabel.gameObject, 60);
        view.TitleLabel.fontSize = 25; view.HintLabel.fontSize = 16;
        foreach (string name in new[] { "Divider", "Accent" }) { var decoration = panel.Body.Find(name); if (decoration != null) Object.DestroyImmediate(decoration.gameObject); }
        foreach (var text in root.GetComponentsInChildren<Text>(true)) FitText(text);
    }

    private static void AdaptStation(GameObject root, string caption, Sprite icon)
    {
        if (root.GetComponent<TabPanel>() != null) return;
        var panel = Surface(root, caption, icon);
        var children = panel.Body.Cast<Transform>().ToArray();
        var content = VerticalScroll(panel.Body);
        foreach (var child in children) child.SetParent(content, false);
        // Ручная композиция ингредиентов остаётся авторской; сжимается целиком при нехватке ширины.
        var diagram = Rect("RecipeDiagram", content); diagram.gameObject.AddComponent<DesignSizeLayout>();
        var design = Rect("Design", diagram); design.sizeDelta = new Vector2(300, 282);
        foreach (string name in new[] { "Ingredients", "Arrow", "Result" })
        {
            var child = content.Find(name) as RectTransform;
            child.SetParent(design, false); child.anchoredPosition -= new Vector2(24, -120);
        }
        var accent = content.Find("Accent"); if (accent != null) Object.DestroyImmediate(accent.gameObject);
        string[] order = { "Title", "Subtitle", "RecipeDiagram", "Recipe", "Description", "NextRecipe", "Confirm", "Status" };
        float[] heights = { 34, 44, -1, 48, 48, 42, 46, 64 };
        for (int i = 0; i < order.Length; i++)
        {
            var child = content.Find(order[i]); child.SetSiblingIndex(i);
            if (heights[i] > 0) Height(child.gameObject, heights[i]);
        }
        foreach (var text in root.GetComponentsInChildren<Text>(true)) FitText(text);
    }

    private static void AdaptDemo(GameObject root)
    {
        if (root.transform.Find("Workspace") != null) return;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
        var bag = root.GetComponentsInChildren<InventoryView>(true).Single(v => v.Source != null && v.Source is not CraftingStationComponent);
        var catalog = root.GetComponentInChildren<InfiniteCatalogView>(true);
        AdaptInventory(catalog.gameObject, "Каталог", Icon("Def_Metal"));
        var context = root.GetComponentInChildren<PlayerContextView>(true);
        AdaptContext(context);
        var workspace = Rect("Workspace", root.transform); TabPanel.Stretch(workspace);
        workspace.offsetMin = new Vector2(18, 38); workspace.offsetMax = new Vector2(-18, -68);
        workspace.gameObject.AddComponent<ResponsiveColumns>();
        var stores = Rect("InventoryTabs", workspace); var storage = Surface(stores.gameObject, "Хранилища", Icon("Def_Caps"));
        var crafts = Rect("WorkbenchTabs", workspace); var workbenches = Surface(crafts.gameObject, "Мастерские", Icon("Def_Gun"));
        foreach (var page in new[] { bag.GetComponent<TabPanel>(), catalog.GetComponent<TabPanel>(), context.GetComponent<TabPanel>() })
        { page.transform.SetParent(storage.Body, false); TabPanel.Stretch((RectTransform)page.transform); }
        var gun = root.GetComponentInChildren<GunWorkbench>(true);
        var fire = root.GetComponentInChildren<CampfireWorkbench>(true);
        foreach (var page in new[] { gun.GetComponent<TabPanel>(), fire.GetComponent<TabPanel>() })
        { page.transform.SetParent(workbenches.Body, false); TabPanel.Stretch((RectTransform)page.transform); }
        Set(storage, ("initialPage", bag.GetComponent<TabPanel>())); Set(workbenches, ("initialPage", gun.GetComponent<TabPanel>()));
        // Сохраняем overrides размещения nested prefab-ов, а также ссылки на сценовый PlayerContext.
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component != null && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }

    private static void AdaptContext(PlayerContextView view)
    {
        if (view.GetComponent<TabPanel>() != null) return;
        var panel = Surface(view.gameObject, "Игрок", Icon("Def_Caps"));
        var original = panel.Body.Cast<Transform>().ToArray();
        var content = VerticalScroll(panel.Body);
        var title = original.First(t => t.name == "Title"); title.SetParent(content, false); Height(title.gameObject, 36);
        Row("Уровень игрока", view.LevelInput.transform, content);
        foreach (var entry in view.Skills) Row(entry.Label.text, entry.ValueInput.transform, content, entry.Label);
        foreach (var entry in view.Blueprints)
        {
            var rect = (RectTransform)entry.KnownToggle.transform; rect.SetParent(content, false); Height(rect.gameObject, 52);
            var label = entry.Label.rectTransform; TabPanel.Stretch(label); label.offsetMin = new Vector2(42, 0); label.offsetMax = Vector2.zero;
        }
        var hint = original.First(t => t.name == "Hint"); hint.SetParent(content, false); Height(hint.gameObject, 80);
        foreach (var child in original) if (child != null && child.parent == panel.Body) Object.DestroyImmediate(child.gameObject);
        foreach (var text in view.GetComponentsInChildren<Text>(true)) FitText(text);
    }
    private static void Row(string name, Transform input, Transform parent, Text existingLabel = null)
    {
        var root = Rect("Row " + name, parent); Height(root.gameObject, 58);
        var group = root.gameObject.AddComponent<HorizontalLayoutGroup>(); group.spacing = 12; group.childForceExpandWidth = false;
        group.childControlHeight = true; group.childControlWidth = true;
        var label = existingLabel != null ? existingLabel : Text(Rect("Label", root), name, 18);
        label.transform.SetParent(root, false); label.alignment = TextAnchor.MiddleLeft;
        var le = label.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.minWidth = 70;
        input.SetParent(root, false); var inputLayout = input.gameObject.AddComponent<LayoutElement>(); inputLayout.minWidth = inputLayout.preferredWidth = 96;
        if (input.TryGetComponent<InputField>(out var field)) { TabPanel.Stretch(field.textComponent.rectTransform); field.textComponent.rectTransform.offsetMin = new Vector2(10, 0); field.textComponent.rectTransform.offsetMax = new Vector2(-10, 0); }
    }

    private static RectTransform VerticalScroll(RectTransform parent)
    {
        var root = Rect("Scroll", parent); TabPanel.Stretch(root);
        var scroll = root.gameObject.AddComponent<ContextScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 35; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", root); TabPanel.Stretch(viewport); viewport.offsetMax = new Vector2(-12, 0);
        Paint(viewport, new Color32(20, 30, 40, 255)); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(16, 16, 16, 16); layout.spacing = 12;
        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var bar = Rect("Scrollbar", root); bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.offsetMin = new Vector2(-8, 4); bar.offsetMax = new Vector2(-2, -4); Paint(bar, new Color32(34, 47, 59, 255));
        var handle = Rect("Handle", bar); TabPanel.Stretch(handle); var image = Paint(handle, new Color32(112, 220, 187, 255));
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.handleRect = handle; scrollbar.targetGraphic = image;
        scroll.content = content; scroll.viewport = viewport; scroll.verticalScrollbar = scrollbar;
        return content;
    }
    private static RectTransform Rect(string name, Transform parent = null)
    {
        var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); root.gameObject.layer = 5;
        root.SetParent(parent, false); return root;
    }
    private static Image Paint(RectTransform rect, Color color) { var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image; }
    private static Text Text(RectTransform rect, string value, int size)
    {
        var label = rect.gameObject.AddComponent<Text>(); label.font = Font; label.text = value; label.fontSize = size; label.color = new Color32(236, 243, 245, 255); label.raycastTarget = false; FitText(label); return label;
    }
    private static void FitText(Text label) { label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = label.fontSize; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; }
    private static void Height(GameObject root, float height) { var le = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>(); le.minHeight = le.preferredHeight = height; le.flexibleHeight = 0; }
    private static void Top(RectTransform rect, float margin, float top, float height)
    { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.offsetMin = new Vector2(margin, -top - height); rect.offsetMax = new Vector2(-margin, -top); }
    private static void Bottom(RectTransform rect, float margin, float bottom, float height)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, 0); rect.offsetMin = new Vector2(margin, bottom); rect.offsetMax = new Vector2(-margin, bottom + height); }
    private static void Set(Object target, params (string field, Object value)[] fields)
    { var so = new SerializedObject(target); foreach (var field in fields) so.FindProperty(field.field).objectReferenceValue = field.value; so.ApplyModifiedPropertiesWithoutUndo(); }
}

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ServerCore.Inventory;
using Shared.Inputs;
using Shared.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ServerCore.UI.Tests
{
    public sealed class TabSelectionTests
    {
        [Test]
        public void SelectionSurvivesReorderAndFallsBackToNearestAvailableNeighbour()
        {
            var selection = new TabSelection<string>();
            int changed = 0; selection.Changed += _ => changed++;
            selection.SetItems(new[] { "Bag", "Craft", "Skills" });
            Assert.That(selection.Selected, Is.EqualTo("Bag"));
            selection.Select("Craft");
            selection.SetItems(new[] { "Skills", "Bag", "Craft" });
            Assert.That(selection.Selected, Is.EqualTo("Craft"));
            Assert.That(changed, Is.EqualTo(2));
            selection.SetItems(new[] { "Skills", "Bag" });
            Assert.That(selection.Selected, Is.EqualTo("Bag"));
            Assert.That(selection.Select("Absent"), Is.False);
            selection.SetItems(Array.Empty<string>());
            Assert.That(selection.Selected, Is.Null);
        }

        [Test]
        public void InvalidReconfigurationDoesNotPartiallyMutateSelection()
        {
            var selection = new TabSelection<string>(); selection.SetItems(new[] { "A", "B" });
            Assert.Throws<ArgumentException>(() => selection.SetItems(new[] { "A", "A" }));
            Assert.Throws<ArgumentException>(() => selection.SetItems(new[] { "C", null }));
            Assert.That(selection.Items, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(selection.Selected, Is.EqualTo("A"));
        }
    }

    public sealed class TabPanelPlayTests
    {
        private GameObject demo;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            demo = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Inventory/InventoryDemo.prefab"));
            yield return null; yield return null;
            Canvas.ForceUpdateCanvases();
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InventoryDragController.Cancel();
            if (demo != null) Object.Destroy(demo);
            yield return null;
            LogAssert.NoUnexpectedReceived();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SiblingVisibilityNestedTreesReparentingAndClosingPreserveModels()
        {
            var bag = demo.GetComponentsInChildren<InventoryView>().Single(v => v.Source != null && v.Source is not CraftingStationComponent);
            var catalog = demo.GetComponentInChildren<InfiniteCatalogView>();
            var page = bag.GetComponent<TabPanel>(); var root = page.Parent;
            Assert.That(root, Is.Not.Null); Assert.That(root.Children.Count, Is.EqualTo(3));
            Assert.That(root.SelectedPage, Is.SameAs(page));
            Assert.That(page.IsVisible, Is.True); Assert.That(catalog.GetComponent<TabPanel>().IsVisible, Is.False);
            var model = bag.Source.Model; var item = model[0];
            catalog.GetComponent<TabPanel>().Open();
            Assert.That(page.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Assert.That(bag.isActiveAndEnabled, Is.True, "Выбор вкладки не выключает модель/источник drag.");
            catalog.GetComponent<TabPanel>().Close();
            Assert.That(root.SelectedPage, Is.Not.SameAs(catalog.GetComponent<TabPanel>()));
            page.Open(); root.Close(); root.Open();
            Assert.That(root.SelectedPage, Is.SameAs(page)); Assert.That(bag.Source.Model, Is.SameAs(model)); Assert.That(model[0], Is.SameAs(item));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/TabPanel.prefab");
            var nested = Object.Instantiate(prefab, page.Body).GetComponent<TabPanel>(); nested.Caption = "Сумка";
            var leaf = Object.Instantiate(prefab, nested.Body).GetComponent<TabPanel>(); leaf.Caption = "Карман";
            yield return null;
            leaf.Open(); Assert.That(leaf.Parent, Is.SameAs(nested)); Assert.That(nested.Parent, Is.SameAs(page));
            Assert.That(leaf.IsVisible, Is.True);
            var right = demo.GetComponentInChildren<GunWorkbench>().GetComponent<TabPanel>().Parent;
            nested.AttachTo(right); nested.Open();
            Assert.That(nested.Parent, Is.SameAs(right)); Assert.That(page.Children, Has.No.Member(nested));
            Assert.That(leaf.IsVisible, Is.True);
            Assert.Throws<ArgumentException>(() => right.AttachTo(leaf));
            Object.Destroy(nested.gameObject); yield return null; yield return null;
            right.RefreshHierarchy(); Assert.That(right.SelectedPage, Is.Not.Null); Assert.That(right.Children.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator HoveringTabDuringDragTransfersBetweenSiblingsAndHiddenPageCannotReceiveRaycasts()
        {
            var catalog = demo.GetComponentInChildren<InfiniteCatalogView>();
            var bag = demo.GetComponentsInChildren<InventoryView>().Single(v => v.Source != null && v.Source is not CraftingStationComponent);
            var catalogPage = catalog.GetComponent<TabPanel>(); var bagPage = bag.GetComponent<TabPanel>();
            catalogPage.Open(); Canvas.ForceUpdateCanvases();
            var source = catalog.Slots.First(slot => slot.Item.Definition.Id == "metal");
            var data = new PointerEventData(EventSystem.current) { pointerDrag = source.gameObject, dragging = true, button = PointerEventData.InputButton.Left, position = Center(source.transform) };
            ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.beginDragHandler);
            Assert.That(InventoryDragController.IsDragging, Is.True);
            var tab = bagPage.Parent.GetComponentsInChildren<TabButton>().Single(button => button.Page == bagPage);
            ExecuteEvents.Execute(tab.gameObject, data, ExecuteEvents.pointerEnterHandler);
            Assert.That(bagPage.IsVisible, Is.True); Assert.That(catalogPage.IsVisible, Is.False);
            Assert.That(InventoryDragController.IsDragging, Is.True);
            Canvas.ForceUpdateCanvases();
            var target = bag.Slots[3];
            var scroll = target.GetComponentInParent<ScrollRect>();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, target.transform);
            float offset = bounds.min.y < scroll.viewport.rect.yMin ? scroll.viewport.rect.yMin - bounds.min.y
                : bounds.max.y > scroll.viewport.rect.yMax ? scroll.viewport.rect.yMax - bounds.max.y : 0;
            scroll.content.anchoredPosition += new Vector2(0, offset);
            Canvas.ForceUpdateCanvases(); data.position = Center(target.transform);
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits[0].gameObject.GetComponentInParent<InventorySlotView>(), Is.SameAs(target));
            ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.dropHandler);
            Assert.That(bag.Source.Model[3].Definition.Id, Is.EqualTo("metal"));
            Assert.That(InventoryDragController.IsDragging, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RootCanBeControlledByGameActionAndDoesNotStealInactiveInputFocus()
        {
            var player = demo.GetComponentInChildren<PlayerContextView>();
            var page = player.GetComponent<TabPanel>(); page.Open();
            EventSystem.current.SetSelectedGameObject(player.LevelInput.gameObject);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(player.LevelInput.gameObject));
            var root = page.Parent; var model = demo.GetComponentInChildren<GunWorkbench>().Model;
            var router = new GameActionRouter(new[] { new GameActionDefinition("Window.Toggle", "Окно", ActionTrigger.Press, new InputBinding(KeyCode.I)) });
            using var token = router.GetAction("Window.Toggle").Subscribe(root.Toggle);
            var source = new InputSource(); router.Tick(source);
            Assert.That(root.gameObject.activeSelf, Is.False); Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
            router.Tick(source);
            Assert.That(root.IsVisible, Is.True); Assert.That(demo.GetComponentInChildren<GunWorkbench>().Model, Is.SameAs(model));
            yield return null;
        }

        [UnityTest]
        public IEnumerator LayoutFitsLandscapePortraitEmbeddedParentsAndAllTabSides()
        {
            foreach (var size in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080), new Vector2Int(800, 1100), new Vector2Int(640, 480) })
                yield return CaptureAndCheck(size.x, size.y);
            demo.GetComponentInChildren<PlayerContextView>().GetComponent<TabPanel>().Open();
            demo.GetComponentInChildren<CampfireWorkbench>().GetComponent<TabPanel>().Open();
            yield return CaptureAndCheck(1280, 800, "-context-campfire");
            demo.GetComponentInChildren<GunWorkbench>().GetComponent<TabPanel>().Open();
            demo.GetComponentsInChildren<InventoryView>().Single(v => v.Source != null && v.Source is not CraftingStationComponent).GetComponent<TabPanel>().Open();
            var root = demo.GetComponentsInChildren<TabPanel>().First(panel => panel.Parent == null);
            // Реальный маленький родитель с собственным масштабом, независимо от размеров Canvas.
            var embedded = new GameObject("Embedded host", typeof(RectTransform)).GetComponent<RectTransform>();
            embedded.SetParent(demo.transform, false); embedded.sizeDelta = new Vector2(340, 260); embedded.localScale = Vector3.one * .75f;
            root.transform.SetParent(embedded, false); TabPanel.Stretch((RectTransform)root.transform); root.RefreshHierarchy();
            yield return null; Canvas.ForceUpdateCanvases();
            AssertInside((RectTransform)root.transform, embedded);
            foreach (var scroll in root.SelectedPage.GetComponentsInChildren<ScrollRect>())
                Assert.That(scroll.viewport.rect.height, Is.GreaterThan(0));
            foreach (TabSide side in Enum.GetValues(typeof(TabSide)))
            {
                root.Side = side; root.RefreshHierarchy(); yield return null; Canvas.ForceUpdateCanvases();
                AssertInside(root.Body, (RectTransform)root.transform);
                var strip = root.GetComponentInChildren<TabStrip>();
                AssertInside((RectTransform)strip.transform, (RectTransform)root.transform);
                var bodyBounds = Bounds(root.Body, root.transform); var stripBounds = Bounds((RectTransform)strip.transform, root.transform);
                Assert.That(bodyBounds.Overlaps(stripBounds), Is.False, side.ToString());
            }
            // Много закладок: полоса остаётся в родителе, выбранная последняя доступна через scroll.
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/TabPanel.prefab");
            for (int i = 0; i < 16; i++) Object.Instantiate(template, root.Body).GetComponent<TabPanel>().Caption = "Дополнительная " + i;
            root.Side = TabSide.Top; root.RefreshHierarchy(); root.Children.Last().Open();
            yield return null; Canvas.ForceUpdateCanvases();
            var tabs = root.GetComponentInChildren<TabStrip>();
            Assert.That(tabs.Scroll.content.rect.width, Is.GreaterThan(tabs.Scroll.viewport.rect.width));
            Assert.That(tabs.Buttons.Last().Page.IsVisible, Is.True);
            AssertInside((RectTransform)tabs.Buttons.Last().transform, tabs.Scroll.viewport);
        }

        private IEnumerator CaptureAndCheck(int width, int height, string suffix = "")
        {
            var canvas = demo.GetComponent<Canvas>();
            var go = new GameObject("Preview", typeof(Camera)); var camera = go.GetComponent<Camera>();
            camera.orthographic = true; camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(10, 16, 24, 255);
            var target = new RenderTexture(width, height, 24); var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var previousPipeline = GraphicsSettings.defaultRenderPipeline;
            var previousQualityPipeline = QualitySettings.renderPipeline;
            try
            {
                // Обычный UI — ScreenSpaceOverlay. В offscreen-камере отключаем pipeline,
                // чтобы общий toon/palette renderer игры не менял цвета и шрифты UI preview.
                GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                foreach (var graphic in demo.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
                for (int i = 0; i < 4; i++) { yield return null; Canvas.ForceUpdateCanvases(); }
                var workspace = (RectTransform)demo.transform.Find("Workspace");
                foreach (var root in workspace.GetComponentsInChildren<TabPanel>().Where(p => p.Parent == null))
                {
                    AssertInside((RectTransform)root.transform, workspace);
                    AssertInside(root.Body, (RectTransform)root.transform);
                    AssertInside((RectTransform)root.SelectedPage.transform, root.Body);
                }
                var bag = demo.GetComponentsInChildren<InventoryView>().Single(v => v.Source != null && v.Source is not CraftingStationComponent);
                var grid = bag.Content.GetComponent<AdaptiveGridLayout>();
                Assert.That(grid.Columns, Is.GreaterThanOrEqualTo(1));
                foreach (var slot in bag.Slots) AssertInside((RectTransform)slot.transform, bag.Content);
                foreach (var scroll in demo.GetComponentsInChildren<ScrollRect>())
                    if (scroll.verticalScrollbar != null) AssertInside(scroll.verticalScrollbar.handleRect, (RectTransform)scroll.verticalScrollbar.transform);
                camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                string directory = Environment.GetEnvironmentVariable("TABS_PREVIEW_FOLDER");
                if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, $"tabs-{width}x{height}{suffix}.png"), texture.EncodeToPNG()); }
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; camera.targetTexture = null; RenderTexture.active = previous;
                GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQualityPipeline;
                Object.Destroy(go); Object.Destroy(target); Object.Destroy(texture);
            }
        }
        private static Vector2 Center(Transform transform)
        { var rect = (RectTransform)transform; return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)); }
        private static Rect Bounds(RectTransform rect, Transform relativeTo)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var min = relativeTo.InverseTransformPoint(corners[0]); var max = relativeTo.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static void AssertInside(RectTransform child, RectTransform parent)
        {
            var rect = Bounds(child, parent); var area = parent.rect;
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(area.xMin - 1), child.name + " left");
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(area.xMax + 1), child.name + " right");
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(area.yMin - 1), child.name + " bottom");
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(area.yMax + 1), child.name + " top");
        }
        private sealed class InputSource : IInputSource
        {
            public bool AnyHeld => true;
            public InputSample Read(InputBinding binding) => new(true, true);
            public bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding) { binding = default; return false; }
        }
    }

    public sealed class TabSceneTests
    {
        [UnityTest]
        public IEnumerator InventoryActionOpensAndClosesConfiguredSceneRoots()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            // Изолированный тест этой UI-системы: стартовый экран сети проверяется отдельно.
            GameObject.Find("Game Session")?.SetActive(false);
            var manager = Object.FindAnyObjectByType<InputManager>();
            var serialized = new SerializedObject(manager); serialized.FindProperty("saveBindings").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            yield return new EnterPlayMode(); yield return null;
            var roots = Object.FindObjectsByType<TabPanel>(FindObjectsInactive.Include)
                .Where(panel => panel.transform.parent == null || panel.transform.parent.GetComponentInParent<TabPanel>(true) == null).ToArray();
            Assert.That(roots.Length, Is.EqualTo(2)); Assert.That(roots.All(root => !root.gameObject.activeSelf), Is.True);
            var source = new ToggleSource();
            InputManager.Instance.Router.Tick(source); source.Pressed = true;
            InputManager.Instance.Router.Tick(source);
            Assert.That(roots.All(root => root.IsVisible), Is.True, "Inventory.Toggle → WorldDemoControls → TabPanel.Open");
            var bag = Object.FindObjectsByType<InventoryView>().Single(view => view.Source != null && view.Source is not CraftingStationComponent);
            var model = bag.Source.Model;
            InputManager.Instance.Router.Tick(source); Assert.That(roots.All(root => !root.gameObject.activeSelf), Is.True);
            InputManager.Instance.Router.Tick(source); Assert.That(bag.Source.Model, Is.SameAs(model));
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }
        [UnityTearDown]
        public IEnumerator TearDown() { if (Application.isPlaying) yield return new ExitPlayMode(); }
        private sealed class ToggleSource : IInputSource
        {
            public bool Pressed;
            public bool AnyHeld => Pressed;
            public InputSample Read(InputBinding binding) => binding.Key == KeyCode.I ? new InputSample(Pressed, Pressed) : default;
            public bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding) { binding = default; return false; }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Shared.UI;
using Shared.Inputs;

namespace ServerCore.Inventory.Tests
{
    public class InventoryPrefabTests
    {
        private GameObject _root;
        private InventoryView _bag, _inputs;
        private CraftingResultView _result;
        private GunWorkbench _station;
        private CraftingControls _controls;
        private PlayerContext _player;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Inventory/InventoryDemo.prefab");
            Assert.That(prefab, Is.Not.Null, "Run Tools/Inventory/Upgrade Sample Workbenches and Debug Panels first.");
            Directory.CreateDirectory("Logs/Inventory");
            var layout = new List<string>();
            foreach (var rect in prefab.GetComponentsInChildren<RectTransform>(true))
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rect, out string guid, out long id);
                layout.Add($"{id}\t{rect.name}\t{rect.anchoredPosition}\t{rect.sizeDelta}");
            }
            File.WriteAllLines("Logs/Inventory/prefab-layout.tsv", layout);
            _root = Object.Instantiate(prefab);
            yield return null;
            Canvas.ForceUpdateCanvases();
            _station = _root.GetComponentInChildren<GunWorkbench>();
            _bag = _root.GetComponentsInChildren<InventoryView>().Single(view => view.Source != null && !(view.Source is CraftingStationComponent));
            _inputs = _station.GetComponent<InventoryView>();
            _result = _station.GetComponentInChildren<CraftingResultView>();
            _controls = _station.GetComponentInChildren<CraftingControls>();
            _player = _root.GetComponentInChildren<PlayerContext>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InventoryDragController.Cancel();
            if (_root != null) Object.Destroy(_root);
            yield return null;
            LogAssert.NoUnexpectedReceived();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator HiddenDragOwnerIsCancelledWhileOtherRootRemainsOpen()
        {
            var coordinator = _root.GetComponent<InventoryInputCoordinator>();
            var router = new GameActionRouter(new[] { new GameActionDefinition("zoom", "Zoom", ActionTrigger.Hold, InputBinding.Wheel(false)) });
            router.Contexts.SetRoot(new WheelRoot(router, () => { }));
            coordinator.Configure(router);
            _bag.GetComponent<TabPanel>().Open();
            _inputs.GetComponent<TabPanel>().Open();
            yield return null;
            var data = new PointerEventData(EventSystem.current) { pointerId = -1, button = PointerEventData.InputButton.Left };
            _bag.Slots[0].OnBeginDrag(data);
            Assert.That(InventoryDragController.IsDragging, Is.True);
            var root = _bag.GetComponent<TabPanel>();
            while (root.Parent != null) root = root.Parent;
            root.enabled = false;
            Assert.That(InventoryDragController.IsDragging, Is.False);
            Assert.That(router.Contexts.Current.Previous, Is.SameAs(router.Contexts.Root));
            root.enabled = true;
            coordinator.Configure(null); router.Contexts.Dispose();
        }

        [UnityTest]
        public IEnumerator TwoGroupsShareOneInventoryContext()
        {
            var coordinator = _root.GetComponent<InventoryInputCoordinator>();
            var router = new GameActionRouter(new[] { new GameActionDefinition("zoom", "Zoom", ActionTrigger.Hold, InputBinding.Wheel(false)) });
            router.Contexts.SetRoot(new WheelRoot(router, () => { }));
            coordinator.Configure(router);
            _bag.GetComponent<TabPanel>().Open();
            var another = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Inventory/InventoryDemo.prefab"));
            try
            {
                another.GetComponent<InventoryInputCoordinator>().Configure(router);
                foreach (var panel in another.GetComponentsInChildren<TabPanel>()) if (panel.Parent == null) panel.Open();
                yield return null;
                Assert.That(router.Contexts.Current.Previous, Is.SameAs(router.Contexts.Root),
                    "Multiple prefab groups must not create duplicate inventory contexts.");
                var otherBag = another.GetComponentsInChildren<InventoryView>().Single(view =>
                    view.Source != null && !(view.Source is CraftingStationComponent));
                otherBag.GetComponent<TabPanel>().Open();
                otherBag.BoundModel.TryResize(80);
                yield return null; Canvas.ForceUpdateCanvases();
                coordinator.PointerPosition = () => SlotCenter(otherBag.Slots[0]);
                var otherScroll = otherBag.Slots[0].GetComponentInParent<ContextScrollRect>();
                otherScroll.verticalNormalizedPosition = 1;
                router.Tick(new WheelSource { Delta = Vector2.down });
                Assert.That(otherScroll.verticalNormalizedPosition, Is.LessThan(1),
                    "The shared context must route scrolling into either prefab group.");
                Object.Destroy(another); another = null;
                yield return null;
                Assert.That(router.Contexts.Current.Previous, Is.SameAs(router.Contexts.Root));
            }
            finally
            {
                if (another != null) Object.Destroy(another);
                coordinator.Configure(null); router.Contexts.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator DemoOwnsInputContextsAndScrollRouting()
        {
            var coordinator = _root.GetComponent<InventoryInputCoordinator>();
            Assert.That(coordinator, Is.Not.Null,
                "InventoryDemo must own the window and drag input contexts.");
            var router = new GameActionRouter(new[] {
                new GameActionDefinition("zoom", "Zoom", ActionTrigger.Hold, InputBinding.Wheel(false))
            });
            int zooms = 0;
            var rootContext = new WheelRoot(router, () => zooms++);
            router.Contexts.SetRoot(rootContext);
            coordinator.Configure(router);
            _bag.GetComponent<TabPanel>().Open();
            _bag.BoundModel.TryResize(80);
            yield return null; Canvas.ForceUpdateCanvases();
            coordinator.PointerPosition = () => SlotCenter(_bag.Slots[0]);
            var scroll = _bag.Slots[0].GetComponentInParent<ContextScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 1;
            var input = new WheelSource { Delta = new Vector2(0, -1) };
            router.Tick(input);
            Assert.That(zooms, Is.Zero);
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(1));
            var position = scroll.verticalNormalizedPosition;
            var data = new PointerEventData(EventSystem.current) {
                pointerId = -1, button = PointerEventData.InputButton.Left,
                position = SlotCenter(_bag.Slots[0]), scrollDelta = input.Delta
            };
            scroll.OnScroll(data); // EventSystem доставляет тот же сигнал после InputManager.
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(position).Within(.0001f));
            _bag.Slots[0].OnBeginDrag(data);
            Assert.That(router.Contexts.Current.Previous.Previous, Is.SameAs(rootContext));
            Assert.That(InventoryDragController.SelectedQuantity, Is.EqualTo(4));
            input.Delta = new Vector2(0, -2); router.Tick(input);
            Assert.That(InventoryDragController.SelectedQuantity, Is.EqualTo(2));
            input.Delta = new Vector2(0, 20); router.Tick(input);
            Assert.That(InventoryDragController.SelectedQuantity, Is.EqualTo(4));
            input.Delta = new Vector2(0, -20); router.Tick(input);
            Assert.That(InventoryDragController.SelectedQuantity, Is.EqualTo(1));
            input.Delta = new Vector2(0, .5f); router.Tick(input); router.Tick(input);
            Assert.That(InventoryDragController.SelectedQuantity, Is.EqualTo(2));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(position).Within(.0001f));
            Assert.That(_bag.BoundModel[0].Quantity, Is.EqualTo(4));
            string oldId = _bag.BoundModel[0].ItemId;
            _bag.Slots[15].OnDrop(data);
            Assert.That(_bag.BoundModel[0].Quantity, Is.EqualTo(2));
            Assert.That(_bag.BoundModel[15].Quantity, Is.EqualTo(2));
            Assert.That(_bag.BoundModel[15].ItemId, Is.Not.EqualTo(oldId));
            Assert.That(router.Contexts.Current.Previous, Is.SameAs(rootContext));
            _bag.Slots[0].OnBeginDrag(data);
            foreach (var panel in _root.GetComponentsInChildren<TabPanel>().Where(p => p.Parent == null).ToArray()) panel.Close();
            Assert.That(InventoryDragController.IsDragging, Is.False);
            Assert.That(router.Contexts.Current, Is.SameAs(rootContext));
            input.Delta = Vector2.down; // zoom привязан к направлению вниз; предыдущая проверка использовала вверх.
            router.Tick(input);
            Assert.That(zooms, Is.EqualTo(1));
            coordinator.Configure(null); router.Contexts.Dispose();
        }

        private static Vector2 SlotCenter(InventorySlotView slot)
        {
            var rect = (RectTransform)slot.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
        private sealed class WheelSource : IInputSource, IScrollInputSource
        {
            public Vector2 Delta;
            public Vector2 ScrollDelta => Delta;
            public bool AnyHeld => Delta != Vector2.zero;
            public InputSample Read(InputBinding binding) => binding == InputBinding.Wheel(Delta.y > 0) && Delta.y != 0
                ? new InputSample(true, true) : default;
            public bool TryGetPressed(System.Func<InputBinding, bool> accept, out InputBinding binding)
            { binding = default; return false; }
        }
        private sealed class WheelRoot : InputContext
        {
            public WheelRoot(GameActionRouter source, System.Action zoom) : base(source, null)
            { Actions.Add(new InputAction("zoom", zoom)); }
        }

        [UnityTest]
        public IEnumerator PrefabPreviewChoiceDragAndConfirmationConsumeOnlyOnSuccess()
        {
            Assert.That(_bag.Slots.Count, Is.EqualTo(16));
            Assert.That(_inputs.Slots.Count, Is.EqualTo(2));
            Assert.That(_inputs.AutoGenerate, Is.False);
            Assert.That(_inputs.Slots[1].Icon.sprite, Is.SameAs(_station.Slots[1].HintTag.Icon));
            Assert.That(_inputs.Slots[1].NameLabel.text, Is.EqualTo("Древесина"));
            Assert.That(_controls.ConfirmButton.interactable, Is.False);
            AssertScrollbarFits();
            Capture("prefab-start.png");

            var caps = _bag.BoundModel[2];
            Drag(_bag.Slots[0], _inputs.Slots[0]);
            Drag(_bag.Slots[1], _inputs.Slots[1]);
            Assert.That(_station.Model[0].Quantity, Is.EqualTo(4));
            Assert.That(_station.Model[1].Quantity, Is.EqualTo(4));
            Assert.That(_station.Output[0], Is.Null);
            Assert.That(_station.Previews.Count, Is.EqualTo(2));
            Assert.That(_controls.NextRecipeButton.interactable, Is.True);
            _controls.NextRecipeButton.onClick.Invoke();
            Assert.That(_station.Preview.RecipeId, Is.EqualTo("reinforced-gun"));
            var preview = _station.Preview;
            Assert.That(((CraftedItem)preview.Result).Data.Quality, Is.EqualTo(2));
            Assert.That(_controls.DescriptionLabel.text, Does.Contain("Качество: 2"));
            Capture("prefab-preview.png");

            Drag(_result.Slots[0], null); // Cancelled preview must remain hypothetical.
            Assert.That(_station.Preview, Is.SameAs(preview));
            Assert.That(_station.Model[0].Quantity, Is.EqualTo(4));
            Drag(_result.Slots[0], _bag.Slots[2]); // Occupied currency slot.
            Assert.That(_bag.BoundModel[2], Is.SameAs(caps));
            Assert.That(_station.Model[0].Quantity, Is.EqualTo(4));
            Assert.That(_station.Preview, Is.SameAs(preview));

            Drag(_result.Slots[0], _bag.Slots[15]);
            Assert.That(_bag.BoundModel[15], Is.SameAs(preview.Result));
            Assert.That(_station.Model[0].Quantity, Is.EqualTo(2));
            Assert.That(_station.Model[1].Quantity, Is.EqualTo(3));
            Assert.That(_station.Output[0], Is.Null);

            _controls.ConfirmButton.onClick.Invoke();
            var confirmed = _station.Output[0];
            Assert.That(confirmed, Is.Not.Null);
            Assert.That(_station.Model[0], Is.Null);
            Assert.That(_station.Model[1].Quantity, Is.EqualTo(2));
            Assert.That(_controls.ConfirmButton.interactable, Is.False);
            Drag(_result.Slots[0], _bag.Slots[2]);
            Assert.That(_station.Output[0], Is.SameAs(confirmed));
            Assert.That(_bag.BoundModel[2], Is.SameAs(caps));
            Drag(_result.Slots[0], _bag.Slots[14]);
            Assert.That(_bag.BoundModel[14], Is.SameAs(confirmed));
            Assert.That(_station.Output[0], Is.Null);
            Assert.That(_station.Model[1].Quantity, Is.EqualTo(2));
            Drag(_inputs.Slots[1], _bag.Slots[0]);
            Assert.That(_bag.BoundModel[0].Quantity, Is.EqualTo(2));
            Drag(_bag.Slots[15], _bag.Slots[2]);
            Assert.That(_bag.BoundModel[15], Is.SameAs(caps));
            Assert.That(caps.Quantity, Is.EqualTo(25));
            Capture("prefab-crafted.png");
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewTaggedItemContextStalenessAndLayoutsNeedNoEnumChanges()
        {
            var left = ((RectTransform)_inputs.Slots[0].transform).anchoredPosition;
            var right = ((RectTransform)_inputs.Slots[1].transform).anchoredPosition;
            var revision = _bag.BoundModel.Revision;
            var previousRule = _bag.BoundModel.GetRule(1);
            _bag.Source.SlotCount = 19;
            _bag.Source.Slots = new[] { new SlotSettings { Index = 1, AllowedItems = new[] { _station.Slots[0].HintItem } } };
            Assert.That(_bag.Source.RefreshConfiguration().IsAllowed, Is.False);
            Assert.That(_bag.BoundModel.SlotCount, Is.EqualTo(16));
            Assert.That(_bag.BoundModel.Revision, Is.EqualTo(revision));
            Assert.That(_bag.BoundModel.GetRule(1), Is.SameAs(previousRule));
            _bag.Source.SlotCount = 16;
            _bag.Source.Slots = System.Array.Empty<SlotSettings>();
            Assert.That(_bag.Source.Model.TryResize(7).Succeeded, Is.True);
            Assert.That(_bag.Slots.Count, Is.EqualTo(7));
            Assert.That(_bag.Content.GetComponentsInChildren<InventorySlotView>().Length, Is.EqualTo(7));
            Assert.That(_bag.Source.Model.TryResize(19).Succeeded, Is.True);
            Assert.That(_bag.Slots.Count, Is.EqualTo(19));
            Canvas.ForceUpdateCanvases();
            var scroll = _bag.Content.GetComponentInParent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(_bag.Content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            AssertScrollbarFits();

            var branch = ScriptableObject.CreateInstance<ItemDefinition>();
            branch.Id = "test-branch";
            branch.DisplayName = "Ветка";
            branch.Icon = _station.Slots[1].HintTag.Icon;
            branch.Tags = new[] { _station.Slots[1].HintTag };
            var item = new ItemInstance(branch, 2);
            Assert.That(_bag.BoundModel.TryAdd(item, 3).Succeeded, Is.True);
            Drag(_bag.Slots[0], _inputs.Slots[0]);
            Drag(_bag.Slots[3], _inputs.Slots[1]);
            Assert.That(_station.Preview, Is.Not.Null);
            Assert.That(_station.Model[1], Is.SameAs(item));
            Assert.That(((RectTransform)_inputs.Slots[0].transform).anchoredPosition, Is.EqualTo(left));
            Assert.That(((RectTransform)_inputs.Slots[1].transform).anchoredPosition, Is.EqualTo(right));
            _player.SetLevel(9);
            Assert.That(((CraftedItem)_station.Preview.Result).Data.Level, Is.EqualTo(9));
            _station.SendMessage("OnValidate");
            _player.SetLevel(10);
            yield return null;
            Assert.That(((CraftedItem)_station.Preview.Result).Data.Level, Is.EqualTo(10), "An explicit context must survive pending Inspector validation.");

            var data = Begin(_result.Slots[0]);
            _player.SetLevel(12);
            Drop(_result.Slots[0], _bag.Slots[18], data);
            Assert.That(_bag.BoundModel[18], Is.Null);
            Assert.That(_station.Model[0].Quantity, Is.EqualTo(4));
            Assert.That(_station.Model[1].Quantity, Is.EqualTo(2));
            Assert.That(((CraftedItem)_station.Preview.Result).Data.Level, Is.EqualTo(12));
            _player.SetBlueprint("gun", false);
            _player.SetBlueprint("reinforced-gun", false);
            Assert.That(_station.Preview, Is.Null);
            Assert.That(_controls.ConfirmButton.interactable, Is.False);
            Assert.That(_station.Model[0].Quantity, Is.EqualTo(4));

            Begin(_bag.Slots[1]);
            Assert.That(InventoryDragController.IsDragging, Is.True);
            _root.SetActive(false);
            Assert.That(InventoryDragController.IsDragging, Is.False);
            _root.SetActive(true);
            yield return null;
            Assert.That(_bag.Slots.Count, Is.EqualTo(19));
            Object.Destroy(branch);
        }

        [UnityTest]
        public IEnumerator LivePanelAndInfiniteCatalogUnlockCampfireRecipesAfterCraft()
        {
            var catalog = _root.GetComponentInChildren<InfiniteCatalogView>();
            var campfire = _root.GetComponentInChildren<CampfireWorkbench>();
            var input = campfire.GetComponent<InventoryView>();
            var result = campfire.GetComponentInChildren<CraftingResultView>();
            var contextView = _root.GetComponentInChildren<PlayerContextView>();
            Assert.That(catalog.Slots.Count, Is.EqualTo(catalog.Catalog.Items.Length));
            Assert.That(catalog.Catalog.Items.Select(item => item.Id), Does.Contain("coal"));
            Assert.That(catalog.Catalog.Items.Select(item => item.Id), Does.Contain("soup"));
            var vegetables = catalog.Slots.Single(slot => slot.Item.Definition.Id == "vegetables");
            var coal = catalog.Slots.Single(slot => slot.Item.Definition.Id == "coal");
            var coalTemplate = coal.Item;
            Drag(vegetables, input.Slots.Single(slot => slot.Index == 0));
            Drag(coal, input.Slots.Single(slot => slot.Index == 1));
            Assert.That(coal.Item, Is.SameAs(coalTemplate));
            Assert.That(campfire.Model[1].Quantity, Is.EqualTo(10));
            Assert.That(campfire.Previews.Count, Is.EqualTo(1));
            Assert.That(campfire.Preview.RecipeId, Is.EqualTo("simple-soup"));
            Drag(result.Slots[0], _bag.Slots[10]);
            Assert.That(_bag.BoundModel[10].Definition.Id, Is.EqualTo("soup"));
            Assert.That(_player.GetSkillLevel("cooking"), Is.EqualTo(2));
            Assert.That(campfire.Previews.Select(preview => preview.RecipeId), Does.Contain("good-soup"));
            Assert.That(campfire.Model[0].Quantity, Is.EqualTo(9));
            Assert.That(campfire.Model[1].Quantity, Is.EqualTo(9));

            contextView.Skills.Single(row => row.Id == "cooking").ValueInput.onEndEdit.Invoke("3");
            Assert.That(campfire.Previews.Select(preview => preview.RecipeId), Does.Contain("excelent-soup"));
            contextView.Skills.Single(row => row.Id == "cooking").ValueInput.onEndEdit.Invoke("0");
            Assert.That(campfire.Preview, Is.Null);
            Assert.That(campfire.Model[0].Quantity, Is.EqualTo(9));
            contextView.Skills.Single(row => row.Id == "cooking").ValueInput.onEndEdit.Invoke("2");
            Assert.That(campfire.Preview, Is.Not.Null);

            Drag(_bag.Slots[0], _inputs.Slots[0]);
            Drag(_bag.Slots[1], _inputs.Slots[1]);
            contextView.LevelInput.onEndEdit.Invoke("8");
            Assert.That(((CraftedItem)_station.Preview.Result).Data.Level, Is.EqualTo(8));
            contextView.Blueprints.Single(row => row.Id == "gun").KnownToggle.isOn = false;
            Assert.That(_station.Previews.Select(preview => preview.RecipeId), Does.Not.Contain("gun"));
            contextView.Blueprints.Single(row => row.Id == "reinforced-gun").KnownToggle.isOn = false;
            Assert.That(_station.Preview, Is.Null);
            contextView.Blueprints.Single(row => row.Id == "gun").KnownToggle.isOn = true;
            Assert.That(_station.Preview, Is.Not.Null);
            _controls.ConfirmButton.onClick.Invoke();
            var completed = (CraftedItem)_station.Output[0];
            contextView.LevelInput.onEndEdit.Invoke("15");
            Assert.That(completed.Data.Level, Is.EqualTo(8), "Existing crafted output must keep its original stats.");
            Capture("editor-inventory-live.png");
            yield return null;
        }

        private static PointerEventData Begin(InventorySlotView source)
        {
            Reveal(source);
            var data = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                pointerDrag = source.gameObject,
                position = Center(source)
            };
            ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.beginDragHandler);
            return data;
        }

        private static void Drag(InventorySlotView source, InventorySlotView target)
        {
            var data = Begin(source);
            if (target != null) Drop(source, target, data);
            else ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(InventoryDragController.IsDragging, Is.False);
        }

        private static void Drop(InventorySlotView source, InventorySlotView target, PointerEventData data)
        {
            Reveal(target);
            Canvas.ForceUpdateCanvases();
            data.position = Center(target);
            ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.dragHandler);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponentInParent<InventorySlotView>(), Is.SameAs(target));
            ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.endDragHandler);
        }

        private static Vector2 Center(InventorySlotView slot)
        {
            var rect = (RectTransform)slot.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        private static void Reveal(InventorySlotView slot)
        {
            slot.GetComponentInParent<TabPanel>()?.Open();
            Canvas.ForceUpdateCanvases();
            var scroll = slot.GetComponentInParent<ScrollRect>();
            if (scroll == null) return;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, slot.transform);
            float delta = bounds.min.y < scroll.viewport.rect.yMin ? scroll.viewport.rect.yMin - bounds.min.y
                : bounds.max.y > scroll.viewport.rect.yMax ? scroll.viewport.rect.yMax - bounds.max.y : 0;
            scroll.content.anchoredPosition += new Vector2(0, delta);
            Canvas.ForceUpdateCanvases();
        }

        private void AssertScrollbarFits()
        {
            Canvas.ForceUpdateCanvases();
            var scroll = _bag.Content.GetComponentInParent<ScrollRect>();
            var bar = scroll.verticalScrollbar;
            Assert.That(bar.handleRect.rect.height, Is.LessThanOrEqualTo(((RectTransform)bar.transform).rect.height + .01f));
        }

        private void Capture(string filename)
        {
            var canvas = _root.GetComponent<Canvas>();
            var cameraRoot = new GameObject("Capture", typeof(Camera));
            var camera = cameraRoot.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(42, 51, 60, 255);
            camera.orthographic = true;
            camera.cullingMask = 1 << 5;
            var target = new RenderTexture(1680, 1050, 24);
            var texture = new Texture2D(1680, 1050, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var scale = canvas.scaleFactor;
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                canvas.scaleFactor = 1;
                foreach (var label in canvas.GetComponentsInChildren<Text>()) label.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1680, 1050), 0, 0);
                texture.Apply();
                Directory.CreateDirectory("Logs/Inventory");
                File.WriteAllBytes(Path.Combine("Logs/Inventory", filename), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.scaleFactor = scale;
                camera.targetTexture = null;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraRoot);
                Canvas.ForceUpdateCanvases();
            }
        }
    }
}

using System;
using System.IO;
using ServerCore.Input;
using ServerCore.World;
using Shared.Inputs;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ServerCore.Editor.Input
{
    /// <summary>Создаёт обычные редактируемые asset/prefab-ы. Повторно существующий дизайн не перезаписывает.</summary>
    public static class InputSettingsBuilder
    {
        public const string ProfilePath = "Assets/Data/Input/MainInputProfile.asset";
        public const string MenuPath = "Assets/Prefabs/Input/InputSettingsMenu.prefab";
        public const string RowPath = "Assets/Prefabs/Input/InputBindingRow.prefab";
        private static readonly Color Background = new Color32(24, 31, 40, 255);
        private static readonly Color ButtonColor = new Color32(48, 82, 99, 255);

        [MenuItem("Tools/Input/Set up input actions and settings")]
        public static void Setup()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Data/Input");
            Directory.CreateDirectory("Assets/Prefabs/Input");
            AssetDatabase.Refresh();
            var profile = AssetDatabase.LoadAssetAtPath<GameInputProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<GameInputProfile>();
                profile.Configure("servercore.main", new[]
                {
                    Action("Cancel", "Отменить действие", KeyCode.Escape, "Общее"),
                    new GameActionDefinition("Camera.RotateLeft", "Орбита по часовой", ActionTrigger.Hold, new InputBinding(KeyCode.Q), "Камера"),
                    new GameActionDefinition("Camera.RotateRight", "Орбита против часовой", ActionTrigger.Hold, new InputBinding(KeyCode.E), "Камера"),
                    Action("Inventory.Toggle", "Инвентари", KeyCode.I, "Интерфейс"),
                    Action("Settings.Input", "Настройки управления", KeyCode.F1, "Интерфейс")
                });
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            var row = AssetDatabase.LoadAssetAtPath<GameObject>(RowPath);
            if (row == null) row = BuildRow();
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
            if (menu == null) menu = BuildMenu(row.GetComponent<InputBindingRowView>());

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            // OpenScene выгружает ещё не используемые assets; локальная C#-ссылка не
            // гарантирует сохранность native ScriptableObject после этого перехода.
            profile = AssetDatabase.LoadAssetAtPath<GameInputProfile>(ProfilePath);
            menu = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
            if (profile == null || menu == null) throw new InvalidOperationException("Не удалось загрузить созданные Input assets.");
            var manager = UnityEngine.Object.FindAnyObjectByType<InputManager>();
            if (manager == null) throw new InvalidOperationException("InputManager отсутствует в Test.unity.");
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("profile").objectReferenceValue = profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var existingMenu = UnityEngine.Object.FindAnyObjectByType<InputSettingsView>(FindObjectsInactive.Include);
            if (existingMenu == null) existingMenu = ((GameObject)PrefabUtility.InstantiatePrefab(menu)).GetComponentInChildren<InputSettingsView>(true);
            if (manager.GetComponent<InputUiGate>() == null) manager.gameObject.AddComponent<InputUiGate>();
            AddListener(manager.gameObject, "Settings.Input", existingMenu.Open);
            var worldDemo = UnityEngine.Object.FindAnyObjectByType<WorldDemoControls>();
            if (worldDemo != null) AddListener(manager.gameObject, "Inventory.Toggle", worldDemo.ToggleInventory);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Input: профиль, прокручиваемое меню и игровые действия подключены к Test.unity.");
        }

        private static GameActionDefinition Action(string id, string name, KeyCode key, string category)
            => new(id, name, ActionTrigger.Press, new InputBinding(key), category);

        private static void AddListener(GameObject target, string id, UnityEngine.Events.UnityAction callback)
        {
            foreach (var existing in target.GetComponents<GameActionListener>())
                if (new SerializedObject(existing).FindProperty("actionId").stringValue == id) return;
            var listener = target.AddComponent<GameActionListener>();
            var so = new SerializedObject(listener);
            so.FindProperty("actionId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            var field = typeof(GameActionListener).GetField("onPerformed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            UnityEventTools.AddPersistentListener((UnityEngine.Events.UnityEvent)field.GetValue(listener), callback);
            EditorUtility.SetDirty(listener);
        }

        private static GameObject BuildRow()
        {
            var root = Rect("InputBindingRow");
            root.gameObject.AddComponent<Image>().color = new Color32(34, 43, 54, 255);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 68;
            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 12, 10, 10); layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
            var label = Label("Action", root, "Действие", 19);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var mode = Label("Mode", root, "Нажатие", 16);
            mode.color = new Color32(164, 184, 202, 255);
            mode.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
            var rebind = Button("Rebind", root, "Назначить", 200);
            var clear = Button("Clear", root, "Снять", 85);
            var view = root.gameObject.AddComponent<InputBindingRowView>();
            SetReferences(view, ("actionLabel", label), ("modeLabel", mode),
                ("bindingLabel", rebind.GetComponentInChildren<Text>()), ("rebindButton", rebind), ("clearButton", clear));
            var asset = PrefabUtility.SaveAsPrefabAsset(root.gameObject, RowPath);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return asset;
        }

        private static GameObject BuildMenu(InputBindingRowView row)
        {
            var root = Rect("InputSettingsMenu");
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            root.gameObject.AddComponent<GraphicRaycaster>();
            // Доступ к настройкам остаётся даже после снятия привязки Settings.Input.
            var launcher = Button("OpenSettings", root, "Управление", 180);
            var launcherRect = launcher.GetComponent<RectTransform>();
            launcherRect.anchorMin = launcherRect.anchorMax = new Vector2(1, 1);
            launcherRect.pivot = new Vector2(1, 1); launcherRect.anchoredPosition = new Vector2(-16, -16);
            launcherRect.sizeDelta = new Vector2(180, 40);
            var dialog = Rect("Dialog", root); Stretch(dialog); dialog.gameObject.SetActive(false);
            var dim = Rect("Backdrop", dialog); Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            var panel = Rect("Panel", dialog);
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f); panel.sizeDelta = new Vector2(1080, 650);
            panel.gameObject.AddComponent<Image>().color = Background;
            var title = Label("Title", panel, "Управление", 34);
            Place(title.rectTransform, 28, 24, 1024, 46);
            var subtitle = Label("Subtitle", panel, "Выберите действие и нажмите новую клавишу", 18);
            subtitle.color = new Color32(164, 184, 202, 255); Place(subtitle.rectTransform, 28, 74, 1024, 30);

            var scrollRoot = Rect("Scroll", panel);
            scrollRoot.anchorMin = Vector2.zero; scrollRoot.anchorMax = Vector2.one;
            scrollRoot.offsetMin = new Vector2(28, 150); scrollRoot.offsetMax = new Vector2(-28, -122);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("Viewport", scrollRoot); Stretch(viewport); viewport.offsetMax = new Vector2(-20, 0);
            viewport.gameObject.AddComponent<Image>().color = new Color32(21, 28, 35, 255);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = Vector2.zero;
            var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8; list.childControlWidth = true; list.childControlHeight = true;
            list.childForceExpandHeight = false; list.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bar = Rect("Scrollbar", scrollRoot);
            bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1, .5f); bar.sizeDelta = new Vector2(12, 0);
            bar.gameObject.AddComponent<Image>().color = new Color32(42, 53, 65, 255);
            var handle = Rect("Handle", bar); Stretch(handle);
            handle.gameObject.AddComponent<Image>().color = new Color32(105, 170, 186, 255);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false;
            scroll.verticalScrollbar = scrollbar; scroll.scrollSensitivity = 30;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var status = Label("Status", panel, "", 17);
            Place(status.rectTransform, 28, 516, 1024, 52);
            var footer = Rect("Buttons", panel); Place(footer, 28, 586, 1024, 42);
            var buttons = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttons.spacing = 12; buttons.childAlignment = TextAnchor.MiddleRight;
            buttons.childControlHeight = true; buttons.childControlWidth = true;
            buttons.childForceExpandHeight = true; buttons.childForceExpandWidth = false;
            var reset = Button("Reset", footer, "По умолчанию", 200);
            var cancel = Button("CancelCapture", footer, "Отмена назначения", 235);
            var close = Button("Close", footer, "Закрыть", 160);
            var view = dialog.gameObject.AddComponent<InputSettingsView>();
            SetReferences(view, ("content", content), ("rowPrefab", row), ("status", status),
                ("cancelCaptureButton", cancel), ("resetButton", reset), ("closeButton", close));
            UnityEventTools.AddPersistentListener(launcher.onClick, view.Open);
            var asset = PrefabUtility.SaveAsPrefabAsset(root.gameObject, MenuPath);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return asset;
        }

        private static RectTransform Rect(string name, Transform parent = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        private static Text Label(string name, Transform parent, string text, int size)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size; label.color = Color.white; label.text = text;
            label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false; return label;
        }
        private static Button Button(string name, Transform parent, string text, float width)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>(); image.color = ButtonColor;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.preferredWidth = width; layout.preferredHeight = 42;
            var label = Label("Label", rect, text, 18); label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform);
            return button;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        }
        private static void SetReferences(UnityEngine.Object target, params (string name, UnityEngine.Object value)[] references)
        {
            var so = new SerializedObject(target);
            foreach (var entry in references) so.FindProperty(entry.name).objectReferenceValue = entry.value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

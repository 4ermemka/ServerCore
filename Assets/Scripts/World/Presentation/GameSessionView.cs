using System;
using System.IO;
using System.Linq;
using Shared.Inputs;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ServerCore.World.Presentation
{
    /// <summary>Тонкое меню над контрактом сессии. Не меняет синхронизируемые данные напрямую.</summary>
    public sealed class GameSessionView : MonoBehaviour
    {
        private GameSessionController session;
        private GameObject launch;
        private GameObject toolbar;
        private GameObject planPanel;
        private Text launchStatus;
        private Text header;
        private Text planTitle;
        private Text feedback;
        private Text clients;
        private InputField address;
        private InputField port;
        private InputField profile;
        private Button hostButton;
        private Button joinButton;
        private Button confirm;
        private RectTransform steps;
        private Dropdown scenes;
        private string[] scenePaths;
        private Font font;
        private IDisposable modalInput;
        private static readonly Color Background = new(.065f, .085f, .12f, .97f);
        private static readonly Color Accent = new(.18f, .73f, .73f);
        private sealed class MenuInput : InputContext
        {
            public MenuInput(IGameActionSource source) : base(source, null) { }
            public override bool TryHandleAction(string id) => true;
            public override bool TryHandleScroll(Vector2 delta) => true;
        }

        public void Initialize(GameSessionController owner)
        {
            session = owner;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("Game Session UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            var root = canvasObject.GetComponent<RectTransform>();

            toolbar = Panel("Session toolbar", root, Background).gameObject;
            Anchor((RectTransform)toolbar.transform, new Vector2(0, 1), Vector2.one, new Vector2(0, -96), Vector2.zero);
            header = Label(toolbar.transform, "Ход", 24, new Rect(18, 10, 360, 34));
            clients = Label(toolbar.transform, "", 15, new Rect(18, 50, 520, 30));
            confirm = Button(toolbar.transform, "Подтвердить ход", new Rect(550, 14, 220, 46), session.Confirm);
            Button(toolbar.transform, "Отключиться", new Rect(1370, 14, 200, 46), session.Disconnect);
            scenePaths = Enumerable.Range(0, SceneManager.sceneCountInBuildSettings).Select(SceneUtility.GetScenePathByBuildIndex).ToArray();
            scenes = CreateDropdown(toolbar.transform, new Rect(800, 14, 310, 46));
            scenes.AddOptions(scenePaths.Select(Path.GetFileNameWithoutExtension).ToList());
            Button(toolbar.transform, "Загрузить сцену", new Rect(1125, 14, 220, 46), () =>
            {
                if (scenePaths.Length > 0) session.ChangeScene(scenePaths[scenes.value]);
            });

            planPanel = Panel("Object plan", root, Background).gameObject;
            Anchor((RectTransform)planPanel.transform, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 226));
            planTitle = Label(planPanel.transform, "План", 24, new Rect(20, 12, 1100, 36));
            Button(planPanel.transform, "Закрыть", new Rect(1410, 10, 160, 36), () => session.Select(null));
            Label(planPanel.transform, "Нажмите на шаг, чтобы отменить его и все последующие. Перетаскивание добавляет движение.", 16, new Rect(20, 51, 1450, 28));
            var viewport = Panel("Plan scroll", planPanel.transform, new Color(.12f, .15f, .20f));
            TopRect(viewport, new Rect(20, 88, 1550, 100));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            steps = new GameObject("Steps", typeof(RectTransform)).GetComponent<RectTransform>();
            steps.SetParent(viewport, false);
            TopRect(steps, new Rect(0, 0, 1, 100));
            scroll.viewport = viewport;
            scroll.content = steps;
            feedback = Label(root, "", 16, new Rect(20, 105, 1500, 35));

            launch = Panel("Connection selection", root, new Color(.035f, .05f, .075f, .99f)).gameObject;
            Anchor((RectTransform)launch.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var card = Panel("Connection card", launch.transform, Background);
            card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
            card.pivot = new Vector2(.5f, .5f);
            card.sizeDelta = new Vector2(780, 620);
            Label(card, "SERVER CORE / ВЕДУЩИЙ", 32, new Rect(35, 30, 720, 52));
            Label(card, "Создайте сессию или подключитесь к другой.\nВ обоих случаях доступны все права ведущего.", 20, new Rect(35, 95, 710, 65));
            address = Field(card, "Адрес сервера", "127.0.0.1", 185);
            port = Field(card, "Порт", "7777", 250);
            port.contentType = InputField.ContentType.IntegerNumber;
            profile = Field(card, "Локальный профиль", "default", 315);
            Label(card, "Для двух копий приложения на одном компьютере используйте разные профили.", 15, new Rect(35, 382, 710, 42));
            hostButton = Button(card, "Создать сервер", new Rect(35, 440, 340, 58), () => Connect(true));
            joinButton = Button(card, "Подключиться", new Rect(400, 440, 340, 58), () => Connect(false));
            launchStatus = Label(card, "", 18, new Rect(35, 520, 710, 80));
            InputManager.OnStarted += BindModal;
            Refresh();
        }

        private void Connect(bool host)
        {
            if (!int.TryParse(port.text, out int number) || number < 1 || number > 65535)
            { launchStatus.text = "Введите порт от 1 до 65535."; return; }
            session.Connect(host, address.text.Trim(), number, profile.text.Trim());
            hostButton.interactable = joinButton.interactable = false;
        }

        public void Refresh()
        {
            if (session == null) return;
            bool ready = session.IsReady;
            launch.SetActive(!ready);
            toolbar.SetActive(ready);
            feedback.gameObject.SetActive(ready);
            launchStatus.text = feedback.text = session.Status;
            hostButton.interactable = joinButton.interactable = !session.IsBusy;
            BindModal();
            var planning = session.State.Planning.Value;
            header.text = $"ХОД {planning.Turn}   /   ПЛАНИРОВАНИЕ";
            int online = planning.Participants.Values.Count(p => p.Connected);
            int confirmed = planning.Participants.Values.Count(p => p.Connected && p.Confirmed);
            clients.text = $"Ведущий · {session.ClientId.ToString("N").Substring(0, 8)}   |   Готовы {confirmed} из {online}";
            bool ownConfirmed = planning.Participants.TryGetValue(session.ClientId, out var me) && me.Confirmed;
            confirm.interactable = ready && !session.CommandPending && !ownConfirmed && me?.Connected == true;
            confirm.GetComponentInChildren<Text>().text = ownConfirmed ? "Ожидаем участников" : "Подтвердить ход";
            var selected = session.State.Find(session.SelectedObjectId);
            planPanel.SetActive(ready && selected != null);
            foreach (Transform child in steps) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (selected == null) return;
            string title = string.IsNullOrWhiteSpace(selected.DisplayName.Value) ? selected.Id.Value : selected.DisplayName.Value;
            planTitle.text = title + "  /  ПЛАН ДЕЙСТВИЙ";
            if (!planning.Plans.TryGetValue(selected.Id.Value, out var plan) || plan.Count == 0)
            {
                Label(steps, "План пока пуст. Перетащите фигурку на поле.", 20, new Rect(18, 25, 1000, 50));
                steps.sizeDelta = new Vector2(1500, 100);
                return;
            }
            int index = 0;
            foreach (var step in plan)
            {
                string label = session.Presentations.TryGet(step.ActionId, out var presentation) ? presentation.Label(step) : step.ActionId;
                string stepId = step.Id;
                string objectId = selected.Id.Value;
                var button = Button(steps, $"{index + 1}. {label}", new Rect(8 + index * 195, 8, 164, 84), () => session.Truncate(objectId, stepId));
                button.GetComponent<Image>().color = session.Palette.Get(step.ColorIndex);
                button.GetComponentInChildren<Text>().color = new Color(.035f, .05f, .08f);
                button.interactable = session.CanEdit(selected);
                if (index < plan.Count - 1) Label(steps, "→", 28, new Rect(174 + index * 195, 27, 30, 45));
                index++;
            }
            steps.sizeDelta = new Vector2(Mathf.Max(1550, plan.Count * 195), 100);
        }

        private void BindModal()
        {
            if (launch == null || !launch.activeSelf)
            { modalInput?.Dispose(); modalInput = null; return; }
            if (modalInput == null && InputManager.Instance is IContextInputSource source && source.Contexts?.Current != null)
                modalInput = source.Contexts.Enter(new MenuInput(InputManager.Instance));
        }
        private void Update() { if (launch != null && launch.activeSelf) BindModal(); }
        private void OnDestroy() { InputManager.OnStarted -= BindModal; modalInput?.Dispose(); }
        private InputField Field(Transform parent, string title, string value, float y)
        {
            Label(parent, title, 18, new Rect(35, y, 250, 45));
            var rect = Panel(title, parent, new Color(.16f, .20f, .27f));
            TopRect(rect, new Rect(295, y, 445, 48));
            var input = rect.gameObject.AddComponent<InputField>();
            var text = Label(rect, value, 21, new Rect(12, 4, 421, 40));
            input.textComponent = text;
            input.text = value;
            return input;
        }
        private RectTransform Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }
        private Text Label(Transform parent, string text, int size, Rect rect)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            TopRect(go.GetComponent<RectTransform>(), rect);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = text;
            label.color = new Color(.91f, .94f, .98f);
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }
        private Button Button(Transform parent, string title, Rect rect, UnityEngine.Events.UnityAction action)
        {
            var box = Panel(title, parent, Accent);
            TopRect(box, rect);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = box.GetComponent<Image>();
            button.onClick.AddListener(action);
            var label = Label(box, title, 20, new Rect(6, 0, rect.width - 12, rect.height));
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(.04f, .08f, .10f);
            return button;
        }
        private Dropdown CreateDropdown(Transform parent, Rect rect)
        {
            var box = Panel("Scene selection", parent, new Color(.20f, .25f, .32f));
            TopRect(box, rect);
            var dropdown = box.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = box.GetComponent<Image>();
            dropdown.captionText = Label(box, "Сцена", 20, new Rect(12, 0, rect.width - 24, rect.height));
            var template = Panel("Template", box, Background);
            TopRect(template, new Rect(0, rect.height, rect.width, 200));
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            var viewport = Panel("Viewport", template, Background);
            Anchor(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Panel("Content", viewport, Color.clear);
            TopRect(content, new Rect(0, 0, rect.width, 40));
            var item = Panel("Item", content, new Color(.2f, .3f, .37f));
            TopRect(item, new Rect(0, 0, rect.width, 40));
            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();
            toggle.graphic = item.GetComponent<Image>();
            dropdown.itemText = Label(item, "", 18, new Rect(12, 0, rect.width - 24, 40));
            scroll.viewport = viewport;
            scroll.content = content;
            dropdown.template = template;
            template.gameObject.SetActive(false);
            return dropdown;
        }
        private static void TopRect(RectTransform rect, Rect area)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
        }
        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}

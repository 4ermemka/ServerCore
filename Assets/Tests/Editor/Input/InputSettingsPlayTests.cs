using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ServerCore.Input;
using Shared.Inputs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ServerCore.Input.Tests
{
    public sealed class InputSettingsPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            // Изолированный тест этой UI-системы: стартовый экран сети проверяется отдельно.
            GameObject.Find("Game Session")?.SetActive(false);
            // Тест не читает и не перезаписывает настройки реального пользователя.
            var input = Object.FindAnyObjectByType<InputManager>();
            var so = new SerializedObject(input);
            so.FindProperty("saveBindings").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            yield return new EnterPlayMode();
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MenuOpensFromActionBuildsAllRowsScrollsAndRebindsWithoutChangingAsset()
        {
            var input = InputManager.Instance;
            Assert.That(input, Is.Not.Null);
            var menu = Object.FindAnyObjectByType<InputSettingsView>(FindObjectsInactive.Include);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.gameObject.activeSelf, Is.False);
            var source = new FakeInputSource();
            source.Release(); input.Router.Tick(source); // Нейтральный кадр после focus/загрузки.
            source.Press(KeyCode.F1); input.Router.Tick(source);
            Assert.That(menu.gameObject.activeSelf, Is.True, "Settings.Input должен вызывать UnityEvent Open.");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var rows = menu.GetComponentsInChildren<InputBindingRowView>();
            Assert.That(rows.Length, Is.EqualTo(input.Definitions.Count));
            var scroll = menu.GetComponentInChildren<ScrollRect>();
            // Уменьшаем viewport для проверки overflow независимо от числа игровых действий.
            scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100);
            Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0;
            yield return null;
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(0.01f));
            scroll.verticalNormalizedPosition = 1;

            int reactions = 0;
            // Камера теперь поглощает действие в MainContext. Наблюдаем маршрутизацию до него,
            // сохраняя настоящую реакцию камеры; обычный Performed для этого действия не вызывается.
            using var listener = input.Contexts.Enter(new ReactionProbe(input, () => reactions++));
            menu.StartRebind("Camera.RotateRight");
            Assert.That(input.Capture, Is.Not.Null);
            Assert.That(EventSystem.current.sendNavigationEvents, Is.False);
            source.Release(); input.Capture.Tick(source);
            source.Press(KeyCode.Q); input.Capture.Tick(source);
            Assert.That(input.Capture, Is.Null);
            Assert.That(input.GetBinding("Camera.RotateRight").Key, Is.EqualTo(KeyCode.Q));
            Assert.That(input.GetBinding("Camera.RotateLeft").IsEmpty, Is.True);
            input.Router.Tick(source);
            Assert.That(reactions, Is.Zero, "Переназначение не поворачивает камеру.");
            var profile = AssetDatabase.LoadAssetAtPath<GameInputProfile>("Assets/Data/Input/MainInputProfile.asset");
            Assert.That(profile.Actions.Single(a => a.Id == "Camera.RotateLeft").DefaultBinding.Key, Is.EqualTo(KeyCode.Q));

            // Escape доступен как назначаемая клавиша, для отмены есть отдельная кнопка.
            menu.StartRebind("Camera.RotateLeft");
            source.Release(); input.Capture.Tick(source);
            source.Press(KeyCode.Escape); input.Capture.Tick(source);
            Assert.That(input.GetBinding("Cancel").IsEmpty, Is.True);
            Assert.That(input.GetAction("Camera.RotateLeft").Trigger, Is.EqualTo(ActionTrigger.Hold));
            menu.StartRebind("Camera.RotateRight");
            menu.CancelCapture();
            Assert.That(input.GetBinding("Camera.RotateRight").Key, Is.EqualTo(KeyCode.Q));
            menu.ResetDefaults();
            Assert.That(input.GetBinding("Camera.RotateLeft").Key, Is.EqualTo(KeyCode.Q));
            Assert.That(input.GetBinding("Camera.RotateRight").Key, Is.EqualTo(KeyCode.E));
            Assert.That(input.Definitions.Any(a => a.Id.StartsWith("Camera.Zoom")), Is.False);

            yield return CaptureMenu(menu);
            menu.Close();
            Assert.That(input.Router.IsSuspended, Is.False);
            source.Release(); input.Router.Tick(source);
            source.Press(KeyCode.E); input.Router.Tick(source);
            Assert.That(reactions, Is.EqualTo(1));
            menu.Open();
            yield return null;
            Assert.That(menu.GetComponentsInChildren<InputBindingRowView>().Length, Is.EqualTo(input.Definitions.Count), "Повторное открытие не дублирует строки.");
            menu.StartRebind("Camera.RotateLeft");
            menu.Close();
            Assert.That(input.Capture, Is.Null);
            Assert.That(input.Router.IsSuspended, Is.False);
            input.Rebind("Settings.Input", default);
            var launcher = menu.GetComponentInParent<Canvas>().transform.Find("OpenSettings").GetComponent<Button>();
            launcher.onClick.Invoke();
            Assert.That(menu.gameObject.activeSelf, Is.True, "Без горячей клавиши меню доступно кнопкой.");
            menu.ResetDefaults(); menu.Close();
        }

        private static IEnumerator CaptureMenu(InputSettingsView menu)
        {
            string directory = System.Environment.GetEnvironmentVariable("INPUT_PREVIEW_FOLDER");
            if (string.IsNullOrEmpty(directory)) yield break;
            var canvas = menu.GetComponentInParent<Canvas>();
            var cameraObject = new GameObject("UI preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .09f, .12f);
            camera.orthographic = true;
            camera.cullingMask = 1 << 5;
            var children = canvas.GetComponentsInChildren<Transform>(true);
            var layers = children.Select(child => child.gameObject.layer).ToArray();
            foreach (var child in children) child.gameObject.layer = 5;
            var image = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = image;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
                yield return null;
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = image;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "input-settings.png"), pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                for (int i = 0; i < children.Length; i++) children[i].gameObject.layer = layers[i];
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.Destroy(cameraObject); Object.Destroy(image); Object.Destroy(pixels);
            }
        }

        private sealed class ReactionProbe : InputContext
        {
            private readonly System.Action observed;
            public ReactionProbe(IGameActionSource source, System.Action observed) : base(source, null) => this.observed = observed;
            public override bool TryHandleAction(string id)
            {
                if (id == "Camera.RotateRight") observed();
                return false;
            }
        }
    }
}

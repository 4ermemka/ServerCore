using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Shared.CameraSystem;
using Shared.Inputs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ServerCore.CameraTests
{
    public sealed class CameraDirectorTests
    {
        [Test]
        public void DirectorEvaluatesLivePositionAndLookFunctions()
        {
            Vector3 position = new Vector3(1, 2, 3), look = new Vector3(4, 5, 6);
            using var director = new CameraDirector(() => position, () => look);
            director.Evaluate(out var p, out var l);
            Assert.That(p, Is.EqualTo(position)); Assert.That(l, Is.EqualTo(look));
            position = Vector3.one; look = Vector3.right;
            director.Evaluate(out p, out l);
            Assert.That(p, Is.EqualTo(position)); Assert.That(l, Is.EqualTo(look));
        }
        [Test]
        public void NestedModesRestorePreviousAndCannotPopRoot()
        {
            using var director = new CameraDirector(() => Vector3.zero, () => Vector3.forward);
            using var dialog = director.Enter(() => Vector3.one, () => Vector3.up);
            var cutscene = director.Enter(() => Vector3.right, () => Vector3.left);
            Assert.That(director.Root.Next, Is.SameAs(dialog));
            Assert.That(dialog.Next, Is.SameAs(cutscene));
            Assert.That(cutscene.Previous, Is.SameAs(dialog));
            director.Evaluate(out var p, out _); Assert.That(p, Is.EqualTo(Vector3.right));
            Assert.That(director.Pop(), Is.True);
            director.Evaluate(out p, out _); Assert.That(p, Is.EqualTo(Vector3.one));
            Assert.That(dialog.Next, Is.Null); Assert.That(cutscene.IsActive, Is.False);
            dialog.Dispose();
            Assert.That(director.IsDefault, Is.True);
            director.Root.Dispose();
            Assert.That(director.Pop(), Is.False);
        }
        [Test]
        public void ClosingParentClosesChildrenAndStaleTokenCannotCloseNewMode()
        {
            using var director = new CameraDirector(() => Vector3.zero, () => Vector3.forward);
            var parent = director.Enter(() => Vector3.one, () => Vector3.up);
            var child = director.Enter(() => Vector3.right, () => Vector3.left);
            parent.Dispose();
            Assert.That(child.IsActive, Is.False); Assert.That(child.Previous, Is.Null);
            var replacement = director.Enter(() => Vector3.back, () => Vector3.forward);
            child.Dispose(); parent.Dispose();
            Assert.That(director.Current, Is.SameAs(replacement));
            director.Reset();
            Assert.That(director.IsDefault, Is.True); Assert.That(replacement.IsActive, Is.False);
        }
        [Test]
        public void InvalidFunctionsDoNotReplaceActiveModeAndDisposedDirectorRejectsUse()
        {
            var director = new CameraDirector(() => Vector3.zero, () => Vector3.forward);
            Assert.Throws<ArgumentNullException>(() => director.Enter(null, () => Vector3.zero));
            Assert.Throws<ArgumentNullException>(() => director.Enter(() => Vector3.zero, null));
            Assert.That(director.IsDefault, Is.True);
            var mode = director.Enter(() => Vector3.one, () => Vector3.forward);
            director.Dispose(); director.Dispose(); mode.Dispose();
            Assert.That(mode.IsActive, Is.False);
            Assert.Throws<ObjectDisposedException>(() => director.Evaluate(out _, out _));
            Assert.Throws<ObjectDisposedException>(() => director.Enter(() => Vector3.zero, () => Vector3.forward));
        }
    }

    public sealed class IsometricCameraTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject Make(string name)
        {
            var go = new GameObject(name); objects.Add(go); return go;
        }
        [TearDown] public void Cleanup() { foreach (var go in objects) if (go != null) Object.DestroyImmediate(go); objects.Clear(); }

        private CameraManager Rig(out Camera camera, out IsometricCamera behaviour)
        {
            var rig = Make("rig"); var manager = rig.AddComponent<CameraManager>(); manager.enabled = false;
            camera = Make("camera").AddComponent<Camera>();
            behaviour = rig.GetComponent<IsometricCamera>();
            behaviour.Focus = Make("focus").transform;
            behaviour.Focus.position = new Vector3(20, 2, 30);
            manager.Configure(camera, behaviour);
            return manager;
        }

        [Test]
        public void DefaultIsDiagonalToGridWithFortyDegreeElevationAndCenteredFocus()
        {
            var manager = Rig(out var camera, out var behaviour);
            var grid = Make("grid").transform; grid.rotation = Quaternion.Euler(0, 30, 0);
            behaviour.GridFrame = grid;
            manager.Tick(0);
            var offset = camera.transform.position - behaviour.GetLookAt();
            Assert.That(offset.magnitude, Is.EqualTo(12).Within(0.0001f));
            Assert.That(Mathf.Asin(offset.normalized.y) * Mathf.Rad2Deg, Is.EqualTo(40).Within(0.0001f));
            Assert.That(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 75), Is.EqualTo(0).Within(0.001f));
            var viewport = camera.WorldToViewportPoint(behaviour.GetLookAt());
            Assert.That(viewport.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(viewport.y, Is.EqualTo(0.5f).Within(0.0001f));
        }
        [Test]
        public void QERotatesFullCircleAtConstantDistanceAndStopsOnRelease()
        {
            var manager = Rig(out var camera, out var behaviour);
            camera.fieldOfView = 30;
            for (int i = 0; i < 360; i++)
            {
                manager.Orbit(1); manager.Tick(1f / 60);
                Assert.That(Vector3.Distance(camera.transform.position, behaviour.GetLookAt()), Is.EqualTo(12).Within(0.001f));
                Assert.That(camera.WorldToViewportPoint(behaviour.GetLookAt()).x, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(Mathf.Abs(camera.transform.right.y), Is.LessThan(0.00001f));
            }
            Assert.That(Mathf.DeltaAngle(behaviour.Angle, 45), Is.EqualTo(0).Within(0.01f));
            float angle = behaviour.Angle;
            manager.Tick(1);
            Assert.That(behaviour.Angle, Is.EqualTo(angle));
            manager.Orbit(-1); manager.Tick(1);
            Assert.That(Mathf.DeltaAngle(angle, behaviour.Angle), Is.EqualTo(-60).Within(0.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(30), "Контроллер не меняет объектив.");
        }
        [Test]
        public void CustomFunctionsOverridePoseAndPauseDefaultInputUntilExit()
        {
            var manager = Rig(out var camera, out var behaviour);
            manager.Tick(0);
            float initialAngle = behaviour.Angle;
            Vector3 position = new Vector3(3, 4, 5), look = Vector3.zero;
            manager.Orbit(1); // ввод уже пришёл, но в этом же кадре началась катсцена.
            var mode = manager.Director.Enter(() => position, () => look);
            manager.Tick(1);
            manager.Orbit(1); manager.Tick(1);
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Assert.That(behaviour.Angle, Is.EqualTo(initialAngle));
            position += Vector3.up;
            manager.Tick(0);
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Assert.That(Vector3.Dot(camera.transform.forward, (look - position).normalized), Is.GreaterThan(0.9999f));
            mode.Dispose();
            manager.Tick(1);
            Assert.That(behaviour.Angle, Is.EqualTo(initialAngle));
            Assert.That(camera.transform.position, Is.EqualTo(behaviour.GetPosition()));
        }
        [Test]
        public void MissingFocusUsesOriginAndCoincidentFunctionPointsPreserveRotation()
        {
            var manager = Rig(out var camera, out var behaviour);
            manager.SetFocusPoint(null);
            Assert.That(behaviour.GetLookAt(), Is.EqualTo(behaviour.FocusOffset));
            manager.Tick(0);
            var rotation = camera.transform.rotation;
            using var mode = manager.Director.Enter(() => Vector3.one, () => Vector3.one);
            manager.Tick(0);
            Assert.That(camera.transform.position, Is.EqualTo(Vector3.one));
            Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
        }

    }

    public sealed class CameraDirectorSceneTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var input = Object.FindAnyObjectByType<InputManager>();
            var inputSo = new SerializedObject(input);
            inputSo.FindProperty("saveBindings").boolValue = false; inputSo.ApplyModifiedPropertiesWithoutUndo();
            // Переход PlayMode перезагружает domain: замыкания теста создаём уже после перехода.
            yield return new EnterPlayMode(); yield return null;
        }

        [UnityTest]
        public IEnumerator TestSceneUsesSingleFocusNoZoomAndCanRestoreDefaultMode()
        {
            var manager = Object.FindAnyObjectByType<CameraManager>();
            Assert.That(manager.DefaultCamera.Focus.name, Is.EqualTo("Player Camera Focus"));
            Assert.That(manager.ControlledCamera.orthographic, Is.True);
            Assert.That(InputManager.Instance.Definitions.Any(a => a.Id.StartsWith("Camera.Zoom")), Is.False);
            Assert.That(InputManager.Instance.GetAction("Camera.RotateLeft").Trigger, Is.EqualTo(ActionTrigger.Hold));
            Capture(manager.ControlledCamera);
            for (int frame = 0; frame < 30; frame++)
            {
                manager.Orbit(1); yield return null;
                var p = manager.ControlledCamera.WorldToViewportPoint(manager.DefaultCamera.GetLookAt());
                Assert.That(p.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(p.y, Is.EqualTo(0.5f).Within(0.001f));
            }
            Vector3 customPosition = manager.DefaultCamera.GetLookAt() + new Vector3(4, 6, -8);
            var mode = manager.Director.Enter(() => customPosition, manager.DefaultCamera.GetLookAt);
            yield return null;
            Assert.That(Vector3.Distance(manager.ControlledCamera.transform.position, customPosition), Is.LessThan(0.001f));
            mode.Dispose(); yield return null;
            Assert.That(Vector3.Distance(manager.ControlledCamera.transform.position, manager.DefaultCamera.GetPosition()), Is.LessThan(0.001f));
            var pending = manager.Director.Enter(() => Vector3.zero, () => Vector3.forward);
            manager.enabled = false;
            Assert.That(pending.IsActive, Is.False); Assert.That(manager.Director.IsDefault, Is.True);
            manager.enabled = true;
        }
        private static void Capture(Camera camera)
        {
            string path = Environment.GetEnvironmentVariable("CAMERA_PREVIEW_PATH");
            if (string.IsNullOrEmpty(path)) return;
            var image = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = image; camera.Render(); RenderTexture.active = image;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; Object.Destroy(image); Object.Destroy(pixels); }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}

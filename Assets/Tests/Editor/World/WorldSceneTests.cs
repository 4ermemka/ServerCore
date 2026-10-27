using System.Collections;
using System.Linq;
using System.IO;
using NUnit.Framework;
using ServerCore.World.Data;
using Shared.WorldObjects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace ServerCore.World.Tests
{
    public sealed class WorldSceneTests
    {
        [Test]
        public void PrefabsHaveViewsHitTargetsAndPersistentGeometry()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/World" });
            Assert.That(paths.Length, Is.GreaterThanOrEqualTo(2));
            foreach (string guid in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var view = go.GetComponent<WorldObjectView>();
                Assert.That(view, Is.Not.Null, go.name);
                Assert.That(view.SceneId, Is.Empty, "ID принадлежит экземпляру, не префабу.");
                Assert.That(go.GetComponent<GridObjectDrag>(), Is.Not.Null);
                Assert.That(go.GetComponentsInChildren<Collider>().Length, Is.GreaterThan(0));
                Assert.That(go.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
                foreach (var mesh in go.GetComponentsInChildren<MeshFilter>())
                {
                    Assert.That(mesh.sharedMesh, Is.Not.Null, mesh.name);
                    Assert.That(mesh.sharedMesh.vertexCount, Is.GreaterThan(0), mesh.name);
                    Assert.That(AssetDatabase.GetAssetPath(mesh.sharedMesh), Does.Not.EndWith(".unity"));
                }
            }
        }

    }

    public sealed class WorldScenePlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            // Этот fixture проверяет самостоятельный World/View без сетевой composition root.
            var gameSession = Object.FindAnyObjectByType<GameSessionController>();
            if (gameSession != null) gameSession.gameObject.SetActive(false);
            yield return new EnterPlayMode();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SceneBindsDataAndSupportsDragPatchAndEmptySnapshot()
        {
            var controller = Object.FindAnyObjectByType<WorldSceneController>();
            var grid = Object.FindAnyObjectByType<GridProvider>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(grid, Is.Not.Null);
            Assert.That(controller.State, Is.Not.Null);
            // EnterPlayMode у editor runner может продолжить enumerator до первого Start.
            for (int frame = 0; frame < 10 && !controller.State.IsInitialized; frame++) yield return null;
            Assert.That(controller.State.IsInitialized, Is.True, "WorldSceneController.Start должен наполнить сценовый мир.");
            Assert.That(controller.State.Players.Count, Is.EqualTo(1));
            Assert.That(controller.State.Furniture.Count, Is.EqualTo(2));
            var player = Object.FindAnyObjectByType<PlayerObjectView>();
            Assert.That(player, Is.Not.Null);
            Assert.That(player.Data, Is.Not.Null);
            CapturePreview();
            var initialData = player.Data;
            var cells = grid.AllCells.ToArray();
            var target = cells.Where(c => UnityGridSpace.Address(c) != player.Data.Cell.Value)
                .OrderBy(c => Vector3.Distance(c.GetWorldPosition(grid.CellSize), player.AnchorWorld)).First();
            var address = UnityGridSpace.Address(target);
            int local = 0;
            controller.State.Changed += (_, _, _) => local++;
            controller.State.ApplyPatch("Players.[0].Cell", address);
            Assert.That(Vector3.Distance(player.AnchorWorld, target.GetWorldPosition(grid.CellSize)), Is.LessThan(0.001f));
            Assert.That(local, Is.Zero);

            // Реальный pointer pipeline на физической сцене: preview без мутации, release с одним Changed.
            var camera = Camera.main;
            var raycaster = camera.GetComponent<PhysicsRaycaster>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = camera.WorldToScreenPoint(player.AnchorWorld),
                pointerPressRaycast = new RaycastResult { module = raycaster, gameObject = player.gameObject }
            };
            var drag = player.GetComponent<GridObjectDrag>();
            drag.OnBeginDrag(pointer);
            var before = player.Data.Cell.Value;
            var destination = cells.Where(c => UnityGridSpace.Address(c) != before && c.GridPosition.y == target.GridPosition.y)
                .OrderBy(c => Vector3.Distance(c.GetWorldPosition(grid.CellSize), player.AnchorWorld)).First();
            pointer.position = camera.WorldToScreenPoint(destination.GetWorldPosition(grid.CellSize));
            drag.OnDrag(pointer);
            Assert.That(player.Data.Cell.Value, Is.EqualTo(before));
            drag.OnEndDrag(pointer);
            Assert.That(player.Data.Cell.Value, Is.Not.EqualTo(before), "Проверить raycast поверхности и snap distance.");
            Assert.That(local, Is.EqualTo(1));
            Assert.That(player.Data, Is.SameAs(initialData));
            Assert.That(Object.FindAnyObjectByType<PlayerObjectView>(), Is.SameAs(player));

            pointer.position = camera.WorldToScreenPoint(player.AnchorWorld);
            drag.OnBeginDrag(pointer);
            drag.OnDrag(pointer);
            controller.State.ApplyPatch("Players.[0].Cell", before);
            Assert.That(drag.IsDragging, Is.False);
            Assert.That(player.Data.Cell.Value, Is.EqualTo(before));

            controller.State.ApplySnapshot(controller.State.CreateSnapshot());
            yield return null;
            Assert.That(Object.FindAnyObjectByType<PlayerObjectView>(), Is.SameAs(player));
            Assert.That(player.Data, Is.Not.SameAs(initialData), "Snapshot должен перепривязать сохранившийся View к новой Data.");

            controller.State.ApplySnapshot(new WorldState().CreateSnapshot());
            yield return null;
            yield return null;
            Assert.That(Object.FindObjectsByType<WorldObjectView>().Length, Is.Zero);
            Assert.That(controller.State.IsInitialized, Is.True);
            var spawned = new FurnitureData();
            spawned.Id.Value = "runtime-pile";
            spawned.DefinitionId.Value = "furniture.pile";
            spawned.Cell.Value = address;
            controller.State.Furniture.Add(spawned);
            yield return null;
            Assert.That(controller.TryGetView("runtime-pile", out var spawnedView), Is.True);
            Assert.That(spawnedView.Data, Is.SameAs(spawned));
            Assert.That(Vector3.Distance(spawnedView.AnchorWorld, target.GetWorldPosition(grid.CellSize)), Is.LessThan(0.001f));
        }

        private static void CapturePreview()
        {
            string reportFolder = System.Environment.GetEnvironmentVariable("WORLD_PREVIEW_FOLDER");
            if (string.IsNullOrEmpty(reportFolder)) return;
            var camera = Camera.main;
            var texture = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory(reportFolder);
                File.WriteAllBytes(Path.Combine(reportFolder, "world-preview.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.Destroy(pixels);
                Object.Destroy(texture);
            }
        }
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using ServerCore.World.Data;
using ServerCore.World.Planning;
using Shared.TurnPlanning;
using System.Collections.Generic;
using ServerCore.World;
using Shared.WorldObjects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ServerCore.Planning.Tests
{
    public sealed class GameSessionPlayTests
    {
        [UnitySetUp]
        public IEnumerator Setup()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            yield return new EnterPlayMode();
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
        private static IEnumerator Until(Func<bool> predicate, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), message);
                yield return null;
            }
        }
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class AnyGrid : IGridSpace { public bool Contains(GridCoordinate cell) => true; }

        [UnityTest]
        public IEnumerator ClientAndReconnectLoadSceneAndKeepIdentityAndPlan()
        {
            var session = Object.FindAnyObjectByType<GameSessionController>();
            yield return Until(() => session.State.IsInitialized, "Initial scene.");
            var source = new WorldState();
            source.ApplySnapshot(JsonGameSerializer.Deserialize<Dictionary<string, object>>(JsonGameSerializer.Serialize(session.State.CreateSnapshot())));
            var catalog = new ActionRegistry();
            catalog.Register(new MovementAction());
            var authority = new WorldTurnAuthority(source, () => new AnyGrid(), new TurnPlanner(catalog, new OwnerAccessPolicy()));
            var identity = new MemoryClientIdentityStore();
            using var server = new GameServer(new TcpHostTransport(), patchAuthority: identity.LoadOrCreate().ClientId);
            using var host = new GameClient(new TcpClientTransport(), source, identity) { ApplyOwnPatchEchoes = false };
            server.ClientReady += peer => authority.SetConnected(peer.ClientId, true);
            server.ClientDisconnected += peer => authority.SetConnected(peer.ClientId, false);
            IEnumerator Pump(Func<bool> done)
            {
                float deadline = Time.realtimeSinceStartup + 20;
                while (!done())
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), session.Status);
                    server.Update();
                    host.Update();
                    yield return null;
                }
            }
            int port = FreePort();
            var start = server.StartAsync("127.0.0.1", port);
            yield return Pump(() => start.IsCompleted);
            start.GetAwaiter().GetResult();
            var connect = host.ConnectAsync("127.0.0.1", port);
            yield return Pump(() => connect.IsCompleted);
            connect.GetAwaiter().GetResult();
            var oldScene = Object.FindAnyObjectByType<WorldSceneController>().GetEntityId();
            string profile = "planning-client-test-" + Guid.NewGuid().ToString("N");
            session.Connect(false, "127.0.0.1", port, profile);
            yield return Pump(() => session.IsReady && session.State.Planning.Value.Participants.Count == 2);
            Assert.That(Object.FindAnyObjectByType<WorldSceneController>().GetEntityId(), Is.Not.EqualTo(oldScene));
            Guid clientId = session.ClientId;
            string id = source.Players[0].Id.Value;
            var cell = source.Players[0].Cell.Value;
            Assert.That(authority.Apply(clientId, new PlanCommand
            {
                Kind = PlanCommandKind.Append, Turn = source.Planning.Value.Turn,
                ObjectId = id, ActionId = MovementAction.ActionId, Arguments = MovementAction.Arguments(cell)
            }), Is.Null);
            yield return Pump(() => session.State.Planning.Value.Plans.ContainsKey(id));
            string stepId = session.State.Planning.Value.Plans[id].First.Value.Id;
            session.Disconnect();
            yield return Pump(() => !session.IsBusy && server.ConnectedClients.Count == 1);
            oldScene = Object.FindAnyObjectByType<WorldSceneController>().GetEntityId();
            session.Connect(false, "127.0.0.1", port, profile);
            yield return Pump(() => session.IsReady && session.State.Planning.Value.Participants[clientId].Connected);
            Assert.That(session.ClientId, Is.EqualTo(clientId));
            Assert.That(session.State.Planning.Value.Participants[clientId].IsGameMaster, Is.True);
            Assert.That(session.State.Planning.Value.Plans[id].First.Value.Id, Is.EqualTo(stepId));
            Assert.That(Object.FindAnyObjectByType<WorldSceneController>().GetEntityId(), Is.Not.EqualTo(oldScene));
            Assert.That(Object.FindObjectsByType<ManagersHolder>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameSessionController>().Length, Is.EqualTo(1));
            session.Disconnect();
            yield return Pump(() => !session.IsBusy && server.ConnectedClients.Count == 1);
        }

        private static void Capture(GameSessionController session, string path)
        {
            var canvas = session.GetComponentInChildren<Canvas>();
            var camera = Camera.main;
            var texture = new RenderTexture(1600, 900, 24);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var mode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            try
            {
                camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 1;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                pixels.Apply();
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = previousCamera;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.Destroy(pixels);
                Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator HostSelectionDragTruncateConfirmAndReloadKeepOneSessionAndCamera()
        {
            var session = Object.FindAnyObjectByType<GameSessionController>();
            Assert.That(session, Is.Not.Null);
            yield return Until(() => session.State.IsInitialized, "Initial scene was not bound.");
            Assert.That(session.IsReady, Is.False);
            Assert.That(GameObject.Find("Connection selection"), Is.Not.Null);
            Capture(session, "Temp/game-startup.png");
            session.Connect(true, "127.0.0.1", FreePort(), "planning-test-" + Guid.NewGuid().ToString("N"));
            yield return Until(() => session.IsReady && session.State.Planning.Value.Participants.Count == 1, session.Status);
            yield return null;
            Assert.That(GameObject.Find("Connection selection"), Is.Null);
            var figure = Object.FindAnyObjectByType<PlayerObjectView>();
            var world = Object.FindAnyObjectByType<WorldSceneController>();
            var grid = Object.FindAnyObjectByType<GridProvider>();
            var before = figure.Data.Cell.Value;
            var destination = grid.AllCells.Where(c => UnityGridSpace.Address(c) != before)
                .OrderBy(c => Vector3.Distance(c.GetWorldPosition(grid.CellSize), figure.AnchorWorld)).First();
            var target = UnityGridSpace.Address(destination);
            var camera = Camera.main;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = camera.WorldToScreenPoint(figure.AnchorWorld),
                pointerPressRaycast = new RaycastResult { module = camera.GetComponent<PhysicsRaycaster>(), gameObject = figure.gameObject }
            };
            var drag = figure.GetComponent<GridObjectDrag>();
            drag.OnBeginDrag(pointer);
            yield return null; // selection repaint must not cancel a drag begun on an unselected figure.
            Assert.That(drag.IsDragging, Is.True);
            pointer.position = camera.WorldToScreenPoint(destination.GetWorldPosition(grid.CellSize));
            drag.OnDrag(pointer);
            drag.OnEndDrag(pointer);
            yield return Until(() => !session.CommandPending && session.State.Planning.Value.Plans.ContainsKey(figure.Data.Id.Value), session.Status);
            Assert.That(figure.Data.Cell.Value, Is.EqualTo(before));
            var plan = session.State.Planning.Value.Plans[figure.Data.Id.Value];
            Assert.That(plan.Count, Is.EqualTo(1));
            Assert.That(session.SelectedObjectId, Is.EqualTo(figure.Data.Id.Value));
            Assert.That(GameObject.Find("Object plan"), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<LineRenderer>().Length, Is.GreaterThanOrEqualTo(2));
            string id = figure.Data.Id.Value;
            session.PlanMove(id, before);
            yield return Until(() => !session.CommandPending && session.State.Planning.Value.Plans[id].Count == 2, session.Status);
            yield return null;
            Capture(session, "Temp/game-planning.png");
            string second = session.State.Planning.Value.Plans[id].Last.Value.Id;
            session.Truncate(id, second);
            yield return Until(() => !session.CommandPending && session.State.Planning.Value.Plans[id].Count == 1, session.Status);
            var confirm = Object.FindObjectsByType<Button>().First(button => button.name == "Подтвердить ход");
            confirm.onClick.Invoke();
            yield return Until(() => !session.CommandPending && session.State.Planning.Value.Turn == 2, session.Status);
            Assert.That(session.State.Find(id).Cell.Value, Is.EqualTo(target));
            Assert.That(session.State.Planning.Value.Plans, Is.Empty);
            var oldSceneController = world.GetEntityId();
            Guid clientId = session.ClientId;
            session.ChangeScene("Test");
            yield return Until(() => session.IsReady && !session.CommandPending && session.State.Scene.Value.Revision == 1
                && Object.FindAnyObjectByType<WorldSceneController>().GetEntityId() != oldSceneController, session.Status);
            yield return null;
            Assert.That(session.ClientId, Is.EqualTo(clientId));
            Assert.That(Object.FindObjectsByType<GameSessionController>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ManagersHolder>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<InputManager>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<Camera>().Count(c => c.CompareTag("MainCamera")), Is.EqualTo(1));
            Assert.That(session.State.Players.Count, Is.EqualTo(1));
            Assert.That(session.State.Furniture.Count, Is.EqualTo(2));
        }
    }
}

using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using NUnit.Framework;
using ServerCore.World.Data;
using ServerCore.World.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ServerCore.World.Tests
{
    public sealed class EntityScenePlayTests
    {
        [UnitySetUp]
        public IEnumerator Setup()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EntityTestScene.unity");
            yield return new EnterPlayMode();
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [UnityTest]
        public IEnumerator HostAndRemoteReconnectReuseCharactersAndSynchronizeData()
        {
            var session = Object.FindAnyObjectByType<GameSessionController>();
            var world = Object.FindAnyObjectByType<WorldSceneController>();
            Assert.That(session.State.Scene.Value.Path, Is.EqualTo("Assets/Scenes/EntityTestScene.unity"));
            Assert.That(session.State.Players.Count, Is.Zero);
            Assert.That(session.State.Furniture.Count, Is.EqualTo(5));
            Assert.That(Object.FindAnyObjectByType<WorldDataObject>().RuntimeState, Is.SameAs(session.State));
            var remoteState = new WorldState();
            using var remote = new GameClient(new TcpClientTransport(), remoteState, new MemoryClientIdentityStore());
            IEnumerator Pump(Func<bool> done)
            {
                float deadline = Time.realtimeSinceStartup + 20;
                while (!done())
                {
                    remote.Update();
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), session.Status);
                    yield return null;
                }
            }
            int port = FreePort();
            string profile = "entity-test-" + Guid.NewGuid().ToString("N");
            session.Connect(true, "127.0.0.1", port, profile);
            yield return Pump(() => session.IsReady && session.State.Players.Count == 1);
            Assert.That(session.State.Players[0].OwnerId.Value, Is.EqualTo(session.ClientId));
            var connect = remote.ConnectAsync("127.0.0.1", port);
            yield return Pump(() => connect.IsCompleted);
            connect.GetAwaiter().GetResult();
            yield return Pump(() => remote.IsReady && remoteState.Players.Count == 2 && session.State.Players.Count == 2);
            yield return null;
            Guid remoteId = remote.ClientId;
            var character = session.State.Players.Single(item => item.OwnerId.Value == remoteId);
            Assert.That(world.TryGetView(character.Id.Value, out var view), Is.True);
            Assert.That(view, Is.TypeOf<PlayerEntity>());
            var position = character.Cell.Value;
            var instance = view.GetEntityId();
            CapturePreview();
            var disconnect = remote.DisconnectAsync();
            yield return Pump(() => disconnect.IsCompleted && !session.State.Planning.Value.Participants[remoteId].Connected);
            disconnect.GetAwaiter().GetResult();
            Assert.That(session.State.Players.Count, Is.EqualTo(2));
            Assert.That(world.TryGetView(character.Id.Value, out view), Is.True);
            Assert.That(view.GetEntityId(), Is.EqualTo(instance));
            connect = remote.ConnectAsync("127.0.0.1", port);
            yield return Pump(() => connect.IsCompleted && remote.IsReady && session.State.Planning.Value.Participants[remoteId].Connected);
            connect.GetAwaiter().GetResult();
            yield return Pump(() => remoteState.Players.Count == 2);
            Assert.That(remote.ClientId, Is.EqualTo(remoteId));
            Assert.That(session.State.Players.Count, Is.EqualTo(2));
            Assert.That(session.State.Players.Single(item => item.OwnerId.Value == remoteId), Is.SameAs(character));
            Assert.That(character.Cell.Value, Is.EqualTo(position));
            Assert.That(world.TryGetView(character.Id.Value, out view), Is.True);
            Assert.That(view.GetEntityId(), Is.EqualTo(instance));
            Assert.That(Object.FindObjectsByType<PlayerEntity>().Length, Is.EqualTo(2));
            disconnect = remote.DisconnectAsync();
            yield return Pump(() => disconnect.IsCompleted && !session.State.Planning.Value.Participants[remoteId].Connected);
            session.Disconnect();
            yield return Pump(() => !session.IsBusy && !session.IsReady);
            session.Connect(true, "127.0.0.1", port, profile);
            yield return Pump(() => session.IsReady && session.State.Planning.Value.Participants[session.ClientId].Connected);
            Assert.That(session.State.Players.Count, Is.EqualTo(2), "Restarting the host session must also retain existing characters.");
            session.Disconnect();
            yield return Pump(() => !session.IsBusy);
        }

        [UnityTest]
        public IEnumerator ReadyDuringSceneChangeWaitsForDestinationCatalog()
        {
            return ReadyDuringTransition("Assets/Scenes/Test.unity");
        }

        [UnityTest]
        public IEnumerator ReadyDuringSameSceneReloadWaitsForNewRevision()
        {
            return ReadyDuringTransition("Assets/Scenes/EntityTestScene.unity");
        }

        private static IEnumerator ReadyDuringTransition(string destination)
        {
            var session = Object.FindAnyObjectByType<GameSessionController>();
            session.Connect(true, "127.0.0.1", FreePort(), "entity-transition-" + Guid.NewGuid().ToString("N"));
            float deadline = Time.realtimeSinceStartup + 20;
            while (!session.IsReady || session.State.Players.Count != 1)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), session.Status);
                yield return null;
            }
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var request = new ClientCommand(new ClientConnection(session.ClientId, Guid.NewGuid(), Guid.NewGuid(), false),
                new CommandMessage
                {
                    Name = "scene", Payload = JsonGameSerializer.Serialize(new SceneState
                    { Path = destination, Revision = session.State.Scene.Value.Revision })
                });
            Assert.That(typeof(GameSessionController).GetMethod("ProcessCommand", flags).Invoke(session, new object[] { request }), Is.Null);
            Guid newcomer = Guid.NewGuid();
            // The network event queue may drain a Ready event after the scene command in the same Update.
            typeof(GameSessionController).GetMethod("OnPeerReady", flags).Invoke(session,
                new object[] { new ClientConnection(newcomer, Guid.NewGuid(), Guid.NewGuid(), false) });
            Assert.That(session.State.Players, Is.Empty, "The old scene must not seed an avatar into the replacement world.");
            deadline = Time.realtimeSinceStartup + 20;
            while (!session.IsReady || session.State.Scene.Value.Path != destination || session.State.Scene.Value.Revision != 1)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), session.Status);
                yield return null;
            }
            Assert.That(session.State.Furniture.Count, Is.EqualTo(destination.EndsWith("EntityTestScene.unity") ? 5 : 2));
            if (destination.EndsWith("EntityTestScene.unity"))
            {
                Assert.That(session.State.Players.Count, Is.EqualTo(2));
                Assert.That(session.State.Players.Any(item => item.OwnerId.Value == newcomer), Is.True);
            }
            session.Disconnect();
            while (session.IsBusy) yield return null;
        }
        private static void CapturePreview()
        {
            var camera = Camera.main;
            var texture = new RenderTexture(1280, 800, 24);
            var pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory(".utmp/Entities");
                File.WriteAllBytes(".utmp/Entities/scene-preview.png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                Object.Destroy(pixels);
                Object.Destroy(texture);
            }
        }
    }
}

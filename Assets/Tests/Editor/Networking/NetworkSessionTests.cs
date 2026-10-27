using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using Assets.Shared.SyncSystem.Core;
using Assets.Shared.SyncSystem.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ServerCore.Networking.Tests
{
    public sealed class NetworkSessionTests
    {
        private sealed class State : TrackableNode { public Sync<int> Value = new Sync<int>(0); public SyncList<int> Items = new SyncList<int>(); }

        [UnityTest]
        public IEnumerator ConnectedEventHasUsableIdentity()
        {
            using (var server = new GameServer(new TcpHostTransport()))
            using (var client = new GameClient(new TcpClientTransport(), new State()))
            {
                var port = FreePort();
                var start = server.StartAsync("127.0.0.1", port);
                yield return Wait(start, client);
                Guid observed = Guid.Empty;
                client.ConnectedToHost += () => observed = client.ClientId;
                var connect = client.ConnectAsync("127.0.0.1", port, default);
                yield return Wait(connect, client);
                yield return Until(() => client.ClientId != Guid.Empty, client);
                Assert.That(observed, Is.Not.EqualTo(Guid.Empty), "Connection event must follow identity handshake.");
                yield return Wait(client.DisconnectAsync(), client);
                yield return Wait(server.StopAsync(), client);
            }
        }

        [UnityTest]
        public IEnumerator ReconnectRetainsIdentity()
        {
            using (var server = new GameServer(new TcpHostTransport()))
            using (var client = new GameClient(new TcpClientTransport(), new State()))
            {
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), client);
                yield return Wait(client.ConnectAsync("127.0.0.1", port, default), client);
                yield return Until(() => client.ClientId != Guid.Empty, client);
                var id = client.ClientId;
                yield return Wait(client.DisconnectAsync(), client);
                yield return Wait(client.ConnectAsync("127.0.0.1", port, default), client);
                yield return Until(() => client.ClientId != Guid.Empty, client);
                Assert.That(client.ClientId, Is.EqualTo(id));
                yield return Wait(client.DisconnectAsync(), client);
                yield return Wait(server.StopAsync(), client);
            }
        }

        [Test]
        public void DisconnectedSendReportsFailure()
        {
            using (var transport = new TcpClientTransport())
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await transport.SendAsync(Guid.Empty, NetworkPacketCodec.Pack(MessageType.SnapshotRequest, new SnapshotRequestMessage())));
        }

        [UnityTest]
        public IEnumerator SnapshotAndPatchesSurviveReconnectWithNewObjects()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ServerCoreNetworkTests", Guid.NewGuid().ToString("N"));
            try
            {
                var registryPath = System.IO.Path.Combine(directory, "registry.json");
                var clientPath = System.IO.Path.Combine(directory, "client.json");
                var hostState = new State();
                var remoteState = new State();
                var registry = new FileClientRegistry(registryPath);
                using (var server = new GameServer(new TcpHostTransport(), registry))
                using (var host = new GameClient(new TcpClientTransport(), hostState))
                using (var remote = new GameClient(new TcpClientTransport(), remoteState, new FileClientIdentityStore(clientPath)))
                {
                    var port = FreePort();
                    yield return Wait(server.StartAsync("127.0.0.1", port), server);
                    yield return Wait(host.ConnectAsync("127.0.0.1", port), server, host);
                    hostState.Value.Value = 7;
                    hostState.Items.Add(3);
                    yield return Wait(remote.ConnectAsync("127.0.0.1", port), server, host, remote);
                    Assert.That(remoteState.Value.Value, Is.EqualTo(7));
                    Assert.That(remoteState.Items.Count, Is.EqualTo(1));
                    Assert.That(remoteState.Items[0], Is.EqualTo(3));
                    Assert.That(hostState.Items.Count, Is.EqualTo(1), "Own list echo must not duplicate an item.");
                    remoteState.Value.Value = 9;
                    yield return Until(() => hostState.Value.Value == 9, server, host, remote);
                    yield return Wait(remote.SendPatchAsync(new PatchMessage { ChangeData = new ChangeData { Path = "Value", NewValue = 11 } }), server, host, remote);
                    yield return Until(() => hostState.Value.Value == 11 && remoteState.Value.Value == 11, server, host, remote);
                    var identity = remote.ClientId;
                    var connection = remote.Connection.ConnectionId;
                    yield return Wait(remote.DisconnectAsync(), server, host, remote);
                    yield return Until(() => server.ConnectedClients.Count == 1, server, host);
                    using (var resumed = new GameClient(new TcpClientTransport(), new State(), new FileClientIdentityStore(clientPath)))
                    {
                        int reconnected = 0;
                        resumed.Reconnected += _ => reconnected++;
                        yield return Wait(resumed.ConnectAsync("127.0.0.1", port), server, host, resumed);
                        Assert.That(resumed.ClientId, Is.EqualTo(identity));
                        Assert.That(resumed.Connection.ConnectionId, Is.Not.EqualTo(connection));
                        Assert.That(reconnected, Is.EqualTo(1));
                        Assert.That(server.KnownClients.Count, Is.EqualTo(2));
                        yield return Wait(resumed.DisconnectAsync(), server, host, resumed);
                    }
                    yield return Wait(host.DisconnectAsync(), host, server);
                    yield return Wait(server.StopAsync(), server);
                    Assert.That(new FileClientRegistry(registryPath).KnownClients.Count, Is.EqualTo(2));
                }
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [UnityTest]
        public IEnumerator ServerEventsRunOnlyOnUpdateAndUsePersistentIdentity()
        {
            using (var server = new GameServer(new TcpHostTransport()))
            using (var client = new GameClient(new TcpClientTransport(), new State()))
            {
                int count = 0, observedThread = 0;
                ClientConnection observed = null;
                server.ClientConnected += connection => { count++; observed = connection; observedThread = System.Threading.Thread.CurrentThread.ManagedThreadId; };
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), client);
                yield return Wait(client.ConnectAsync("127.0.0.1", port), client);
                Assert.That(count, Is.Zero, "Background IO must not invoke manager callbacks.");
                server.Update();
                Assert.That(count, Is.EqualTo(1));
                Assert.That(observedThread, Is.EqualTo(System.Threading.Thread.CurrentThread.ManagedThreadId));
                Assert.That(observed.ClientId, Is.EqualTo(client.ClientId));
                Assert.That(observed.ConnectionId, Is.Not.EqualTo(client.ClientId));
                yield return Wait(client.DisconnectAsync(), server, client);
                yield return Wait(server.StopAsync(), server);
            }
        }

        [UnityTest]
        public IEnumerator DuplicateLiveIdentityIsRejectedWithoutKickingOriginal()
        {
            var store = new MemoryClientIdentityStore();
            using (var server = new GameServer(new TcpHostTransport()))
            using (var first = new GameClient(new TcpClientTransport(), new State(), store))
            using (var duplicate = new GameClient(new TcpClientTransport(), new State(), store))
            {
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(first.ConnectAsync("127.0.0.1", port), server, first);
                var attempt = duplicate.ConnectAsync("127.0.0.1", port);
                yield return Until(() => attempt.IsCompleted, server, first, duplicate);
                Assert.That(attempt.IsFaulted, Is.True);
                _ = attempt.Exception;
                Assert.That(first.IsReady, Is.True);
                Assert.That(server.ConnectedClients.Count, Is.EqualTo(1));
                Assert.That(server.KnownClients.Count, Is.EqualTo(1));
                yield return Wait(first.DisconnectAsync(), server, first);
                yield return Wait(server.StopAsync(), server);
            }
        }

        [UnityTest]
        public IEnumerator AlreadyConnectedCallDoesNotDisconnectActiveSession()
        {
            using (var server = new GameServer(new TcpHostTransport()))
            using (var client = new GameClient(new TcpClientTransport(), new State()))
            {
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(client.ConnectAsync("127.0.0.1", port), server, client);
                var repeated = client.ConnectAsync("127.0.0.1", port);
                yield return Until(() => repeated.IsCompleted, server, client);
                Assert.That(repeated.IsFaulted, Is.True);
                _ = repeated.Exception;
                Assert.That(client.IsReady, Is.True);
                Assert.That(server.ConnectedClients.Count, Is.EqualTo(1));
                yield return Wait(client.DisconnectAsync(), server, client);
                yield return Wait(server.StopAsync(), server);
            }
        }

        [UnityTest]
        public IEnumerator TimeoutCanBeFollowedBySuccessfulConnection()
        {
            using (var raw = new TcpHostTransport())
            using (var client = new GameClient(new TcpClientTransport(), new State()))
            using (var server = new GameServer(new TcpHostTransport()))
            {
                yield return Wait(raw.StartAsync("127.0.0.1", 0), client);
                client.ConnectionTimeout = TimeSpan.FromMilliseconds(200);
                var attempt = client.ConnectAsync("127.0.0.1", raw.BoundPort);
                yield return Until(() => attempt.IsCompleted, client);
                Assert.That(attempt.IsCanceled, Is.True);
                yield return Wait(raw.StopAsync(), client);
                client.ConnectionTimeout = TimeSpan.FromSeconds(10);
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(client.ConnectAsync("127.0.0.1", port), server, client);
                Assert.That(client.IsReady, Is.True);
                yield return Wait(client.DisconnectAsync(), server, client);
                yield return Wait(server.StopAsync(), server);
            }
        }

        [UnityTest]
        public IEnumerator DisablingSenderEchoStillAllowsSubsequentSnapshot()
        {
            var source = new State();
            var target = new State();
            using (var server = new GameServer(new TcpHostTransport()) { EchoAuthoritativePatchesToSender = false })
            using (var host = new GameClient(new TcpClientTransport(), source))
            using (var remote = new GameClient(new TcpClientTransport(), target))
            {
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(host.ConnectAsync("127.0.0.1", port), server, host);
                source.Items.Add(42);
                remote.ConnectionTimeout = TimeSpan.FromSeconds(2);
                yield return Wait(remote.ConnectAsync("127.0.0.1", port), server, host, remote);
                Assert.That(target.Items.Count, Is.EqualTo(1));
                Assert.That(target.Items[0], Is.EqualTo(42));
                yield return Wait(remote.DisconnectAsync(), server, host, remote);
                yield return Wait(host.DisconnectAsync(), server, host);
                yield return Wait(server.StopAsync(), server);
            }
        }

        [UnityTest]
        public IEnumerator RestartResetsPatchSequenceAndRecognizesOldIdentity()
        {
            var registry = new MemoryClientRegistry();
            var state = new State();
            using (var server = new GameServer(new TcpHostTransport(), registry))
            using (var client = new GameClient(new TcpClientTransport(), state))
            {
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(client.ConnectAsync("127.0.0.1", port), server, client);
                var identity = client.ClientId;
                var session = client.Connection.ServerSessionId;
                yield return Wait(server.BroadcastServerPatchAsync(new PatchMessage { ChangeData = new ChangeData { Path = "Value", NewValue = 10 } }), server, client);
                yield return Until(() => state.Value.Value == 10, server, client);
                yield return Wait(server.StopAsync(), server, client);
                yield return Until(() => !client.IsConnected, server, client);
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(client.ConnectAsync("127.0.0.1", port), server, client);
                Assert.That(client.ClientId, Is.EqualTo(identity));
                Assert.That(client.Connection.IsReconnect, Is.True);
                Assert.That(client.Connection.ServerSessionId, Is.Not.EqualTo(session));
                yield return Wait(server.BroadcastServerPatchAsync(new PatchMessage { ChangeData = new ChangeData { Path = "Value", NewValue = 20 } }), server, client);
                yield return Until(() => state.Value.Value == 20, server, client);
                yield return Wait(client.DisconnectAsync(), server, client);
                yield return Wait(server.StopAsync(), server);
            }
        }
        private sealed class DelayedCleanupTransport : ITransport
        {
            public event Action<Guid> Connected { add { } remove { } }
            public event Action<Guid> Disconnected { add { } remove { } }
            public event Action<Guid, ArraySegment<byte>> DataReceived { add { } remove { } }
            public System.Collections.Generic.IReadOnlyCollection<Guid> Clients => Array.Empty<Guid>();
            public bool Started;
            public bool CleanupRequested;
            public readonly TaskCompletionSource<bool> Cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int stops;
            public Task StartAsync(string address, int port, System.Threading.CancellationToken token = default) { Started = true; return Task.CompletedTask; }
            public Task StopAsync(System.Threading.CancellationToken token = default)
            {
                if (System.Threading.Interlocked.Increment(ref stops) == 3) { CleanupRequested = true; return Cleanup.Task; }
                return Task.CompletedTask;
            }
            public Task SendAsync(Guid id, ArraySegment<byte> data, System.Threading.CancellationToken token = default) => Task.CompletedTask;
            public Task BroadcastAsync(ArraySegment<byte> data, System.Threading.CancellationToken token = default) => Task.CompletedTask;
            public void Dispose() { }
        }

        [UnityTest]
        public IEnumerator DisconnectJoinsCancelledConnectCleanup()
        {
            var transport = new DelayedCleanupTransport();
            var client = new GameClient(transport, new State());
            try
            {
                var connect = client.ConnectAsync("unused", 1);
                yield return Until(() => transport.Started, client);
                var disconnect = client.DisconnectAsync();
                yield return Until(() => transport.CleanupRequested, client);
                Assert.That(disconnect.IsCompleted, Is.False, "Disconnect must await failed connect cleanup.");
                transport.Cleanup.TrySetResult(true);
                yield return Wait(disconnect, client);
                Assert.That(connect.IsCompleted, Is.True);
                _ = connect.Exception;
            }
            finally { transport.Cleanup.TrySetResult(true); client.Dispose(); }
        }

        [UnityTest]
        public IEnumerator RepeatedSnapshotRequestsShareOneSynchronization()
        {
            var source = new State();
            var target = new State();
            using (var server = new GameServer(new TcpHostTransport()))
            using (var host = new GameClient(new TcpClientTransport(), source))
            using (var remote = new GameClient(new TcpClientTransport(), target))
            {
                var port = FreePort();
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(host.ConnectAsync("127.0.0.1", port), server, host);
                yield return Wait(remote.ConnectAsync("127.0.0.1", port), server, host, remote);
                var first = remote.RequestSnapshotAsync();
                var second = remote.RequestSnapshotAsync();
                Assert.That(first, Is.SameAs(second), "Concurrent refreshes must join the same snapshot operation.");
                yield return Wait(first, server, host, remote);
                Assert.That(remote.IsReady, Is.True, "Snapshot Task must complete after state application.");
                source.Value.Value = 101;
                yield return Until(() => target.Value.Value == 101, server, host, remote);
                yield return Wait(remote.DisconnectAsync(), server, host, remote);
                yield return Wait(host.DisconnectAsync(), server, host);
                yield return Wait(server.StopAsync(), server);
            }
        }
        private static async Task RestartImmediately(GameClient client, int port)
        {
            await client.DisconnectAsync().ConfigureAwait(false);
            await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
        }

        [UnityTest]
        public IEnumerator ReconnectDoesNotRequireUpdateBetweenStopAndStart()
        {
            using (var server = new GameServer(new TcpHostTransport()))
            using (var client = new GameClient(new TcpClientTransport(), new State()))
            {
                var port = FreePort();
                int disconnected = 0, reconnected = 0;
                client.Disconnected += _ => disconnected++;
                client.Reconnected += _ => reconnected++;
                yield return Wait(server.StartAsync("127.0.0.1", port), server);
                yield return Wait(client.ConnectAsync("127.0.0.1", port), server, client);
                yield return Wait(RestartImmediately(client, port), server, client);
                Assert.That(disconnected, Is.EqualTo(1));
                Assert.That(reconnected, Is.EqualTo(1));
                Assert.That(client.IsReady, Is.True);
                yield return Wait(client.DisconnectAsync(), server, client);
                yield return Wait(server.StopAsync(), server);
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

        private static IEnumerator Wait(Task task, params object[] clients)
        {
            yield return Until(() => task.IsCompleted, clients);
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator Until(Func<bool> condition, params object[] clients)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                foreach (var client in clients) { if (client is GameClient c) c.Update(); if (client is GameServer s) s.Update(); }
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Network operation timed out.");
                yield return null;
            }
            foreach (var client in clients) { if (client is GameClient c) c.Update(); if (client is GameServer s) s.Update(); }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Network.NetCore;
using Assets.Shared.SyncSystem.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ServerCore.Networking.Tests
{
    public sealed class ProtocolSessionTests
    {
        private sealed class State : TrackableNode { public Sync<int> Value = new Sync<int>(0); }
        private sealed class Sent
        {
            public Guid Peer;
            public MessageType Type;
            public byte[] Payload;
        }
        private sealed class ScriptedTransport : ITransport
        {
            public event Action<Guid> Connected;
            public event Action<Guid> Disconnected;
            public event Action<Guid, ArraySegment<byte>> DataReceived;
            public readonly ConcurrentQueue<Sent> Sent = new ConcurrentQueue<Sent>();
            public Action<Sent> OnSend;
            private readonly HashSet<Guid> peers = new HashSet<Guid>();
            public IReadOnlyCollection<Guid> Clients => peers.ToArray();
            public Task StartAsync(string a, int p, CancellationToken ct = default) => Task.CompletedTask;
            public Task StopAsync(CancellationToken ct = default) { foreach (var p in peers.ToArray()) Drop(p); return Task.CompletedTask; }
            public void Join(Guid p) { peers.Add(p); Connected?.Invoke(p); }
            public void Drop(Guid p) { if (peers.Remove(p)) Disconnected?.Invoke(p); }
            public void Deliver<T>(Guid p, MessageType type, T body) => DataReceived?.Invoke(p, NetworkPacketCodec.Pack(type, body));
            public Task SendAsync(Guid p, ArraySegment<byte> data, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(NetworkPacketCodec.TryUnpack(data, out var type, out var payload, out _), Is.True);
                var sent = new Sent { Peer = p, Type = type, Payload = payload };
                Sent.Enqueue(sent);
                OnSend?.Invoke(sent);
                return Task.CompletedTask;
            }
            public Task BroadcastAsync(ArraySegment<byte> data, CancellationToken ct = default) => Task.CompletedTask;
            public void Dispose() { }
        }

        private static ScriptedTransport ClientTransport()
        {
            var transport = new ScriptedTransport();
            transport.OnSend = packet =>
            {
                if (packet.Type != MessageType.ClientHello) return;
                var hello = JsonGameSerializer.Deserialize<ClientHelloMessage>(packet.Payload);
                transport.Join(Guid.Empty);
                transport.Deliver(Guid.Empty, MessageType.Handshake, new HandshakeMessage
                {
                    ProtocolVersion = NetworkProtocol.Version, ClientId = hello.ClientId, ConnectionId = Guid.NewGuid(),
                    ServerSessionId = Guid.NewGuid(), IsHost = true
                });
            };
            return transport;
        }

        [Test]
        public void SynchronousStopThenConnectKeepsDisconnectEventAndAllowsNewSession()
        {
            using (var client = new GameClient(ClientTransport(), new State()))
            {
                var first = client.ConnectAsync("test", 1);
                client.Update();
                first.GetAwaiter().GetResult();
                int disconnected = 0;
                client.Disconnected += _ => disconnected++;
                client.DisconnectAsync().GetAwaiter().GetResult();
                Assert.That(client.IsReady, Is.False);
                var next = client.ConnectAsync("test", 1);
                client.Update();
                next.GetAwaiter().GetResult();
                Assert.That(client.IsReady, Is.True);
                Assert.That(disconnected, Is.EqualTo(1));
            }
        }

        [Test]
        public void AckReconcilesLocalScalarAfterEarlierCompetingPatch()
        {
            var transport = ClientTransport();
            var state = new State();
            using (var client = new GameClient(transport, state))
            {
                var connect = client.ConnectAsync("test", 1);
                client.Update();
                connect.GetAwaiter().GetResult();
                state.Value.Value = 2;
                var local = JsonGameSerializer.Deserialize<PatchMessage>(transport.Sent.Last(p => p.Type == MessageType.Patch).Payload);
                transport.Deliver(Guid.Empty, MessageType.Patch, new PatchMessage { ChangeData = new ChangeData
                {
                    Path = "Value", NewValue = 1, ServerSequence = 1, SourceClientId = Guid.NewGuid()
                }});
                local.ChangeData.ServerSequence = 2;
                transport.Deliver(Guid.Empty, MessageType.PatchAcknowledgement, local);
                client.Update();
                Assert.That(state.Value.Value, Is.EqualTo(2), "Later authoritative ACK must reconcile the earlier remote write.");
            }
        }

        [UnityTest]
        public IEnumerator ProviderLossPreservesDeliveredSnapshotUntilAcknowledgement()
        {
            var transport = new ScriptedTransport();
            using (var server = new GameServer(transport))
            {
                yield return Await(server.StartAsync("test", 1), server);
                var source = Guid.NewGuid();
                var target = Guid.NewGuid();
                var sourceIdentity = ClientIdentity.Create();
                var targetIdentity = ClientIdentity.Create();
                Hello(transport, source, sourceIdentity);
                yield return Until(() => server.ConnectedClients.Count == 1, server);
                transport.Deliver(source, MessageType.ClientReady, new ClientReadyMessage());
                Hello(transport, target, targetIdentity);
                yield return Until(() => server.ConnectedClients.Count == 2, server);
                transport.Deliver(target, MessageType.SnapshotRequest, new SnapshotRequestMessage());
                yield return Until(() => transport.Sent.Any(p => p.Type == MessageType.SnapshotRequest), server);
                var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(transport.Sent.Last(p => p.Type == MessageType.SnapshotRequest).Payload);
                transport.Deliver(source, MessageType.Snapshot, new SnapshotMessage
                {
                    TargetClientId = targetIdentity.ClientId, RequestId = request.RequestId,
                    WorldDataPayload = "{}", ServerSequence = 0
                });
                yield return Until(() => transport.Sent.Any(p => p.Type == MessageType.Snapshot), server);
                transport.Drop(source);
                transport.Deliver(target, MessageType.ClientReady, new ClientReadyMessage { RequestId = request.RequestId });
                bool promoted = false;
                server.SnapshotProviderChanged += id => { if (id == targetIdentity.ClientId) promoted = true; };
                yield return Until(() => promoted || server.ConnectedClients.Count == 0, server);
                Assert.That(promoted, Is.True);
                Assert.That(server.ConnectedClients.Single().ClientId, Is.EqualTo(targetIdentity.ClientId));
                Assert.That(transport.Sent.Any(p => p.Type == MessageType.ConnectionRejected && p.Peer == target), Is.False);
                yield return Await(server.StopAsync(), server);
            }
        }

        private static void Hello(ScriptedTransport transport, Guid connection, ClientIdentity identity)
        {
            transport.Join(connection);
            transport.Deliver(connection, MessageType.ClientHello, new ClientHelloMessage
            {
                ProtocolVersion = NetworkProtocol.Version, ClientId = identity.ClientId, ReconnectToken = identity.ReconnectToken
            });
        }
        private static IEnumerator Await(Task task, GameServer server)
        {
            yield return Until(() => task.IsCompleted, server);
            task.GetAwaiter().GetResult();
        }
        private static IEnumerator Until(Func<bool> condition, GameServer server)
        {
            var time = Stopwatch.StartNew();
            while (!condition()) { server.Update(); Assert.That(time.Elapsed.TotalSeconds, Is.LessThan(5)); yield return null; }
            server.Update();
        }
    }
}

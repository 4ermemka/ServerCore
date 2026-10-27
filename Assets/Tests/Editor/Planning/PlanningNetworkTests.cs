using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using NUnit.Framework;
using ServerCore.World.Data;
using ServerCore.World.Planning;
using Shared.TurnPlanning;
using Shared.WorldObjects;
using UnityEngine.TestTools;

namespace ServerCore.Planning.Tests
{
    public sealed class PlanningNetworkTests
    {
        private sealed class Grid : IGridSpace { public bool Contains(GridCoordinate cell) => true; }
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        private static IEnumerator Until(Func<bool> done, params object[] pumps)
        {
            var time = Stopwatch.StartNew();
            while (!done())
            {
                Assert.That(time.Elapsed.TotalSeconds, Is.LessThan(15), "Network operation timed out.");
                foreach (var pump in pumps)
                    if (pump is GameClient client) client.Update(); else ((GameServer)pump).Update();
                yield return null;
            }
        }
        private static IEnumerator Wait(Task task, params object[] pumps)
        {
            yield return Until(() => task.IsCompleted, pumps);
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator CommandsUseAuthenticatedIdentity_ReplicatePlanAndTurn_ResumeSnapshot()
        {
            var world = new WorldState();
            var figure = new PlayerData();
            figure.Id.Value = "figure";
            figure.DefinitionId.Value = "player";
            world.Players.Add(figure);
            var registry = new ActionRegistry();
            registry.Register(new MovementAction());
            var authority = new WorldTurnAuthority(world, () => new Grid(), new TurnPlanner(registry, new OwnerAccessPolicy()));
            var remoteState = new WorldState();
            var remoteIdentity = new MemoryClientIdentityStore();
            var hostIdentity = new MemoryClientIdentityStore();
            using var server = new GameServer(new TcpHostTransport(), patchAuthority: hostIdentity.LoadOrCreate().ClientId);
            using var host = new GameClient(new TcpClientTransport(), world, hostIdentity) { ApplyOwnPatchEchoes = false };
            using var remote = new GameClient(new TcpClientTransport(), remoteState, remoteIdentity);
            object[] pumps = { server, host, remote };
            var sendReplies = new List<Task>();
            server.ClientReady += peer => authority.SetConnected(peer.ClientId, true);
            server.ClientDisconnected += peer => authority.SetConnected(peer.ClientId, false);
            server.CommandReceived += command =>
            {
                string error = authority.Apply(command.Connection.ClientId, JsonGameSerializer.Deserialize<PlanCommand>(command.Message.Payload));
                sendReplies.Add(ReplyAfterPublish(command, error));
            };
            async Task ReplyAfterPublish(ClientCommand command, string error)
            {
                await host.FlushLocalChangesAsync();
                await server.ReplyAsync(command, error);
            }
            int port = FreePort();
            yield return Wait(server.StartAsync("127.0.0.1", port), pumps);
            yield return Wait(host.ConnectAsync("127.0.0.1", port), pumps);
            yield return Wait(remote.ConnectAsync("127.0.0.1", port), pumps);
            yield return Until(() => remoteState.Planning.Value.Participants.Count == 2, pumps);
            var command = new PlanCommand
            {
                Kind = PlanCommandKind.Append, Turn = 1, ObjectId = "figure", ActionId = MovementAction.ActionId,
                Arguments = MovementAction.Arguments(new GridCoordinate(7, 0, 0))
            };
            CommandReply reply = null;
            remote.CommandReplied += value =>
            {
                reply = value;
                Assert.That(remoteState.Planning.Value.Plans.ContainsKey("figure"), Is.True,
                    "Reply must arrive after its world patch, so the next command can use the current tail.");
            };
            var request = new CommandMessage { Name = "plan", Payload = JsonGameSerializer.Serialize(command) };
            yield return Wait(remote.SendCommandAsync(request), pumps);
            yield return Until(() => reply != null && remoteState.Planning.Value.Plans.ContainsKey("figure"), pumps);
            Assert.That(reply.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(reply.Error, Is.Null);
            Assert.That(remoteState.Planning.Value.Plans["figure"].First.Value.ClientId, Is.EqualTo(remote.ClientId));
            Assert.That(world.Players[0].Cell.Value.X, Is.Zero);
            var clientId = remote.ClientId;
            yield return Wait(remote.DisconnectAsync(), pumps);
            yield return Until(() => !world.Planning.Value.Participants[clientId].Connected, pumps);

            {
                // A fresh Data instance proves restoration comes from the snapshot.
                var resumedWorld = new WorldState();
                using var reconnected = new GameClient(new TcpClientTransport(), resumedWorld, remoteIdentity);
                object[] nextPumps = { server, host, reconnected };
                yield return Wait(reconnected.ConnectAsync("127.0.0.1", port), nextPumps);
                yield return Until(() => resumedWorld.Planning.Value.Participants[clientId].Connected, nextPumps);
                Assert.That(reconnected.ClientId, Is.EqualTo(clientId));
                Assert.That(resumedWorld.Planning.Value.Plans["figure"].Count, Is.EqualTo(1));
                Assert.That(resumedWorld.Planning.Value.Participants[clientId].IsGameMaster, Is.True);
                PlanCommand Confirm() => new() { Kind = PlanCommandKind.Confirm, Turn = 1, Revision = world.Planning.Value.Revision };
                yield return Wait(host.SendCommandAsync(new CommandMessage { Name = "plan", Payload = JsonGameSerializer.Serialize(Confirm()) }), nextPumps);
                yield return Wait(reconnected.SendCommandAsync(new CommandMessage { Name = "plan", Payload = JsonGameSerializer.Serialize(Confirm()) }), nextPumps);
                yield return Until(() => resumedWorld.Planning.Value.Turn == 2, nextPumps);
                Assert.That(resumedWorld.Players[0].Cell.Value.X, Is.EqualTo(7));
                Assert.That(world.Players[0].Cell.Value.X, Is.EqualTo(7));
                Assert.That(resumedWorld.Planning.Value.Plans, Is.Empty);
                foreach (var sent in sendReplies) yield return Wait(sent, nextPumps);
                Exception rejected = null;
                reconnected.Error += error => rejected = error;
                yield return Wait(reconnected.SendPatchAsync(new PatchMessage
                {
                    ChangeData = new ChangeData { Path = "Players.[0].Cell", NewValue = new GridCoordinate(99, 0, 0) }
                }), nextPumps);
                yield return Until(() => rejected != null && !reconnected.IsConnected, nextPumps);
                Assert.That(world.Players[0].Cell.Value.X, Is.EqualTo(7), "Remote direct patches must not bypass authority.");
                yield return Wait(reconnected.DisconnectAsync(), nextPumps);
            }
            yield return Wait(host.DisconnectAsync(), server, host);
            yield return Wait(server.StopAsync(), server);
        }
    }
}

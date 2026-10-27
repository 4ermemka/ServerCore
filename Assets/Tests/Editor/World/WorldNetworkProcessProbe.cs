using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using ServerCore.World.Data;
using Shared.WorldObjects;
using UnityEditor;

namespace ServerCore.World.Tests
{
    /// <summary>
    /// Integration probe для двух отдельных Unity-процессов на loopback.
    /// -executeMethod ServerCore.World.Tests.WorldNetworkProcessProbe.Run
    /// -worldProbeRole host|client -worldProbePort N -worldProbeReport absolute-path
    /// Запускается в отдельных копиях проекта, не внутри открытого рабочего Editor.
    /// </summary>
    public static class WorldNetworkProcessProbe
    {
        public static void Run()
        {
            string report = Argument("-worldProbeReport");
            var previousContext = SynchronizationContext.Current;
            // Batch runner блокирует editor loop. Продолжения SendAsync должны освобождать
            // транспортные semaphore в thread pool, а не ждать этот заблокированный loop.
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                int port = int.Parse(Argument("-worldProbePort"));
                if (Argument("-worldProbeRole") == "host") Host(port); else Client(port);
                File.WriteAllText(report, "PASS: snapshot, bidirectional movement, collection add/remove, final acknowledgement.");
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "FAIL: " + error);
                EditorApplication.Exit(1);
                throw;
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }

        private static void Host(int port)
        {
            var state = new WorldState();
            var player = new PlayerData();
            player.Id.Value = "network-player";
            player.DefinitionId.Value = "player";
            state.Players.Add(player);
            state.Furniture.Add(Furniture("network-pile"));
            using var server = new GameServer(new TcpHostTransport());
            using var client = new GameClient(new TcpClientTransport(), state);
            // API транспорта асинхронный; Task.Run не даёт блокирующему batch runner
            // захватить UnitySynchronizationContext до завершения TCP-connect.
            Task.Run(() => server.StartAsync("127.0.0.1", port)).GetAwaiter().GetResult();
            Task.Run(() => client.ConnectAsync("127.0.0.1", port, CancellationToken.None)).GetAwaiter().GetResult();
            PumpUntil(client, () => client.IsSnapshotProvider, "host handshake");
            PumpUntil(client, () => player.Cell.Value == new GridCoordinate(3, 2, 1), "client movement");
            state.Furniture[0].Cell.Value = new GridCoordinate(4, 3, 2);
            state.Furniture.Add(Furniture("extra"));
            PumpUntil(client, () => state.Furniture.Count == 1 && player.Cell.Value == new GridCoordinate(5, 4, 3), "client removal and second movement");
            state.Furniture[0].Cell.Value = new GridCoordinate(6, 5, 4);
            PumpUntil(client, () => player.Cell.Value == new GridCoordinate(7, 6, 5), "final acknowledgement");
        }

        private static void Client(int port)
        {
            var state = new WorldState();
            using var client = new GameClient(new TcpClientTransport(), state);
            Task.Run(() => client.ConnectAsync("127.0.0.1", port, CancellationToken.None)).GetAwaiter().GetResult();
            PumpUntil(client, () => client.ClientId != Guid.Empty, "client handshake");
            Task.Run(() => client.RequestSnapshotAsync()).GetAwaiter().GetResult();
            PumpUntil(client, () => state.IsInitialized && state.Players.Count == 1 && state.Furniture.Count == 1, "snapshot");
            if (state.Players[0].Id.Value != "network-player" || state.Furniture[0].Id.Value != "network-pile")
                throw new InvalidOperationException("Snapshot object identity mismatch.");
            state.Players[0].Cell.Value = new GridCoordinate(3, 2, 1);
            PumpUntil(client, () => state.Furniture.Count == 2 && state.Furniture[0].Cell.Value == new GridCoordinate(4, 3, 2), "host movement and spawn");
            if (state.Furniture[1].Id.Value != "extra") throw new InvalidOperationException("JSON spawn did not preserve Id.");
            state.Furniture.RemoveAt(1);
            state.Players[0].Cell.Value = new GridCoordinate(5, 4, 3);
            PumpUntil(client, () => state.Furniture[0].Cell.Value == new GridCoordinate(6, 5, 4), "host acknowledgement");
            state.Players[0].Cell.Value = new GridCoordinate(7, 6, 5);
            bool echoed = false;
            state.Players[0].Cell.Patched += (_, _) => echoed = true;
            PumpUntil(client, () => echoed, "final patch echo");
        }

        private static FurnitureData Furniture(string id)
        {
            var data = new FurnitureData();
            data.Id.Value = id;
            data.DefinitionId.Value = "furniture.pile";
            return data;
        }

        private static void PumpUntil(GameClient client, Func<bool> predicate, string stage)
        {
            var time = Stopwatch.StartNew();
            while (!predicate())
            {
                if (time.Elapsed.TotalSeconds > 90) throw new TimeoutException(stage);
                client.Update();
                Thread.Sleep(10);
            }
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException(name);
        }
    }
}

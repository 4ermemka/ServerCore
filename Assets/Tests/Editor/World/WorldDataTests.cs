using System;
using System.Collections.Generic;
using Assets.Scripts.Network.NetCore;
using NUnit.Framework;
using ServerCore.World.Data;
using Shared.WorldObjects;

namespace ServerCore.World.Tests
{
    public sealed class WorldDataTests
    {
        private sealed class FakeGrid : IGridSpace
        {
            public bool Enabled = true;
            public bool Contains(GridCoordinate p) => Enabled && p.X >= 0 && p.X < 10;
        }
        // Альтернативная модель подтверждает, что move core не зависит от Sync/Unity.
        private sealed class FakeObject : IGridPositioned { public GridCoordinate Position { get; set; } }

        [Test]
        public void PreviewDoesNotMutate_CommitChangesOnce()
        {
            var data = Player("a", default);
            int changes = 0;
            data.Cell.Changed += (_, _, _) => changes++;
            var move = new GridMoveSession<PlayerData>(data, new FakeGrid());
            var target = new GridCoordinate(2, 3, 4);
            Assert.That(move.Preview(target, out _), Is.True);
            Assert.That(data.Cell.Value, Is.EqualTo(default(GridCoordinate)));
            Assert.That(move.Commit(out _), Is.True);
            Assert.That(data.Cell.Value, Is.EqualTo(target));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(move.Commit(out _), Is.False);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void CommitRechecksGridAndPolicy()
        {
            var data = new FakeObject();
            var grid = new FakeGrid();
            bool allowed = true;
            var move = new GridMoveSession<FakeObject>(data, grid, (_, _) => allowed ? null : "locked");
            Assert.That(move.Preview(new GridCoordinate(1, 0, 0), out _), Is.True);
            allowed = false;
            Assert.That(move.Commit(out var error), Is.False);
            Assert.That(error, Is.EqualTo("locked"));
            allowed = true;
            move = new GridMoveSession<FakeObject>(data, grid);
            Assert.That(move.Preview(new GridCoordinate(2, 0, 0), out _), Is.True);
            grid.Enabled = false;
            Assert.That(move.Commit(out _), Is.False);
            Assert.That(data.Position, Is.EqualTo(default(GridCoordinate)));
        }

        [Test]
        public void CancelAndConcurrentChangesNeverOverwriteData()
        {
            var data = new FakeObject();
            var move = new GridMoveSession<FakeObject>(data, new FakeGrid());
            move.Preview(new GridCoordinate(2, 0, 0), out _);
            move.Cancel();
            Assert.That(move.Commit(out _), Is.False);
            move = new GridMoveSession<FakeObject>(data, new FakeGrid());
            move.Preview(new GridCoordinate(2, 0, 0), out _);
            data.Position = new GridCoordinate(3, 0, 0);
            Assert.That(move.Commit(out _), Is.False);
            Assert.That(data.Position.X, Is.EqualTo(3));
        }

        [Test]
        public void InvalidPreviewCannotCommitPreviousValidCandidate()
        {
            var item = new FakeObject();
            var move = new GridMoveSession<FakeObject>(item, new FakeGrid());
            Assert.That(move.Preview(new GridCoordinate(1, 0, 0), out _), Is.True);
            Assert.That(move.Preview(new GridCoordinate(-1, 0, 0), out _), Is.False);
            Assert.That(move.Commit(out _), Is.False);
            Assert.That(item.Position, Is.EqualTo(default(GridCoordinate)));
        }

        [Test]
        public void MoveProducesSingleAtomicPath_JsonPatchDoesNotEcho()
        {
            var source = new WorldState();
            source.Players.Add(Player("a", default));
            var target = new WorldState();
            target.ApplySnapshot(JsonRoundTrip(source.CreateSnapshot()));
            int local = 0, echoed = 0, patched = 0;
            target.Changed += (_, _, _) => echoed++;
            target.Players[0].Cell.Patched += (_, _) => patched++;
            source.Changed += (path, _, value) =>
            {
                local++;
                Assert.That(path, Is.EqualTo("Players.[0].Cell"));
                target.ApplyPatch(path, JsonGameSerializer.Deserialize<object>(JsonGameSerializer.Serialize(value)));
            };
            source.Players[0].Cell.Value = new GridCoordinate(-5, 2, 17);
            Assert.That(target.Players[0].Cell.Value, Is.EqualTo(source.Players[0].Cell.Value));
            Assert.That(local, Is.EqualTo(1));
            Assert.That(patched, Is.EqualTo(1));
            Assert.That(echoed, Is.Zero);
        }

        [Test]
        public void JsonCollectionAddAndRemoveRetainConcreteTypeAndTracking()
        {
            var source = new WorldState();
            var target = new WorldState();
            int echoes = 0;
            target.Changed += (_, _, _) => echoes++;
            source.Changed += (path, _, value) => target.ApplyPatch(path,
                value == null ? null : JsonGameSerializer.Deserialize<object>(JsonGameSerializer.Serialize(value)));
            source.Players.Add(Player("p", new GridCoordinate(1, 2, 3)));
            var furniture = new FurnitureData();
            furniture.Id.Value = "f";
            furniture.DefinitionId.Value = "pile";
            source.Furniture.Add(furniture);
            Assert.That(target.Players[0].Id.Value, Is.EqualTo("p"));
            Assert.That(target.Players[0].Cell.Value, Is.EqualTo(new GridCoordinate(1, 2, 3)));
            Assert.That(target.Furniture[0].DefinitionId.Value, Is.EqualTo("pile"));
            source.Players[0].Cell.Value = new GridCoordinate(8, 2, 3);
            Assert.That(target.Players[0].Cell.Value.X, Is.EqualTo(8));
            source.Players.RemoveAt(0);
            Assert.That(target.Players.Count, Is.Zero);
            Assert.That(echoes, Is.Zero);
        }

        [Test]
        public void SnapshotReplacesBothCollections_IncludingEmptyWorld()
        {
            var state = new WorldState();
            state.Players.Add(Player("old1", default));
            state.Players.Add(Player("old2", default));
            state.Furniture.Add(new FurnitureData());
            int changes = 0;
            state.Changed += (_, _, _) => changes++;
            var source = new WorldState();
            source.Players.Add(Player("new", new GridCoordinate(1, 2, 3)));
            state.ApplySnapshot(JsonRoundTrip(source.CreateSnapshot()));
            Assert.That(state.Players.Count, Is.EqualTo(1));
            Assert.That(state.Players[0].Id.Value, Is.EqualTo("new"));
            Assert.That(state.Furniture.Count, Is.Zero);
            state.ApplySnapshot(JsonRoundTrip(new WorldState().CreateSnapshot()));
            Assert.That(state.Players.Count, Is.Zero);
            Assert.That(state.Furniture.Count, Is.Zero);
            Assert.That(state.IsInitialized, Is.True);
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void LegacySnapshotRejectedBeforeMutatingState()
        {
            var state = new WorldState();
            var original = Player("original", default);
            state.Players.Add(original);
            Assert.Throws<InvalidOperationException>(() => state.ApplySnapshot(new Dictionary<string, object> { ["Boxes.[0].Position"] = "old" }));
            Assert.That(state.Players[0], Is.SameAs(original));
        }

        private static PlayerData Player(string id, GridCoordinate cell)
        {
            var result = new PlayerData();
            result.Id.Value = id;
            result.DefinitionId.Value = "player";
            result.Cell.Value = cell;
            return result;
        }
        private static Dictionary<string, object> JsonRoundTrip(Dictionary<string, object> data)
            => JsonGameSerializer.Deserialize<Dictionary<string, object>>(JsonGameSerializer.Serialize(data));
    }
}

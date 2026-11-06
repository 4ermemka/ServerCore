using System;
using System.Collections.Generic;
using Assets.Scripts.Network.NetCore;
using NUnit.Framework;
using ServerCore.World.Data;
using Shared.WorldObjects;

namespace ServerCore.World.Tests
{
    public sealed class EntityOwnershipTests
    {
        [Test]
        public void RepeatedJoinAndSnapshotReuseTheOwnerWithoutInvokingFactory()
        {
            var state = new WorldState();
            Guid owner = Guid.NewGuid();
            int created = 0;
            PlayerData Create()
            {
                created++;
                var player = new PlayerData();
                player.Id.Value = "player:" + owner.ToString("N");
                player.DefinitionId.Value = "entity.player";
                player.Cell.Value = new GridCoordinate(3, 0, -2);
                return player;
            }
            var first = OwnedObjectRegistry.GetOrCreate(state.Players, owner, Create);
            first.DisplayName.Value = "Retained character";
            Assert.That(OwnedObjectRegistry.GetOrCreate(state.Players, owner, Create), Is.SameAs(first));
            var snapshot = JsonGameSerializer.Deserialize<Dictionary<string, object>>(JsonGameSerializer.Serialize(state.CreateSnapshot()));
            state.ApplySnapshot(snapshot);
            var restored = OwnedObjectRegistry.GetOrCreate(state.Players, owner, Create);
            Assert.That(restored.OwnerId.Value, Is.EqualTo(owner));
            Assert.That(restored.Id.Value, Is.EqualTo(first.Id.Value));
            Assert.That(restored.Cell.Value, Is.EqualTo(first.Cell.Value));
            Assert.That(restored.DisplayName.Value, Is.EqualTo("Retained character"));
            Assert.That(state.Players.Count, Is.EqualTo(1));
            Assert.That(created, Is.EqualTo(1));
        }

        [Test]
        public void DifferentIdentitiesGetDifferentCharactersAndInvalidFactoryDoesNotMutateList()
        {
            var state = new WorldState();
            PlayerData Create(string id) { var player = new PlayerData(); player.Id.Value = id; return player; }
            var a = OwnedObjectRegistry.GetOrCreate(state.Players, Guid.NewGuid(), () => Create("a"));
            var b = OwnedObjectRegistry.GetOrCreate(state.Players, Guid.NewGuid(), () => Create("b"));
            Assert.That(a, Is.Not.SameAs(b));
            Assert.Throws<InvalidOperationException>(() => OwnedObjectRegistry.GetOrCreate(state.Players, Guid.NewGuid(), () => Create("a")));
            Assert.Throws<ArgumentException>(() => OwnedObjectRegistry.GetOrCreate(state.Players, Guid.Empty, () => Create("c")));
            Assert.That(state.Players.Count, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateOwnerIsReportedInsteadOfSilentlyChoosingACharacter()
        {
            var state = new WorldState();
            Guid owner = Guid.NewGuid();
            foreach (var id in new[] { "a", "b" })
            {
                var player = new PlayerData(); player.Id.Value = id; player.OwnerId.Value = owner;
                state.Players.Add(player);
            }
            Assert.Throws<InvalidOperationException>(() => OwnedObjectRegistry.GetOrCreate(state.Players, owner, () => new PlayerData()));
            Assert.That(state.Players.Count, Is.EqualTo(2));
        }
    }
}

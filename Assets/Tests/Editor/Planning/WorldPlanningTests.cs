using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Network.NetCore;
using NUnit.Framework;
using ServerCore.World.Data;
using ServerCore.World.Planning;
using Shared.TurnPlanning;
using Shared.WorldObjects;

namespace ServerCore.Planning.Tests
{
    public sealed class WorldPlanningTests
    {
        private sealed class Grid : IGridSpace { public bool Contains(GridCoordinate cell) => cell.X >= 0 && cell.X < 100; }
        private readonly Guid a = Guid.NewGuid();
        private readonly Guid b = Guid.NewGuid();
        private WorldState world;
        private WorldTurnAuthority authority;

        [SetUp]
        public void Setup()
        {
            world = new WorldState();
            var player = new PlayerData();
            player.Id.Value = "figure";
            player.DefinitionId.Value = "player";
            world.Players.Add(player);
            var actions = new ActionRegistry();
            actions.Register(new MovementAction());
            authority = new WorldTurnAuthority(world, () => new Grid(), new TurnPlanner(actions, new OwnerAccessPolicy()));
            authority.SetConnected(a, true);
            authority.SetConnected(b, true);
        }

        private PlanCommand Move(int x) => new()
        {
            Kind = PlanCommandKind.Append, Turn = world.Planning.Value.Turn, ObjectId = "figure", ActionId = MovementAction.ActionId,
            ExpectedTail = world.Planning.Value.Plans.TryGetValue("figure", out var plan) ? plan.Last?.Value.Id : null,
            Arguments = MovementAction.Arguments(new GridCoordinate(x, 0, 0))
        };
        private PlanCommand Confirm() => new() { Kind = PlanCommandKind.Confirm, Turn = world.Planning.Value.Turn, Revision = world.Planning.Value.Revision };

        [Test]
        public void MovesStayPlannedUntilEveryoneConfirms_ThenPublishOneAtomicWorldPatch()
        {
            Assert.That(authority.Apply(a, Move(2)), Is.Null);
            Assert.That(authority.Apply(b, Move(5)), Is.Null);
            Assert.That(world.Players[0].Cell.Value.X, Is.Zero);
            Assert.That(authority.Apply(a, Confirm()), Is.Null);
            int patches = 0;
            string path = null;
            world.Changed += (changed, _, _) => { patches++; path = changed; };
            Assert.That(authority.Apply(b, Confirm()), Is.Null);
            Assert.That(world.Players[0].Cell.Value.X, Is.EqualTo(5));
            Assert.That(world.Planning.Value.Turn, Is.EqualTo(2));
            Assert.That(world.Planning.Value.Plans, Is.Empty);
            Assert.That(patches, Is.EqualTo(1));
            Assert.That(path, Is.EqualTo("$world"));
        }

        [Test]
        public void FailedActionCannotPartiallyMoveWorld()
        {
            authority.Planner.Actions.Register(new DelegatePlanAction("failure", (_, _) => null, (copy, _) =>
            {
                copy.SetParameter("figure", "bad", "partial");
                throw new InvalidOperationException("test failure");
            }));
            authority.Apply(a, Move(4));
            var command = Move(6);
            command.ActionId = "failure";
            authority.Apply(b, command);
            authority.Apply(a, Confirm());
            Assert.That(authority.Apply(b, Confirm()), Does.Contain("test failure"));
            Assert.That(world.Players[0].Cell.Value.X, Is.Zero);
            Assert.That(world.Players[0].Parameters.Value.ContainsKey("bad"), Is.False);
            Assert.That(world.Planning.Value.Turn, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotRestoresLinkedPlansSceneParametersAndStableAuthors()
        {
            authority.Apply(a, Move(4));
            authority.Apply(b, Move(8));
            authority.SetConnected(b, false);
            world.Scene.Value = new SceneState { Path = "Assets/Scenes/Other.unity", Revision = 7 };
            world.Players[0].Parameters.Value = new Dictionary<string, string> { ["health"] = "12" };
            world.Players[0].Transform.Value = new ObjectTransformData { RotationY = 90, ScaleX = 2 };
            var copy = new WorldState();
            copy.ApplySnapshot(JsonGameSerializer.Deserialize<Dictionary<string, object>>(JsonGameSerializer.Serialize(world.CreateSnapshot())));
            var plan = copy.Planning.Value.Plans["figure"];
            Assert.That(plan.First.Next.Previous, Is.SameAs(plan.First));
            Assert.That(plan.Last.Value.ClientId, Is.EqualTo(b));
            Assert.That(copy.Scene.Value.Path, Is.EqualTo(world.Scene.Value.Path));
            Assert.That(copy.Scene.Value.Revision, Is.EqualTo(7));
            Assert.That(copy.Players[0].Parameters.Value["health"], Is.EqualTo("12"));
            Assert.That(copy.Players[0].Transform.Value.RotationY, Is.EqualTo(90));
            var resumed = authority.Planner.SetConnected(copy.Planning.Value, b, true, true);
            Assert.That(resumed.Plans["figure"].Last.Value.Id, Is.EqualTo(plan.Last.Value.Id));
            Assert.That(resumed.Participants[b].IsGameMaster, Is.True);
            Assert.That(resumed.Participants[b].Confirmed, Is.False);
        }

        [Test]
        public void DisconnectedParticipantDoesNotBlockConfirmedHost()
        {
            authority.Apply(b, Move(3));
            authority.Apply(a, Confirm());
            authority.SetConnected(b, false);
            Assert.That(world.Planning.Value.Turn, Is.EqualTo(2));
            Assert.That(world.Players[0].Cell.Value.X, Is.EqualTo(3));
        }

        [Test]
        public void ColorsDoNotRepeatBeyondInitialPalette()
        {
            for (int i = 0; i < 30; i++) Assert.That(authority.Apply(a, Move(i)), Is.Null);
            var values = world.Planning.Value.Plans["figure"].Select(s => s.ColorIndex).ToArray();
            Assert.That(values.Distinct().Count(), Is.EqualTo(values.Length));
        }

        [Test]
        public void InvalidMovementAndUnknownActionDoNotChangePlan()
        {
            Assert.That(authority.Apply(a, Move(-1)), Is.Not.Null);
            var command = Move(1);
            command.ActionId = "unregistered";
            Assert.That(authority.Apply(a, command), Is.Not.Null);
            Assert.That(world.Planning.Value.Plans, Is.Empty);
        }
    }
}

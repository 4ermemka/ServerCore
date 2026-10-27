using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Shared.TurnPlanning;

namespace ServerCore.Planning.Tests
{
    public sealed class TurnPlannerTests
    {
        private readonly Guid gm = Guid.NewGuid();
        private readonly Guid player = Guid.NewGuid();
        private TurnPlanner planner;

        [SetUp]
        public void Setup()
        {
            planner = new TurnPlanner(new ActionRegistry(), new OwnerAccessPolicy());
            planner.Actions.Register(new DelegatePlanAction("test", (world, step) => null,
                (world, step) => world.SetParameter(step.ObjectId, "last", step.Id)));
        }

        private PlanningState State()
        {
            var state = new PlanningState();
            state.Participants.Add(gm, new Participant { ClientId = gm, IsGameMaster = true, Connected = true });
            state.Participants.Add(player, new Participant { ClientId = player, Connected = true });
            return state;
        }

        private sealed class World : IPlanningWorld
        {
            public Dictionary<string, string> Parameters = new();
            public Guid Owner;
            public bool Contains(string id) => id == "figure";
            public Guid OwnerOf(string id) => Owner;
            public string GetParameter(string id, string key) => Parameters.TryGetValue(id, out var value) ? value : null;
            public void SetParameter(string id, string key, string value) => Parameters[id] = value;
        }

        private PlanCommand Append(PlanningState state, string tail = null) => new PlanCommand
        {
            Kind = PlanCommandKind.Append, Turn = state.Turn, ObjectId = "figure", ActionId = "test", ExpectedTail = tail
        };

        [Test]
        public void LinkedPlanPreservesAuthorAndOrder_StaleTailCannotOverwrite()
        {
            var state = State();
            var world = new World { Owner = player };
            Assert.That(planner.TryApply(state, world, gm, Append(state), out var first, out _), Is.True);
            Assert.That(state.Plans.Count, Is.Zero, "Source snapshot must stay immutable.");
            var a = first.Plans["figure"].First.Value;
            Assert.That(planner.TryApply(first, world, player, Append(first, a.Id), out var second, out _), Is.True);
            var b = second.Plans["figure"].Last.Value;
            Assert.That(b.ClientId, Is.EqualTo(player));
            Assert.That(b.Sequence, Is.GreaterThan(a.Sequence));
            Assert.That(second.Plans["figure"].First.Next.Previous, Is.SameAs(second.Plans["figure"].First));
            Assert.That(planner.TryApply(second, world, gm, Append(second, a.Id), out _, out _), Is.False);
        }

        [Test]
        public void CancelRemovesSelectedStepAndEverySuccessor_ResetsConfirmations()
        {
            var world = new World { Owner = player };
            var state = State();
            planner.TryApply(state, world, gm, Append(state), out state, out _);
            string first = state.Plans["figure"].Last.Value.Id;
            planner.TryApply(state, world, gm, Append(state, first), out state, out _);
            state.Participants[player].Confirmed = true;
            Assert.That(planner.TryApply(state, world, gm, new PlanCommand
            {
                Kind = PlanCommandKind.Truncate, Turn = state.Turn, ObjectId = "figure",
                StepId = first, ExpectedTail = state.Plans["figure"].Last.Value.Id
            }, out state, out _), Is.True);
            Assert.That(state.Plans["figure"].Count, Is.Zero);
            Assert.That(state.Participants[player].Confirmed, Is.False);
        }

        [Test]
        public void OwnerPolicyRestrictsPlayersButEveryGmCanEdit()
        {
            var world = new World { Owner = Guid.NewGuid() };
            Assert.That(planner.TryApply(State(), world, player, Append(State()), out _, out _), Is.False);
            Assert.That(planner.TryApply(State(), world, gm, Append(State()), out _, out _), Is.True);
        }

        [Test]
        public void ConfirmationRequiresCurrentRevision_OfflineParticipantsDoNotBlock()
        {
            var state = State();
            state.Participants[player].Connected = false;
            var world = new World();
            planner.TryApply(state, world, gm, Append(state), out state, out _);
            var confirm = new PlanCommand { Kind = PlanCommandKind.Confirm, Turn = state.Turn, Revision = state.Revision - 1 };
            Assert.That(planner.TryApply(state, world, gm, confirm, out _, out _), Is.False);
            confirm.Revision = state.Revision;
            Assert.That(planner.TryApply(state, world, gm, confirm, out state, out _), Is.True);
            Assert.That(planner.CanExecute(state), Is.True);
            var actions = planner.OrderedActions(state).ToArray();
            Assert.That(actions.Length, Is.EqualTo(1));
            planner.Actions.Get(actions[0].ActionId).Execute(world, actions[0]);
            var next = planner.NextTurn(state);
            Assert.That(next.Turn, Is.EqualTo(state.Turn + 1));
            Assert.That(next.Plans.Count, Is.Zero);
            Assert.That(planner.TryApply(next, world, gm, confirm, out _, out _), Is.False);
        }
    }
}

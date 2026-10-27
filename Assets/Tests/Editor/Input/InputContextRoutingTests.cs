using System;
using NUnit.Framework;
using Shared.Inputs;
using UnityEngine;

namespace ServerCore.Input.Tests
{
    public class InputContextRoutingTests
    {
        private class Context : InputContext
        {
            public int Count;
            public Context(GameActionRouter router, bool handles) : base(router, null)
            { if (handles) Actions.Add(new InputAction("a", () => Count++)); }
        }

        private sealed class FaultyContext : Context
        {
            public FaultyContext(GameActionRouter router) : base(router, false) { }
            public override void Init() => throw new InvalidOperationException("init failed");
        }
        private sealed class SubscribingFault : InputContext
        {
            private readonly System.Action observed;
            private readonly bool missingAction;
            public SubscribingFault(GameActionRouter source, System.Action observed, bool missingAction) : base(source, null)
            { this.observed = observed; this.missingAction = missingAction; }
            public override void Init()
            {
                SubscribeToAction("a", observed);
                if (missingAction) Actions.Add(new InputAction("missing", () => { }));
                else throw new InvalidOperationException("init failed after acquiring a resource");
            }
        }
        private sealed class ScrollContext : InputContext
        {
            public float Total;
            public ScrollContext(IGameActionSource source) : base(source, null) { }
            public override bool TryHandleScroll(Vector2 delta) { Total += delta.y; return true; }
        }
        private sealed class ScrollSource : IInputSource, IScrollInputSource
        {
            public Vector2 ScrollDelta { get; set; }
            public bool AnyHeld => ScrollDelta != Vector2.zero;
            public InputSample Read(InputBinding binding) => default;
            public bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding) { binding = default; return false; }
        }
        [Test]
        public void UnboundWheelReachesContextButModalSuspensionStillWins()
        {
            var router = new GameActionRouter(Array.Empty<GameActionDefinition>());
            var context = new ScrollContext(router); router.Contexts.SetRoot(context);
            var input = new ScrollSource { ScrollDelta = new Vector2(0, -.5f) };
            router.Tick(input); Assert.That(context.Total, Is.EqualTo(-.5f));
            using (router.Suspend()) { router.Tick(input); Assert.That(context.Total, Is.EqualTo(-.5f)); }
            router.Tick(input); Assert.That(context.Total, Is.EqualTo(-.5f), "Neutral guard survives suspension.");
            input.ScrollDelta = Vector2.zero; router.Tick(input);
            input.ScrollDelta = Vector2.up; router.Tick(input);
            Assert.That(context.Total, Is.EqualTo(.5f));
            router.Contexts.Dispose();
        }
        private sealed class LeavingContext : InputContext
        {
            private readonly InputContextChain chain;
            public LeavingContext(GameActionRouter source) : base(source, null) => chain = source.Contexts;
            public override bool TryHandleAction(string id) { chain.Pop(); return false; }
        }
        [Test]
        public void ReturningDuringDispatchDoesNotReplaySignalInPreviousContext()
        {
            var router = new GameActionRouter(new[] { new GameActionDefinition("a", "A", ActionTrigger.Press, new InputBinding(KeyCode.A)) });
            var root = new Context(router, true); router.Contexts.SetRoot(root);
            router.Contexts.Enter(new LeavingContext(router));
            int observed = 0; using var observer = router.GetAction("a").Subscribe(() => observed++);
            var source = new FakeInputSource(); source.Press(KeyCode.A); router.Tick(source);
            Assert.That(router.Contexts.Current, Is.SameAs(root));
            Assert.That(root.Count, Is.Zero); Assert.That(observed, Is.Zero);
            router.Tick(source); Assert.That(root.Count, Is.EqualTo(1));
            router.Contexts.Dispose();
        }
        [TestCase(false)]
        [TestCase(true)]
        public void FailedEnterDisposesResourcesEvenBeforeOnEntered(bool missingAction)
        {
            var router = new GameActionRouter(new[] { new GameActionDefinition("a", "A", ActionTrigger.Press, new InputBinding(KeyCode.A)) });
            router.Contexts.SetRoot(new Context(router, false));
            int observed = 0;
            var faulty = new SubscribingFault(router, () => observed++, missingAction);
            try
            {
                Assert.Throws<InvalidOperationException>(() => router.Contexts.Enter(faulty));
                var input = new FakeInputSource(); input.Press(KeyCode.A); router.Tick(input);
                Assert.That(observed, Is.Zero, "Failed initialization must release acquired subscriptions.");
                Assert.That(faulty.IsDisposed, Is.True);
            }
            finally { faulty.Dispose(); router.Contexts.Dispose(); }
        }
        [Test]
        public void ManagerKeepsOldInputSourceWhenNewSourceRootFails()
        {
            var a = new GameActionRouter(Array.Empty<GameActionDefinition>());
            var b = new GameActionRouter(Array.Empty<GameActionDefinition>());
            var go = new GameObject("context manager replacement");
            try
            {
                var manager = go.AddComponent<InputContextManager>();
                var root = new Context(a, false); manager.SetRootContext(root);
                Assert.Throws<InvalidOperationException>(() => manager.SetRootContext(new FaultyContext(b)));
                Assert.That(manager.CurrentContext, Is.SameAs(root));
                Assert.That(a.Contexts.Current, Is.SameAs(root));
                Assert.That(root.IsDisposed, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [Test]
        public void FailedRootReplacementPreservesPreviousChain()
        {
            var router = new GameActionRouter(Array.Empty<GameActionDefinition>());
            var root = new Context(router, false);
            router.Contexts.SetRoot(root);
            Assert.Throws<InvalidOperationException>(() => router.Contexts.SetRoot(new FaultyContext(router)));
            Assert.That(router.Contexts.Current, Is.SameAs(root));
            Assert.That(root.IsDisposed, Is.False);
            router.Contexts.Dispose();
        }
        [Test]
        public void RemovingMiddleClosesDescendantsAndStaleTokenCannotPopNewContext()
        {
            var router = new GameActionRouter(Array.Empty<GameActionDefinition>());
            var chain = router.Contexts;
            var root = new Context(router, false);
            var middle = new Context(router, false);
            var top = new Context(router, false);
            chain.SetRoot(root);
            var middleToken = chain.Enter(middle);
            var topToken = chain.Enter(top);
            Assert.That(root.Next, Is.SameAs(middle)); Assert.That(middle.Next, Is.SameAs(top));
            Assert.That(top.Previous, Is.SameAs(middle));
            middleToken.Dispose();
            Assert.That(chain.Current, Is.SameAs(root)); Assert.That(root.Next, Is.Null);
            Assert.That(top.Previous, Is.Null); Assert.That(middle.IsDisposed && top.IsDisposed, Is.True);
            var replacement = new Context(router, false); chain.Enter(replacement);
            topToken.Dispose(); middleToken.Dispose();
            Assert.That(chain.Current, Is.SameAs(replacement));
            chain.Pop(); Assert.That(chain.Pop(), Is.False);
            chain.Dispose();
        }

        [Test]
        public void UnhandledActionReachesPreviousContext()
        {
            var router = new GameActionRouter(new[] { new GameActionDefinition("a", "A", ActionTrigger.Press, new InputBinding(KeyCode.A)) });
            var go = new GameObject("contexts");
            try
            {
                var manager = go.AddComponent<InputContextManager>();
                var root = new Context(router, true);
                manager.SetRootContext(root);
                manager.AddContext(new Context(router, false));
                var source = new FakeInputSource(); source.Press(KeyCode.A);
                router.Tick(source);
                Assert.That(root.Count, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void HandledActionDoesNotReachOrdinarySubscribers()
        {
            var router = new GameActionRouter(new[] { new GameActionDefinition("a", "A", ActionTrigger.Press, new InputBinding(KeyCode.A)) });
            var go = new GameObject("contexts");
            try
            {
                var manager = go.AddComponent<InputContextManager>();
                var root = new Context(router, true);
                manager.SetRootContext(root);
                int count = 0;
                using var subscription = router.GetAction("a").Subscribe(() => count++);
                var source = new FakeInputSource(); source.Press(KeyCode.A); router.Tick(source);
                Assert.That(root.Count, Is.EqualTo(1));
                Assert.That(count, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Shared.Inputs;
using UnityEditor;
using UnityEngine;

namespace ServerCore.Input.Tests
{
    public sealed class FakeInputSource : IInputSource
    {
        public InputBinding Current;
        public bool Down;
        public bool AnyHeld { get; set; }
        public InputSample Read(InputBinding binding) => Current == binding ? new InputSample(Down, AnyHeld) : default;
        public bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding)
        {
            binding = Current;
            return Down && !binding.IsEmpty && (accept == null || accept(binding));
        }
        public void Press(KeyCode key) { Current = new InputBinding(key); Down = AnyHeld = true; }
        public void Release() { Down = AnyHeld = false; }
    }

    public sealed class InputCoreTests
    {
        private static GameActionDefinition[] Definitions() => new[]
        {
            new GameActionDefinition("a", "Press", ActionTrigger.Press, new InputBinding(KeyCode.A)),
            new GameActionDefinition("b", "Hold", ActionTrigger.Hold, new InputBinding(KeyCode.B))
        };
        [Test]
        public void RebindingIsExclusiveAndNotifiesOnlyAfterBothSidesChanged()
        {
            var table = new BindingTable(Definitions());
            int changes = 0;
            table.Changed += () => { changes++; Assert.That(table["a"].IsEmpty, Is.True); Assert.That(table["b"].Key, Is.EqualTo(KeyCode.A)); };
            table.Rebind("b", new InputBinding(KeyCode.A));
            Assert.That(changes, Is.EqualTo(1));
            table.Rebind("b", new InputBinding(KeyCode.A));
            Assert.That(changes, Is.EqualTo(1));
        }
        [Test]
        public void WheelBindingsAreExclusiveToo()
        {
            var table = new BindingTable(Definitions());
            table.Rebind("a", InputBinding.Wheel(true));
            table.Rebind("b", InputBinding.Wheel(true));
            Assert.That(table["a"].IsEmpty, Is.True);
            Assert.That(table["b"], Is.EqualTo(InputBinding.Wheel(true)));
            table.Rebind("a", InputBinding.Wheel(false));
            Assert.That(table["b"].IsEmpty, Is.False);
        }
        [Test]
        public void TriggerAndSubscribersSurviveRebind()
        {
            var router = new GameActionRouter(Definitions());
            var input = new FakeInputSource();
            int press = 0, hold = 0;
            var original = router.GetAction("a");
            using var subscriptionA = original.Subscribe(() => press++);
            using var subscriptionB = router.GetAction("b").Subscribe(() => hold++);
            input.Press(KeyCode.A); router.Tick(input);
            input.Down = false; router.Tick(input);
            Assert.That(press, Is.EqualTo(1));
            router.Bindings.Rebind("b", new InputBinding(KeyCode.A));
            router.Tick(input); // Всё ещё удерживается клавиша старого действия.
            Assert.That(hold, Is.Zero);
            input.Release(); router.Tick(input);
            input.Press(KeyCode.A); router.Tick(input);
            input.Down = false; router.Tick(input);
            Assert.That(hold, Is.EqualTo(2));
            Assert.That(press, Is.EqualTo(1));
            Assert.That(router.GetAction("a"), Is.SameAs(original));
            Assert.That(router.GetAction("b").Trigger, Is.EqualTo(ActionTrigger.Hold));
        }
        [Test]
        public void CaptureIgnoresOpeningClickAndDoesNotPerformAssignedAction()
        {
            var router = new GameActionRouter(Definitions());
            var source = new FakeInputSource();
            int performed = 0;
            router.GetAction("a").Performed += () => performed++;
            using var capture = new RebindCapture(router, "a");
            source.Press(KeyCode.Mouse0); capture.Tick(source);
            Assert.That(capture.IsActive, Is.True);
            source.Release(); capture.Tick(source);
            source.Press(KeyCode.B); capture.Tick(source);
            Assert.That(capture.IsActive, Is.False);
            Assert.That(router.Bindings["b"].IsEmpty, Is.True);
            router.Tick(source);
            Assert.That(performed, Is.Zero);
            source.Release(); router.Tick(source);
            source.Press(KeyCode.B); router.Tick(source);
            Assert.That(performed, Is.EqualTo(1));
        }
        [Test]
        public void CaptureCanAssignEscapeAndFilterUIClicks()
        {
            var router = new GameActionRouter(Definitions());
            var source = new FakeInputSource();
            using var capture = new RebindCapture(router, "a");
            capture.Tick(source);
            source.Press(KeyCode.Mouse0);
            capture.Tick(source, b => !b.IsMouseButton);
            Assert.That(capture.IsActive, Is.True);
            source.Press(KeyCode.Escape); capture.Tick(source);
            Assert.That(router.Bindings["a"].Key, Is.EqualTo(KeyCode.Escape));
        }
        [Test]
        public void NestedSuspensionsAndCancelledCaptureReleaseIndependently()
        {
            var router = new GameActionRouter(Definitions());
            var menu = router.Suspend();
            var capture = new RebindCapture(router, "b");
            capture.Dispose(); capture.Dispose();
            Assert.That(router.IsSuspended, Is.True);
            Assert.That(router.Bindings["b"].Key, Is.EqualTo(KeyCode.B));
            menu.Dispose(); menu.Dispose();
            Assert.That(router.IsSuspended, Is.False);
        }
        [Test]
        public void OpeningMenuStopsRemainingActionsInSameTick()
        {
            var router = new GameActionRouter(Definitions());
            var source = new AllPressedSource();
            int second = 0;
            IDisposable block = null;
            router.GetAction("a").Performed += () => block = router.Suspend();
            router.GetAction("b").Performed += () => second++;
            router.Tick(source);
            Assert.That(second, Is.Zero);
            block.Dispose();
        }
        private sealed class AllPressedSource : IInputSource
        {
            public bool AnyHeld => true;
            public InputSample Read(InputBinding binding) => new(true, true);
            public bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding) { binding = default; return false; }
        }
        [Test]
        public void InvalidCatalogFailsFast()
        {
            Assert.Throws<ArgumentException>(() => new GameActionRouter(new[] { Definitions()[0], Definitions()[0] }));
            Assert.Throws<ArgumentException>(() => new GameActionRouter(new[] { new GameActionDefinition("", "", ActionTrigger.Press, default) }));
        }
        [Test]
        public void EditorAssignmentToEarlierRowWinsAndUndoRestoresBothRows()
        {
            var profile = ScriptableObject.CreateInstance<GameInputProfile>();
            try
            {
                profile.Configure("editor-test", Definitions());
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(profile, "Rebind test");
                var so = new SerializedObject(profile);
                so.FindProperty("actions").GetArrayElementAtIndex(0).FindPropertyRelative("defaultBinding").FindPropertyRelative("key").intValue = (int)KeyCode.B;
                so.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Assert.That(profile.Actions[0].DefaultBinding.Key, Is.EqualTo(KeyCode.B));
                Assert.That(profile.Actions[1].DefaultBinding.IsEmpty, Is.True);
                Undo.PerformUndo();
                Assert.That(profile.Actions[0].DefaultBinding.Key, Is.EqualTo(KeyCode.A));
                Assert.That(profile.Actions[1].DefaultBinding.Key, Is.EqualTo(KeyCode.B));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
        [Test]
        public void RuntimeDoesNotModifyAuthoringAssetAndResetIsAtomic()
        {
            var defs = Definitions();
            var router = new GameActionRouter(defs);
            router.Bindings.Rebind("a", new InputBinding(KeyCode.B));
            Assert.That(defs[0].DefaultBinding.Key, Is.EqualTo(KeyCode.A));
            int events = 0;
            router.Bindings.Changed += () => { events++; Assert.That(router.Bindings["a"].Key, Is.EqualTo(KeyCode.A)); Assert.That(router.Bindings["b"].Key, Is.EqualTo(KeyCode.B)); };
            router.Bindings.ReplaceWith(new BindingTable(defs));
            Assert.That(events, Is.EqualTo(1));
        }
        [Test]
        public void SavedOverridesRoundTripAndTakePriorityOverNewDefaults()
        {
            string id = "input-test-" + Guid.NewGuid().ToString("N");
            try
            {
                var store = new PlayerPrefsBindingStore(id);
                store.Save(new[] { new BindingOverride { actionId = "a", binding = new InputBinding(KeyCode.B) },
                    new BindingOverride { actionId = "b", binding = default }, new BindingOverride { actionId = "removed", binding = new InputBinding(KeyCode.Z) } });
                var table = new BindingTable(Definitions());
                PlayerPrefsBindingStore.Apply(table, store.Load());
                Assert.That(table["a"].Key, Is.EqualTo(KeyCode.B));
                Assert.That(table["b"].IsEmpty, Is.True);
            }
            finally { PlayerPrefs.DeleteKey("input.bindings.v1." + id); PlayerPrefs.Save(); }
        }
        private sealed class TestContext : InputContext
        {
            public int Count;
            public TestContext(GameActionRouter router) : base(router, "cancel") { Actions.Add(new InputAction("a", () => Count++)); }
        }
        [Test]
        public void ContextStackDoesNotLeakSubscriptionsOrDisposeRootOnCancel()
        {
            var router = new GameActionRouter(new[] { Definitions()[0], new GameActionDefinition("cancel", "Cancel", ActionTrigger.Press, new InputBinding(KeyCode.Escape)) });
            var root = new TestContext(router);
            var child = new TestContext(router);
            var go = new GameObject("Context tests");
            try
            {
                var manager = go.AddComponent<InputContextManager>();
                var input = new FakeInputSource();
                manager.SetRootContext(root); root.Activate();
                input.Press(KeyCode.A); router.Tick(input);
                Assert.That(root.Count, Is.EqualTo(1));
                manager.AddContext(child);
                router.Tick(input);
                Assert.That(child.Count, Is.EqualTo(1)); Assert.That(root.Count, Is.EqualTo(1));
                input.Press(KeyCode.Escape); router.Tick(input);
                Assert.That(manager.CurrentContext, Is.SameAs(root));
                router.Tick(input); // Cancel на root не снимает его подписки.
                input.Press(KeyCode.A); router.Tick(input);
                Assert.That(root.Count, Is.EqualTo(2)); Assert.That(child.Count, Is.EqualTo(1));
                root.Dispose(); root.Dispose(); router.Tick(input);
                Assert.That(root.Count, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

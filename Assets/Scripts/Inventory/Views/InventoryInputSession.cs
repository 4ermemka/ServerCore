using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Inputs;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Общее владение UI-контекстами на один источник ввода. Регистрация симметрична OnEnable/OnDisable.
    /// Живёт, пока существует хотя бы один координатор; не удерживает закрытые сцены.
    /// </summary>
    internal sealed class InventoryInputSession
    {
        private static readonly Dictionary<InputContextChain, InventoryInputSession> sessions = new();
        private readonly List<InventoryInputCoordinator> views = new();
        private WindowContext windowContext;
        private DragContext dragContext;
        private IDisposable windowLease, dragLease;
        private bool reconciling;
        internal InputContextChain Chain { get; }
        internal bool IsOpen => windowContext != null && !windowContext.IsDisposed;

        private InventoryInputSession(InputContextChain chain)
        {
            Chain = chain;
            Chain.Changed += Reconcile;
            InventoryDragController.DragChanged += Reconcile;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var session in sessions.Values.ToArray()) session.Dispose();
            sessions.Clear();
        }
        internal static InventoryInputSession Register(InputContextChain chain, InventoryInputCoordinator view)
        {
            if (!sessions.TryGetValue(chain, out var session)) sessions.Add(chain, session = new InventoryInputSession(chain));
            if (!session.views.Contains(view)) session.views.Add(view);
            session.Reconcile();
            return session;
        }
        internal void Unregister(InventoryInputCoordinator view)
        {
            bool wasReconciling = reconciling;
            reconciling = true;
            try
            {
                if (view.Owns(InventoryDragController.SourceView)) InventoryDragController.Cancel();
                views.Remove(view);
                if (views.Count == 0) { Dispose(); sessions.Remove(Chain); }
            }
            finally { reconciling = wasReconciling; }
            if (views.Count > 0) Reconcile();
        }
        private void Dispose()
        {
            reconciling = true;
            Chain.Changed -= Reconcile;
            InventoryDragController.DragChanged -= Reconcile;
            windowLease?.Dispose(); dragLease?.Dispose();
            windowLease = dragLease = null; windowContext = null; dragContext = null;
        }
        private bool OwnsDrag => views.Any(view => view != null && view.Owns(InventoryDragController.SourceView));
        internal void Reconcile()
        {
            if (reconciling) return;
            reconciling = true;
            try
            {
                var source = InventoryDragController.SourceView;
                // Дочерняя вкладка может скрыться при переносе между страницами; корень обязан оставаться видимым.
                if (OwnsDrag && !views.Any(view => view != null && view.HasVisibleRootFor(source)))
                    InventoryDragController.Cancel();
                if (!views.Any(view => view != null && view.HasVisibleWindow))
                {
                    windowLease?.Dispose(); windowLease = null; windowContext = null;
                    dragLease?.Dispose(); dragLease = null; dragContext = null;
                    return;
                }
                if (Chain.Root == null) return;
                if (!IsOpen)
                {
                    windowContext = new WindowContext(this);
                    windowLease = Chain.Enter(windowContext);
                }
                if (OwnsDrag)
                {
                    if (dragContext == null || dragContext.IsDisposed)
                    {
                        dragContext = new DragContext(this);
                        dragLease = Chain.Enter(dragContext);
                    }
                }
                else { dragLease?.Dispose(); dragLease = null; dragContext = null; }
            }
            finally { reconciling = false; }
        }
        private bool IsCancel(string id) => views.Any(view => view != null && view.HasVisibleWindow && view.IsCancel(id));
        private sealed class WindowContext : InputContext
        {
            private readonly InventoryInputSession session;
            public WindowContext(InventoryInputSession session) : base(null, null) => this.session = session;
            public override bool TryHandleAction(string id)
            {
                if (session.IsCancel(id))
                {
                    foreach (var view in session.views.ToArray()) if (view != null) view.CloseWindows();
                    return true;
                }
                return session.views.Any(view => view != null && view.HasVisibleWindow && view.Blocks(id));
            }
            public override bool TryHandleScroll(Vector2 delta)
            {
                // Один raycast, но попадание может принадлежать любому зарегистрированному prefab-у.
                session.views.FirstOrDefault(view => view != null && view.HasVisibleWindow)?.Scroll(delta);
                return true;
            }
        }
        private sealed class DragContext : InputContext
        {
            private readonly InventoryInputSession session;
            public DragContext(InventoryInputSession session) : base(null, null) => this.session = session;
            public override bool TryHandleAction(string id)
            {
                if (!session.IsCancel(id)) return false;
                InventoryDragController.Cancel(); return true;
            }
            public override bool TryHandleScroll(Vector2 delta)
            {
                InventoryDragController.ScrollQuantity(delta); return true;
            }
            protected override void OnExited()
            {
                if (!session.OwnsDrag) return;
                // Chain находится внутри выхода; DragChanged не должен повторно входить в него.
                bool previous = session.reconciling; session.reconciling = true;
                try { InventoryDragController.Cancel(); }
                finally { session.reconciling = previous; }
            }
        }
    }
}

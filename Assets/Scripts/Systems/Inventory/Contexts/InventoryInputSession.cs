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
    internal sealed partial class InventoryInputSession
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
    }
}

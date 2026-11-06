using UnityEngine;

namespace ServerCore.Inventory
{
    internal sealed partial class InventoryInputSession
    {
        private sealed class DragContext : InputContext
        {
            private readonly InventoryInputSession session;
            public DragContext(InventoryInputSession session) : base(null, null) => this.session = session;
            public override bool TryHandleAction(string id)
            {
                if (!session.IsCancel(id))
                    return false;
                InventoryDragController.Cancel();
                return true;
            }

            public override bool TryHandleScroll(Vector2 delta)
            {
                InventoryDragController.ScrollQuantity(delta);
                return true;
            }

            protected override void OnExited()
            {
                if (!session.OwnsDrag)
                    return;
                // Chain находится внутри выхода; DragChanged не должен повторно входить в него.
                bool previous = session.reconciling;
                session.reconciling = true;
                try
                {
                    InventoryDragController.Cancel();
                }
                finally
                {
                    session.reconciling = previous;
                }
            }
        }
    }
}

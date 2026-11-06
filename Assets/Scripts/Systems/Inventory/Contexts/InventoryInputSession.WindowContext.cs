using System.Linq;
using UnityEngine;

namespace ServerCore.Inventory
{
    internal sealed partial class InventoryInputSession
    {
        private sealed class WindowContext : InputContext
        {
            private readonly InventoryInputSession session;
            public WindowContext(InventoryInputSession session) : base(null, null) => this.session = session;
            public override bool TryHandleAction(string id)
            {
                if (session.IsCancel(id))
                {
                    foreach (var view in session.views.ToArray())
                        if (view != null)
                            view.CloseWindows();
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
    }
}

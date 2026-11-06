using Shared.Inputs;
using UnityEngine;

namespace ServerCore.World.Presentation
{
    public sealed partial class GameSessionView
    {
        private sealed class MenuInput : InputContext
        {
            public MenuInput(IGameActionSource source) : base(source, null)
            {
            }

            public override bool TryHandleAction(string id) => true;
            public override bool TryHandleScroll(Vector2 delta) => true;
        }
    }
}

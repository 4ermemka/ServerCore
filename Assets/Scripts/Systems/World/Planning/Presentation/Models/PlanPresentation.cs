using System;
using Shared.TurnPlanning;
using Shared.WorldObjects;

namespace ServerCore.World.Presentation
{
    public sealed class PlanPresentation
    {
        public Func<PlannedAction, string> Label;
        public Func<PlannedAction, GridCoordinate, GridCoordinate> Project;
        public Func<PlanVisualContext, IDisposable> CreateVisual;
    }
}

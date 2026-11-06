using Shared.TurnPlanning;
using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World.Presentation
{
    public sealed class PlanVisualContext
    {
        public PlannedAction Step;
        public int Number;
        public Color Color;
        public GridCoordinate From;
        public WorldObjectView View;
        public Transform Root;
        public Material Material;
    }
}
